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
using SuspensionSimulation.Solvers;

namespace SuspensionSimulation.App;

public partial class MainWindow : Window
{
    private double _ax, _ay;
    private string? _setupDirectory;
    private ModularSetup? _source;
    private QuasiStaticResult? _result;

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
        var front = _source.Assembly.Corners.First(c => c.Corner is Corner.FL or Corner.FR);
        var rear = _source.Assembly.Corners.First(c => c.Corner is Corner.RL or Corner.RR);
        FrontSpring.Text = (_source.Springs.Single(s => s.Id == front.SpringId).LinearRateNPerM / 1000).ToString("G", CultureInfo.InvariantCulture);
        RearSpring.Text = (_source.Springs.Single(s => s.Id == rear.SpringId).LinearRateNPerM / 1000).ToString("G", CultureInfo.InvariantCulture);
        FrontArb.Text = (_source.AntiRollBars.Single(a => a.Id == _source.Assembly.FrontAntiRollBarId).DirectInstalledWheelPairRateNPerM / 1000).ToString("G", CultureInfo.InvariantCulture);
        RearArb.Text = (_source.AntiRollBars.Single(a => a.Id == _source.Assembly.RearAntiRollBarId).DirectInstalledWheelPairRateNPerM / 1000).ToString("G", CultureInfo.InvariantCulture);
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
        if (!IsLoaded || _source is null) return;
        var radius = Radius(); var sweepCap = ReadPositive(SweepCap, "G axis sweep cap"); var totalGCap = ReadPositive(TotalGCap, "total G cap");
        var point = GCircleMapper.Map(_ax, _ay, radius, sweepCap, totalGCap); _ax = point.DisplayLongitudinalG; _ay = point.DisplayLateralG;
        LongitudinalReadout.Text = $"Longitudinal: {_ax:+0.00;-0.00;0.00} G";
        LateralReadout.Text = $"Lateral: {_ay:+0.00;-0.00;0.00} G";
        MagnitudeReadout.Text = $"Magnitude: {Math.Sqrt(_ax * _ax + _ay * _ay):0.00} / {totalGCap:0.00} G" + (point.WasClamped ? " (capped)" : "");
        try
        {
            _result = new FourStrutQuasiStaticSolver().Solve(BuildPackage(), new LoadCase("dashboard", _ax, _ay, 0));
            var activeWarning = _result.Warnings.FirstOrDefault(w => w.StartsWith("Active bump-stop branch", StringComparison.Ordinal) || w.Contains("non-positive normal load", StringComparison.Ordinal));
            StatusText.Text = $"Solver residuals: Fz {_result.VerticalResidualN:E2} N; roll {_result.RollResidualNm:E2} Nm; pitch {_result.PitchResidualNm:E2} Nm. Input fingerprint: {_source.InputFingerprintSha256[..12]}." + (activeWarning is null ? "" : $" Warning: {activeWarning}");
            DrawG(point, radius); DrawCar(_result); RenderMetrics();
        }
        catch (Exception exception)
        {
            _result = null; StatusText.Text = $"Setup input error: {exception.Message}"; DrawG(point, radius); CarCanvas.Children.Clear(); MetricsGrid.ItemsSource = null;
        }
    }

    private VehiclePackage BuildPackage()
    {
        if (_source is null) throw new InvalidOperationException("Setup was not loaded.");
        var frontSpring = ReadPositive(FrontSpring, "front spring") * 1000;
        var rearSpring = ReadPositive(RearSpring, "rear spring") * 1000;
        var frontArb = ReadNonNegative(FrontArb, "front ARB") * 1000;
        var rearArb = ReadNonNegative(RearArb, "rear ARB") * 1000;
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
            [new AntiRollBarModel("front", frontArb, "User-adjusted", "Direct installed differential wheel-pair rate."), new AntiRollBarModel("rear", rearArb, "User-adjusted", "Direct installed differential wheel-pair rate.")],
            new DampingModel("Loaded, excluded", "Static solver does not use damper force."),
            new ModelScope("load-lab/0.2", ["flat road", "rigid body", "small angles", "quasi-static acceleration", "fixed motion ratio", "first bump-stop tangent after engagement"], ["tire force/grip", "kinematic roll centers and jacking", "transient damping", "chassis/bushing compliance", "full bump-stop curve", "calibrated static bump-stop state"]));
    }

    private void RenderMetrics()
    {
        if (_result is null) return;
        var selected = new HashSet<string>(StringComparer.Ordinal);
        if (Lltd.IsChecked == true) selected.Add("lltd"); if (Rsd.IsChecked == true) selected.Add("rsd"); if (Rcb.IsChecked == true) selected.Add("rcb"); if (Loads.IsChecked == true) selected.Add("loads"); if (AxleSummary.IsChecked == true) selected.Add("summary"); if (Attitude.IsChecked == true) selected.Add("attitude");
        var metrics = new List<DashboardMetric>();
        if (selected.Contains("lltd")) metrics.Add(new("lltd", "Front LLTD", "%", Percent(_result.LoadTransferDistributionFront), true, "estimated"));
        if (selected.Contains("rsd")) metrics.Add(new("rsd", "Front RSD", "%", Percent(_result.RollStiffnessDistributionFront), true, "estimated"));
        if (selected.Contains("rcb")) metrics.Add(new("rcb", "RCB: RSD − static front fraction", "percentage points", _result.RollCoupleBiasPercentagePoints.ToString("F2", CultureInfo.InvariantCulture), "estimated"));
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
