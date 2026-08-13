using System.Globalization;
using IO = System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SuspensionSimulation.Components;
using SuspensionSimulation.Configuration;
using SuspensionSimulation.Core;
using SuspensionSimulation.Core.Dashboard;
using SuspensionSimulation.Kinematics;
using SuspensionSimulation.Solvers;
using SuspensionSimulation.Solvers.Kinematics;

namespace SuspensionSimulation.App;

public partial class MainWindow : Window
{
    private double _ax, _ay;
    private string? _setupDirectory;
    private ModularSetup? _source;
    private GeometryBackedCaymanPackage? _geometrySource;
    private FourCornerMacPhersonGeometry? _hardPoints;
    private VehiclePackage? _package;
    private QuasiStaticResult? _result;
    private GeometryBackedStiffnessResult? _geometryStiffness;

    public MainWindow()
    {
        App.Log("Constructing MainWindow.");
        InitializeComponent();
        Loaded += (_, _) => Initialise();
        SizeChanged += (_, _) => Render();
    }

    private void Initialise()
    {
        _setupDirectory = IO.Path.Combine(RepositoryRoot(), "data", "synthetic", "cayman");
        _source = ModularSetupLoader.Load(_setupDirectory);
        // Fail at startup if the same fixture directory cannot supply geometry
        // travel. The editable dashboard package still rebuilds component rates.
        _geometrySource = GeometryBackedCaymanPackageLoader.Load(_setupDirectory);
        _hardPoints = FourCornerMacPhersonGeometryLoader.Load(_setupDirectory);
        var front = _source.Assembly.Corners.First(c => c.Corner is Corner.FL or Corner.FR);
        var rear = _source.Assembly.Corners.First(c => c.Corner is Corner.RL or Corner.RR);
        FrontSpring.Text = (_source.Springs.Single(s => s.Id == front.SpringId).LinearRateNPerM / 1000).ToString("G", CultureInfo.InvariantCulture);
        RearSpring.Text = (_source.Springs.Single(s => s.Id == rear.SpringId).LinearRateNPerM / 1000).ToString("G", CultureInfo.InvariantCulture);
        PopulateTubularInputs(_source.AntiRollBars.Single(a => a.Id == _source.Assembly.FrontAntiRollBarId), true);
        PopulateTubularInputs(_source.AntiRollBars.Single(a => a.Id == _source.Assembly.RearAntiRollBarId), false);
        CgHeight.Text = _source.Chassis.CgHeightM.ToString("G", CultureInfo.InvariantCulture);
        Wheelbase.Text = _source.Chassis.WheelbaseM.ToString("G", CultureInfo.InvariantCulture);
        FrontTrack.Text = _source.Chassis.FrontTrackM.ToString("G", CultureInfo.InvariantCulture);
        RearTrack.Text = _source.Chassis.RearTrackM.ToString("G", CultureInfo.InvariantCulture);
        var firstStop = _source.BumpStops.First();
        BumpStopGap.Text = (firstStop.EngagementGapM * 1000).ToString("G", CultureInfo.InvariantCulture);
        Render();
    }

    private void CenterGClick(object sender, RoutedEventArgs e) { _ax = 0; _ay = 0; Render(); }
    private void SetupChanged(object sender, RoutedEventArgs e) => Render();
    private void GCapChanged(object sender, RoutedEventArgs e) => Render();
    private void LegacyArbModeChanged(object sender, RoutedEventArgs e)
    {
        var legacy = UseLegacyDirectArb.IsChecked == true;
        LegacyDirectArbPanel.Visibility = legacy ? Visibility.Visible : Visibility.Collapsed;
        ArbModeBadge.Text = legacy ? "LEGACY DIRECT RATE — TEST/IMPORT ONLY" : "TUBULAR GEOMETRY (PRIMARY)";
        ArbModeBadge.Foreground = legacy ? Brushes.Goldenrod : Brushes.LightGreen;
        if (IsLoaded) Render();
    }
    private void GCirclePointer(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed && e.RoutedEvent != Mouse.MouseDownEvent) return;
        var p = e.GetPosition(GCircleCanvas); var r = Radius();
        var sweepCap = ReadPositive(SweepCap, "G axis sweep cap");
        _ay = (p.X - GCircleCanvas.ActualWidth / 2) / r * sweepCap;
        _ax = (p.Y - GCircleCanvas.ActualHeight / 2) / r * sweepCap;
        Render();
    }
    private void MetricChanged(object sender, RoutedEventArgs e) { if (IsLoaded) RenderMetrics(); }

    private void Render()
    {
        if (!IsLoaded || _source is null || _hardPoints is null) return;
        var radius = Radius(); var sweepCap = ReadPositive(SweepCap, "G axis sweep cap"); var totalGCap = ReadPositive(TotalGCap, "total G cap");
        var point = GCircleMapper.Map(_ax, _ay, radius, sweepCap, totalGCap); _ax = point.DisplayLongitudinalG; _ay = point.DisplayLateralG;
        LongitudinalReadout.Text = $"Longitudinal: {_ax:+0.00;-0.00;0.00} G";
        LateralReadout.Text = $"Lateral: {_ay:+0.00;-0.00;0.00} G";
        MagnitudeReadout.Text = $"Magnitude: {Math.Sqrt(_ax * _ax + _ay * _ay):0.00} / {totalGCap:0.00} G" + (point.WasClamped ? " (capped)" : "");
        try
        {
            _package = BuildPackage();
            _result = new FourStrutQuasiStaticSolver().Solve(_package, new LoadCase("dashboard", _ax, _ay, 0));
            _geometryStiffness = new FourCornerGeometryStiffnessSolver().Solve(_hardPoints, _result, BuildGeometryComponents(), _package.AntiRollBars);
            var activeWarning = _result.Warnings.FirstOrDefault(w => w.StartsWith("Active bump-stop branch", StringComparison.Ordinal) || w.Contains("non-positive normal load", StringComparison.Ordinal));
            var travelStatus = _geometryStiffness.IsGeometryBacked ? "Geometry/travel stiffness: solved from Cayman synthetic hard points." : $"Geometry/travel stiffness failed: {string.Join(" ", _geometryStiffness.Diagnostics.Select(x => x.Message))}";
            StatusText.Text = $"Solver residuals: Fz {_result.VerticalResidualN:E2} N; roll {_result.RollResidualNm:E2} Nm; pitch {_result.PitchResidualNm:E2} Nm. Input fingerprint: {_source.InputFingerprintSha256[..12]}. {travelStatus}" + (activeWarning is null ? "" : $" Warning: {activeWarning}");
            DrawG(point, radius); DrawCar(_result); RenderMetrics();
        }
        catch (Exception exception)
        {
            _result = null; _geometryStiffness = null; StatusText.Text = $"Setup input error: {exception.Message}"; DrawG(point, radius); CarCanvas.Children.Clear(); MetricsGrid.ItemsSource = null;
        }
    }

    private VehiclePackage BuildPackage()
    {
        if (_source is null) throw new InvalidOperationException("Setup was not loaded.");
        var frontSpring = ReadPositive(FrontSpring, "front spring") * 1000;
        var rearSpring = ReadPositive(RearSpring, "rear spring") * 1000;
        var bumpStopGapM = ReadNonNegative(BumpStopGap, "bump-stop gap") / 1000;
        var chassis = _source.Chassis with { CgHeightM = ReadNonNegative(CgHeight, "CG height"), WheelbaseM = ReadPositive(Wheelbase, "wheelbase"), FrontTrackM = ReadPositive(FrontTrack, "front track"), RearTrackM = ReadPositive(RearTrack, "rear track") };
        var springs = _source.Assembly.Corners.Select(c =>
        {
            var rawRate = c.Corner is Corner.FL or Corner.FR ? frontSpring : rearSpring;
            var stop = _source.BumpStops.Single(s => s.Id == c.BumpStopId);
            var firstSegment = (stop.ForceCurve[1].Y - stop.ForceCurve[0].Y) / (stop.ForceCurve[1].X - stop.ForceCurve[0].X);
            // The sidebar edits a common stop-compression gap. Per-corner gaps
            // remain owned by the component files until the editor exposes them.
            return new CornerSpring(c.Corner.ToString(), rawRate * c.MotionRatio * c.MotionRatio, bumpStopGapM / c.MotionRatio, firstSegment * c.MotionRatio * c.MotionRatio);
        }).ToList();
        return new VehiclePackage(
            new ChassisModel(chassis.Id, chassis.TotalMassKg, chassis.WheelbaseM, chassis.FrontTrackM, chassis.RearTrackM, chassis.CgHeightM, chassis.StaticFrontWeightFraction, "Mixed public/source + user-adjusted", chassis.SourceNote),
            new SpringsModel(springs, "User-adjusted", "Input is raw spring rate; solver maps it to wheel rate using the fixed configured motion ratio."),
            [BuildArb("front", _source.AntiRollBars.Single(a => a.Id == _source.Assembly.FrontAntiRollBarId), true),
             BuildArb("rear", _source.AntiRollBars.Single(a => a.Id == _source.Assembly.RearAntiRollBarId), false)],
            new DampingModel("Loaded, excluded", "Static solver does not use damper force."),
            new ModelScope("load-lab/0.3", ["flat road", "rigid body", "small angles", "quasi-static acceleration", "fixed spring motion ratio", "tubular ARB virtual-work stiffness from editable geometry", "first bump-stop tangent after engagement"], ["tire force/grip", "four-corner hard-point travel", "kinematic roll centers and jacking", "transient damping", "chassis/bushing compliance", "full bump-stop curve", "calibrated static bump-stop state"]));
    }

    private AntiRollBarModel BuildArb(string axle, AntiRollBarConfig source, bool front)
    {
        if (UseLegacyDirectArb.IsChecked == true)
        {
            var rate = ReadNonNegative(front ? FrontLegacyArb : RearLegacyArb, $"{axle} legacy direct ARB") * 1000;
            return new AntiRollBarModel(axle, rate, "Legacy user import", "Explicit direct-rate legacy/test mode.");
        }
        var input = ReadTubularInput(front);
        return AntiRollBarModel.FromConfig(axle, TubularArbEditor.ApplySymmetricInput(source, input), "User-adjusted tubular geometry");
    }

    private IReadOnlyList<GeometryCornerComponents> BuildGeometryComponents()
    {
        if (_source is null) throw new InvalidOperationException("Setup was not loaded.");
        var frontSpring = ReadPositive(FrontSpring, "front spring") * 1000;
        var rearSpring = ReadPositive(RearSpring, "rear spring") * 1000;
        var stopGap = ReadNonNegative(BumpStopGap, "bump-stop gap") / 1000;
        return _source.Assembly.Corners.Select(c =>
        {
            var rawRate = c.Corner is Corner.FL or Corner.FR ? frontSpring : rearSpring;
            var springConfig = _source.Springs.Single(x => x.Id == c.SpringId) with { LinearRateNPerM = rawRate, ForceCurve = null };
            var stopConfig = _source.BumpStops.Single(x => x.Id == c.BumpStopId) with { EngagementGapM = stopGap };
            return new GeometryCornerComponents(c.Corner.ToString(), new Spring(springConfig), new BumpStop(stopConfig));
        }).ToArray();
    }

    private SymmetricTubularArbInput ReadTubularInput(bool front)
    {
        var od = front ? FrontArbOd : RearArbOd; var id = front ? FrontArbId : RearArbId;
        var g = front ? FrontArbG : RearArbG; var length = front ? FrontArbLength : RearArbLength;
        var arm = front ? FrontArbArm : RearArbArm; var link = front ? FrontArbLink : RearArbLink;
        var angle = front ? FrontArbAngle : RearArbAngle; var motion = front ? FrontArbMr : RearArbMr;
        return new(ReadPositive(od, "ARB outside diameter") / 1000, ReadNonNegative(id, "ARB inside diameter") / 1000,
            ReadPositive(g, "ARB shear modulus") * 1e9, ReadPositive(length, "ARB torsion length"),
            ReadPositive(arm, "ARB lever arm") / 1000, ReadPositive(link, "ARB end-link length") / 1000,
            Read(angle, "ARB end-link angle") * Math.PI / 180, ReadNonNegative(motion, "ARB wheel-to-link motion ratio"));
    }

    private void PopulateTubularInputs(AntiRollBarConfig bar, bool front)
    {
        var t = bar.Tubular ?? throw new IO.InvalidDataException($"ARB '{bar.Id}' is missing tubular geometry.");
        var side = t.Left;
        Set(front ? FrontArbOd : RearArbOd, t.OutsideDiameterM * 1000); Set(front ? FrontArbId : RearArbId, t.InsideDiameterM * 1000);
        Set(front ? FrontArbG : RearArbG, t.ShearModulusPa / 1e9); Set(front ? FrontArbLength : RearArbLength, t.TorsionLengthM);
        Set(front ? FrontArbArm : RearArbArm, side.LeverArmM * 1000); Set(front ? FrontArbLink : RearArbLink, side.EndLinkLengthM * 1000);
        Set(front ? FrontArbAngle : RearArbAngle, side.EndLinkAngleFromVerticalRad * 180 / Math.PI); Set(front ? FrontArbMr : RearArbMr, side.WheelToEndLinkVerticalMotionRatio);
    }

    private void RenderMetrics()
    {
        if (_result is null || _package is null || _geometryStiffness is null) return;
        var selected = new HashSet<string>(StringComparer.Ordinal);
        if (Lltd.IsChecked == true) selected.Add("lltd"); if (Rsd.IsChecked == true) selected.Add("rsd"); if (Rcb.IsChecked == true) selected.Add("rcb"); if (Loads.IsChecked == true) selected.Add("loads"); if (AxleSummary.IsChecked == true) selected.Add("summary"); if (Attitude.IsChecked == true) selected.Add("attitude"); if (Stiffness.IsChecked == true) selected.Add("stiffness");
        var metrics = new List<DashboardMetric>();
        if (selected.Contains("lltd")) metrics.Add(new("lltd", "Front LLTD", "%", Percent(_result.LoadTransferDistributionFront), true, "estimated"));
        var balance = _geometryStiffness.Balance;
        if (selected.Contains("rsd"))
        {
            var validity = balance?.Validity.ToString().ToLowerInvariant() ?? "unavailable";
            var rsd = balance is null ? "Geometry tangent balance unavailable" : Percent(balance.FrontRollStiffnessDistribution);
            metrics.Add(new("rsd", "Front RSD (current geometry/travel tangent)", "%", rsd, true, validity));
            if (balance is not null)
            {
                AddAxleTangentMetrics(metrics, balance.Front); AddAxleTangentMetrics(metrics, balance.Rear);
            }
        }
        if (selected.Contains("rcb"))
        {
            var validity = balance?.Validity.ToString().ToLowerInvariant() ?? "unavailable";
            var rcb = balance is null ? "Geometry tangent balance unavailable" : balance.RollCoupleBiasPercentagePoints.ToString("F2", CultureInfo.InvariantCulture);
            metrics.Add(new("rcb", "RCB: geometry RSD − static front fraction", "percentage points", rcb, true, validity));
        }
        if (selected.Contains("loads")) foreach (var c in _result.Corners) metrics.Add(new($"load-{c.Corner}", $"{c.Corner} normal load", "N", c.NormalLoadN.ToString("F0", CultureInfo.InvariantCulture) + (c.IsLifted ? " (lift)" : ""), true, "estimated"));
        if (selected.Contains("summary"))
        {
            var fl = _result.Corners.Single(x => x.Corner == "FL").NormalLoadN; var fr = _result.Corners.Single(x => x.Corner == "FR").NormalLoadN;
            var rl = _result.Corners.Single(x => x.Corner == "RL").NormalLoadN; var rr = _result.Corners.Single(x => x.Corner == "RR").NormalLoadN;
            metrics.AddRange([new("front", "Front axle normal load", "N", (fl + fr).ToString("F0", CultureInfo.InvariantCulture), "estimated"), new("rear", "Rear axle normal load", "N", (rl + rr).ToString("F0", CultureInfo.InvariantCulture), "estimated"), new("left", "Left-side normal load", "N", (fl + rl).ToString("F0", CultureInfo.InvariantCulture), "estimated"), new("right", "Right-side normal load", "N", (fr + rr).ToString("F0", CultureInfo.InvariantCulture), "estimated")]);
        }
        if (selected.Contains("attitude"))
        {
            metrics.Add(new("roll", "Roll angle (0 = flat)", "deg", Degrees(_result.RollRad), "estimated"));
            metrics.Add(new("pitch", "Pitch angle (0 = flat)", "deg", Degrees(_result.PitchRad), "estimated"));
            foreach (var c in _result.Corners)
                metrics.Add(new($"ride-{c.Corner}", $"{c.Corner} reference wheel-center height delta", "mm", (-c.RelativeCompressionM * 1000).ToString("F2", CultureInfo.InvariantCulture), "estimated"));
        }
        if (selected.Contains("stiffness"))
        {
            foreach (var stiffness in _geometryStiffness.Corners)
            {
                var validity = stiffness.Validity.ToString().ToLowerInvariant();
                var failure = string.Join(" ", stiffness.Diagnostics.Select(x => x.Message));
                var total = double.IsFinite(stiffness.TotalSelfNPerM) ? (stiffness.TotalSelfNPerM / 1000).ToString("F2", CultureInfo.InvariantCulture) : failure;
                var spring = double.IsFinite(stiffness.SpringOnlyNPerM) ? (stiffness.SpringOnlyNPerM / 1000).ToString("F2", CultureInfo.InvariantCulture) : failure;
                var arb = double.IsFinite(stiffness.ArbSelfNPerM) ? (stiffness.ArbSelfNPerM / 1000).ToString("F2", CultureInfo.InvariantCulture) : failure;
                metrics.Add(new($"stiff-{stiffness.Corner}-total", $"{stiffness.Corner} geometry/travel total tangent", "N/mm", total, validity));
                metrics.Add(new($"stiff-{stiffness.Corner}-spring", $"{stiffness.Corner} geometry/travel spring tangent", "N/mm", spring, validity));
                metrics.Add(new($"stiff-{stiffness.Corner}-arb", $"{stiffness.Corner} geometry/travel ARB tangent", "N/mm", arb, validity));
            }
        }
        MetricsGrid.ItemsSource = metrics;
    }

    private void DrawG(GCirclePoint p, double r)
    {
        GCircleCanvas.Children.Clear(); var cx = GCircleCanvas.ActualWidth / 2; var cy = GCircleCanvas.ActualHeight / 2;
        Add(GCircleCanvas, new Ellipse { Width = r * 2, Height = r * 2, Stroke = Brushes.SlateGray, StrokeThickness = 2 }, cx - r, cy - r);
        Add(GCircleCanvas, new Line { X1 = cx - r, X2 = cx + r, Y1 = cy, Y2 = cy, Stroke = Brushes.DimGray }, 0, 0); Add(GCircleCanvas, new Line { X1 = cx, X2 = cx, Y1 = cy - r, Y2 = cy + r, Stroke = Brushes.DimGray }, 0, 0);
        Add(GCircleCanvas, new Ellipse { Width = 14, Height = 14, Fill = Brushes.DeepSkyBlue }, cx + p.X - 7, cy + p.Y - 7);
    }
    private void DrawCar(QuasiStaticResult result)
    {
        CarCanvas.Children.Clear(); var w = CarCanvas.ActualWidth; var h = CarCanvas.ActualHeight;
        Add(CarCanvas, new Rectangle { Width = w * .32, Height = h * .68, RadiusX = 30, RadiusY = 30, Fill = Brushes.SlateGray }, w * .34, h * .15);
        foreach (var (id, x, y) in new[] { ("FL", .12, .17), ("FR", .76, .17), ("RL", .12, .65), ("RR", .76, .65) })
        {
            var corner = result.Corners.Single(c => c.Corner == id); Add(CarCanvas, new Rectangle { Width = 38, Height = 58, Fill = corner.IsLifted ? Brushes.OrangeRed : Brushes.SteelBlue }, w * x, h * y);
            Add(CarCanvas, new TextBlock { Text = $"{id}\n{corner.NormalLoadN:0} N", Foreground = Brushes.White, FontSize = 10 }, w * x, h * y + 60);
        }
    }
    private double Radius() => Math.Max(1, Math.Min(GCircleCanvas.ActualWidth, GCircleCanvas.ActualHeight) / 2 - 20);
    private static double ReadPositive(TextBox box, string name) { var n = Read(box, name); return n > 0 ? n : throw new InvalidOperationException($"{name} must be positive."); }
    private static double ReadNonNegative(TextBox box, string name) { var n = Read(box, name); return n >= 0 ? n : throw new InvalidOperationException($"{name} must be non-negative."); }
    private static double Read(TextBox box, string name) => double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : throw new InvalidOperationException($"{name} must be a finite number using . as decimal separator.");
    private static void Set(TextBox box, double value) => box.Text = value.ToString("G", CultureInfo.InvariantCulture);
    private static void AddAxleTangentMetrics(ICollection<DashboardMetric> metrics, GeometryAxleTangentRollStiffness axle)
    {
        var validity = axle.Validity.ToString().ToLowerInvariant();
        metrics.Add(new($"{axle.Axle}-roll-spring", $"{axle.Axle} spring tangent roll stiffness", "N·m/rad", axle.SpringNmPerRad.ToString("F0", CultureInfo.InvariantCulture), validity));
        metrics.Add(new($"{axle.Axle}-roll-arb", $"{axle.Axle} ARB tangent roll stiffness", "N·m/rad", axle.ArbNmPerRad.ToString("F0", CultureInfo.InvariantCulture), validity));
        metrics.Add(new($"{axle.Axle}-roll-stop", $"{axle.Axle} bump-stop tangent roll stiffness", "N·m/rad", axle.BumpStopNmPerRad.ToString("F0", CultureInfo.InvariantCulture), validity));
        metrics.Add(new($"{axle.Axle}-roll-total", $"{axle.Axle} total tangent roll stiffness", "N·m/rad", axle.TotalNmPerRad.ToString("F0", CultureInfo.InvariantCulture), validity));
    }
    private static string Percent(double value) => double.IsNaN(value) ? "undefined" : (100 * value).ToString("F2", CultureInfo.InvariantCulture);
    private static string Degrees(double radians) => (radians * 180 / Math.PI).ToString("F3", CultureInfo.InvariantCulture);
    private static void Add(Canvas canvas, UIElement item, double x, double y) { Canvas.SetLeft(item, x); Canvas.SetTop(item, y); canvas.Children.Add(item); }
    private static string RepositoryRoot()
    {
        for (var d = new IO.DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent) if (IO.Directory.Exists(IO.Path.Combine(d.FullName, "data", "synthetic", "cayman"))) return d.FullName;
        for (var d = new IO.DirectoryInfo(Environment.CurrentDirectory); d is not null; d = d.Parent) if (IO.Directory.Exists(IO.Path.Combine(d.FullName, "data", "synthetic", "cayman"))) return d.FullName;
        throw new IO.DirectoryNotFoundException("Repository root containing data/synthetic/cayman was not found.");
    }
}
