using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using IO = System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SuspensionSimulation.Configuration;
using SuspensionSimulation.Core;
using SuspensionSimulation.Core.Dashboard;
using SuspensionSimulation.Solvers;
using SuspensionSimulation.Solvers.HeadToHead;
using SuspensionSimulation.Reference;

namespace SuspensionSimulation.App;

public sealed record ComparisonRow(string Metric, string A, string B, string Delta, string Unit);

public partial class MainWindow : Window
{
    private double _ax;
    private double _ay;
    private ModularSetup? _vehicle;
    private HeadToHeadComparisonResult? _comparison;
    private string? _packageDirectory;
    private long _renderAttempt;
    private HeadToHeadComparisonSolver? _headToHeadSolver;
    private StockReferenceDocument? _stockReference;
    private StockSetupDerivation? _stockGr86;
    private StockSetupDerivation? _stockBrz;
    private readonly DispatcherTimer _renderTimer;

    public MainWindow()
    {
        App.Log("Constructing stock GR86 vs BRZ head-to-head MainWindow.");
        InitializeComponent();
        _renderTimer=new DispatcherTimer(DispatcherPriority.Render) { Interval=TimeSpan.FromMilliseconds(50) };
        _renderTimer.Tick+=(_,_)=>{ _renderTimer.Stop(); Render(); };
        Loaded += (_, _) => Initialise();
        SizeChanged += (_, _) => ScheduleRender();
    }

    private void Initialise()
    {
        _packageDirectory = IO.Path.Combine(RepositoryRoot(), "data", "synthetic", "gr86");
        _vehicle = ModularSetupLoader.Load(_packageDirectory);
        _stockReference=Stock86BrzReferenceLoader.Load(IO.Path.Combine(RepositoryRoot(),"data","reference","stock-86-brz","reference-sheet.json"));
        _stockGr86=Stock86BrzReferenceCalculator.Calculate(_stockReference,_stockReference.Setups.Single(x=>x.Id=="stock-gr86-2022"));
        _stockBrz=Stock86BrzReferenceCalculator.Calculate(_stockReference,_stockReference.Setups.Single(x=>x.Id=="stock-brz-2022"));
        App.Log(JsonSerializer.Serialize(new { eventName="stock-reference-derivation", gr86=_stockGr86, brz=_stockBrz }),"REFERENCE");
        var precompute=Stopwatch.StartNew();
        _headToHeadSolver=new HeadToHeadComparisonSolver(_packageDirectory);
        precompute.Stop();
        App.Log(JsonSerializer.Serialize(new { eventName="geometry-precompute", elapsedMs=precompute.Elapsed.TotalMilliseconds,
            maps=_headToHeadSolver.TravelMaps.ToDictionary(x=>x.Key,x=>new { minimumJounceM=x.Value.MinimumJounceM,maximumJounceM=x.Value.MaximumJounceM,stepM=x.Value.StepM,samples=x.Value.Samples.Count,maximumResidualM=x.Value.MaximumConstraintResidualM }) }),"GEOMETRY");
        var a = LoadSetup(IO.Path.Combine(_packageDirectory, "setup-a.json"));
        var b = LoadSetup(IO.Path.Combine(_packageDirectory, "setup-b.json"));
        Set(FrontUnsprungMass,_vehicle.Chassis.FrontUnsprungMassPerCornerKg);
        Set(RearUnsprungMass,_vehicle.Chassis.RearUnsprungMassPerCornerKg);
        App.Log($"Loaded GR86 package directory={_packageDirectory} fingerprint={_vehicle.InputFingerprintSha256}.");
        Populate(a, true); Populate(b, false);
        Render();
    }

    private void CenterGClick(object sender, RoutedEventArgs e) { _ax = 0; _ay = 0; _renderTimer.Stop(); Render(); }
    private void SetupChanged(object sender, RoutedEventArgs e) { if (IsLoaded) Render(); }
    private void SharedInputChanged(object sender, RoutedEventArgs e) { if (IsLoaded) Render(); }

    private void GCirclePointer(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed && e.RoutedEvent != Mouse.MouseDownEvent) return;
        var p = e.GetPosition(GCircleCanvas); var radius = Radius(); var cap = ReadPositive(SweepCap, "G axis cap");
        // Screen axes are intentionally inverted: positive longitudinal is up and positive
        // lateral (leftward vehicle acceleration) is left on the displayed overhead view.
        _ay = -(p.X - GCircleCanvas.ActualWidth / 2) / radius * cap;
        _ax = -(p.Y - GCircleCanvas.ActualHeight / 2) / radius * cap;
        ScheduleRender();
    }

    private void ScheduleRender()
    {
        if(!IsLoaded) return;
        // Coalesce pointer events into a steady 20 Hz display refresh. Do not restart the
        // timer for every MouseMove or a continuous drag would never reach the tick.
        if (!_renderTimer.IsEnabled) _renderTimer.Start();
    }

    private void Render()
    {
        if (!IsLoaded || _vehicle is null) return;
        var attempt = Interlocked.Increment(ref _renderAttempt);
        var inputSnapshot = CaptureUiState(attempt);
        App.Log(inputSnapshot, "INPUT");
        var radius = Radius();
        try
        {
            var sweep = ReadPositive(SweepCap, "G axis cap"); var total = ReadPositive(TotalGCap, "combined G cap");
            var point = GCircleMapper.Map(_ax, _ay, radius, sweep, total);
            _ax = point.DisplayLongitudinalG; _ay = point.DisplayLateralG;
            LongitudinalReadout.Text = $"{_ax:+0.00;-0.00;0.00} G";
            LateralReadout.Text = $"{_ay:+0.00;-0.00;0.00} G";
            MagnitudeReadout.Text = $"GSUM {Math.Sqrt(_ax * _ax + _ay * _ay):0.00} / {total:0.00} G" + (point.WasClamped ? "  •  capped" : "");

            var inputA = ReadSetup("Stock GR86", true); var inputB = ReadSetup("Stock BRZ", false);
            var vehicle=_vehicle with { Chassis=_vehicle.Chassis with
            {
                FrontUnsprungMassPerCornerKg=ReadNonNegative(FrontUnsprungMass,"front unsprung mass"),
                RearUnsprungMassPerCornerKg=ReadNonNegative(RearUnsprungMass,"rear unsprung mass")
            }};
            _comparison = (_headToHeadSolver ?? throw new InvalidOperationException("Geometry maps are not initialized."))
                .Solve(vehicle, inputA, inputB, new LoadCase("shared-dashboard", _ax, _ay, 0));
            DrawG(point, radius);
            DrawCar(ACarCanvas, _comparison.A.State, Brushes.DeepSkyBlue);
            DrawCar(BCarCanvas, _comparison.B.State, Brushes.Orange);
            RenderResults(_comparison);
            App.Log(CaptureResult(attempt, _comparison), "RESULT");
        }
        catch (Exception exception)
        {
            App.LogException($"Head-to-head render failed attempt={attempt}; inputSnapshot={inputSnapshot}", exception);
            StatusText.Text = $"Input/solve error; previous valid plots retained: {exception.Message}  •  Log: {App.LogPath}";
            try
            {
                var point = GCircleMapper.Map(_ax, _ay, radius, ReadPositive(SweepCap, "G axis cap"), ReadPositive(TotalGCap, "combined G cap"));
                DrawG(point, radius);
            }
            catch (Exception drawException) { App.Log("G-circle recovery draw failed: " + drawException); }
        }
    }

    private void RenderResults(HeadToHeadComparisonResult comparison)
    {
        var a = comparison.A; var b = comparison.B;
        ARsd.Text = Percent(a.State.RollStiffnessDistributionFront); BRsd.Text = Percent(b.State.RollStiffnessDistributionFront);
        ARcb.Text = Signed(a.State.RollCoupleBiasPercentagePoints, " pp"); BRcb.Text = Signed(b.State.RollCoupleBiasPercentagePoints, " pp");
        DeltaHeadline.Text = $"B − A RCB  {Signed(comparison.BMinusA.RcbPercentagePoints, " pp")}";
        DeltaHeadline.Foreground = comparison.BMinusA.RcbPercentagePoints switch { > 0 => Brushes.Orange, < 0 => Brushes.DeepSkyBlue, _ => Brushes.White };
        AStateSummary.Text = Summary(a); BStateSummary.Text = Summary(b);
        var toPerDegree=Math.PI/180;
        var rows = new List<ComparisonRow>
        {
            Row("Front RSD", 100*a.State.RollStiffnessDistributionFront, 100*b.State.RollStiffnessDistributionFront, "%"),
            Row("RCB", a.State.RollCoupleBiasPercentagePoints, b.State.RollCoupleBiasPercentagePoints, "percentage points"),
            Row("Roll angle", Degrees(a.State.RollRad), Degrees(b.State.RollRad), "deg"),
            Row("Pitch angle", Degrees(a.State.PitchRad), Degrees(b.State.PitchRad), "deg"),
            Row("3D front spring roll stiffness", a.FrontSpringRollStiffnessNmPerRad*toPerDegree, b.FrontSpringRollStiffnessNmPerRad*toPerDegree, "N·m/deg"),
            Row("3D front ARB roll stiffness", a.FrontArbRollStiffnessNmPerRad*toPerDegree, b.FrontArbRollStiffnessNmPerRad*toPerDegree, "N·m/deg"),
            Row("3D rear spring roll stiffness", a.RearSpringRollStiffnessNmPerRad*toPerDegree, b.RearSpringRollStiffnessNmPerRad*toPerDegree, "N·m/deg"),
            Row("3D rear ARB roll stiffness", a.RearArbRollStiffnessNmPerRad*toPerDegree, b.RearArbRollStiffnessNmPerRad*toPerDegree, "N·m/deg"),
            Row("Front parallel-element roll stiffness",a.State.TangentRollStiffnesses!.Single(x=>x.Axle=="front").ParallelElementNmPerRad*toPerDegree,b.State.TangentRollStiffnesses!.Single(x=>x.Axle=="front").ParallelElementNmPerRad*toPerDegree,"N·m/deg"),
            Row("Rear parallel-element roll stiffness",a.State.TangentRollStiffnesses!.Single(x=>x.Axle=="rear").ParallelElementNmPerRad*toPerDegree,b.State.TangentRollStiffnesses!.Single(x=>x.Axle=="rear").ParallelElementNmPerRad*toPerDegree,"N·m/deg")
        };
        if(_stockGr86 is not null && _stockBrz is not null)
        {
            rows.Add(Row("Reference front spring wheel rate",_stockGr86.FrontSpringWheelRateKgfPerMm,_stockBrz.FrontSpringWheelRateKgfPerMm,"kgf/mm"));
            rows.Add(Row("Reference rear spring wheel rate",_stockGr86.RearSpringWheelRateKgfPerMm,_stockBrz.RearSpringWheelRateKgfPerMm,"kgf/mm"));
            rows.Add(Row("Reference front ride frequency",_stockGr86.FrontRideFrequencyHz,_stockBrz.FrontRideFrequencyHz,"Hz"));
            rows.Add(Row("Reference rear ride frequency",_stockGr86.RearRideFrequencyHz,_stockBrz.RearRideFrequencyHz,"Hz"));
            rows.Add(Row("Derived front ARB rate before MR",_stockGr86.FrontArb.RawRateKgfPerMm,_stockBrz.FrontArb.RawRateKgfPerMm,"kgf/mm"));
            rows.Add(Row("Derived rear ARB rate before MR",_stockGr86.RearArb.RawRateKgfPerMm,_stockBrz.RearArb.RawRateKgfPerMm,"kgf/mm"));
            rows.Add(Row("Reference front ARB wheel rate",_stockGr86.FrontArb.WheelRateKgfPerMm,_stockBrz.FrontArb.WheelRateKgfPerMm,"kgf/mm"));
            rows.Add(Row("Reference rear ARB wheel rate",_stockGr86.RearArb.WheelRateKgfPerMm,_stockBrz.RearArb.WheelRateKgfPerMm,"kgf/mm"));
            rows.Add(Row("Reference front roll stiffness",_stockGr86.FrontRollStiffnessNmPerDeg,_stockBrz.FrontRollStiffnessNmPerDeg,"N·m/deg"));
            rows.Add(Row("Reference rear roll stiffness",_stockGr86.RearRollStiffnessNmPerDeg,_stockBrz.RearRollStiffnessNmPerDeg,"N·m/deg"));
            rows.Add(Row("Reference front RSD",100*_stockGr86.FrontRollStiffnessDistribution,100*_stockBrz.FrontRollStiffnessDistribution,"%"));
            rows.Add(Row("Reference RCB",_stockGr86.RollCoupleBiasPercentagePoints,_stockBrz.RollCoupleBiasPercentagePoints,"percentage points"));
        }
        foreach (var corner in new[] { "FL", "FR", "RL", "RR" })
        {
            var ac=a.State.Corners.Single(x => x.Corner == corner); var bc=b.State.Corners.Single(x => x.Corner == corner);
            var av = ac.NormalLoadN; var bv = bc.NormalLoadN;
            rows.Add(Row($"{corner} point-contact normal load", av, bv, "N"));
            rows.Add(Row($"{corner} wheel jounce",1000*ac.RelativeCompressionM,1000*bc.RelativeCompressionM,"mm"));
            rows.Add(Row($"{corner} parallel-element force",ac.ParallelElementForceN,bc.ParallelElementForceN,"N"));
            if(_headToHeadSolver?.PhysicalTravelLimits.TryGetValue(corner,out var limit)==true)
                rows.Add(new($"{corner} geometry-derived travel window",$"{1000*limit.MinimumJounceM:0.0} to {1000*limit.MaximumJounceM:0.0}",$"{1000*limit.MinimumJounceM:0.0} to {1000*limit.MaximumJounceM:0.0}","0.000","mm"));
        }
        ComparisonGrid.ItemsSource = rows;
        StatusText.Text = "REFERENCE-CALIBRATED: stock GR86 and BRZ inputs reproduce the supplied workbook; shared synthetic hard points are calibrated to its 0.95/0.78 spring and 0.92/0.55 ARB static motion ratios. " +
            "This screen does not yet pass the phase publication gate: nonlinear energy equilibrium, calibrated spring preload-curvature, and force-path roll-center/jacking work remain pending. " +
            $"Inputs GR86 {a.InputHash[..12]} / BRZ {b.InputHash[..12]}. Geometry-derived high-finite travel limits and configured parallel elastic curves are active; calibrated OEM bump/rebound curves and tire physics remain excluded. Log: {App.LogPath}";
    }

    private string CaptureUiState(long attempt) => JsonSerializer.Serialize(new
    {
        eventName = "render-attempt",
        attempt,
        axG = _ax,
        ayG = _ay,
        packageDirectory = _packageDirectory,
        packageFingerprint = _vehicle?.InputFingerprintSha256,
        controls = new
        {
            axisCapG = SweepCap.Text,
            combinedCapG = TotalGCap.Text,
            frontUnsprungMassKgPerCorner=FrontUnsprungMass.Text,
            rearUnsprungMassKgPerCorner=RearUnsprungMass.Text,
            setupA = new { frontSpringKgfPerMm = AFrontSpring.Text, rearSpringKgfPerMm = ARearSpring.Text, frontBarOdMm = AFrontOd.Text, frontBarIdMm = AFrontId.Text, rearBarOdMm = ARearOd.Text, rearBarIdMm = ARearId.Text },
            setupB = new { frontSpringKgfPerMm = BFrontSpring.Text, rearSpringKgfPerMm = BRearSpring.Text, frontBarOdMm = BFrontOd.Text, frontBarIdMm = BFrontId.Text, rearBarOdMm = BRearOd.Text, rearBarIdMm = BRearId.Text }
        }
    });

    private static string CaptureResult(long attempt, HeadToHeadComparisonResult comparison) => JsonSerializer.Serialize(new
    {
        eventName = "render-result",
        attempt,
        loadCase = comparison.SharedLoadCase,
        validity = comparison.Validity,
        setupA = ResultSide(comparison.A),
        setupB = ResultSide(comparison.B),
        delta = comparison.BMinusA
    });

    private static object ResultSide(HeadToHeadSideResult side) => new
    {
        input = side.Input,
        side.InputHash,
        side.Validity,
        side.Diagnostics,
        rsdFront = side.State.RollStiffnessDistributionFront,
        rcbPercentagePoints = side.State.RollCoupleBiasPercentagePoints,
        side.State.HeaveCompressionM,
        side.State.RollRad,
        side.State.PitchRad,
        side.State.Corners,
        side.State.Axles,
        side.State.TangentRollStiffnesses,
        side.State.VerticalResidualN,
        side.State.RollResidualNm,
        side.State.PitchResidualNm,
        side.State.SolverIterations,
        side.State.Converged
    };

    private static string Summary(HeadToHeadSideResult side) =>
        $"Roll {Degrees(side.State.RollRad):0.000}°  •  Pitch {Degrees(side.State.PitchRad):0.000}°  •  " +
        $"residuals Fz {side.State.VerticalResidualN:E1} N / roll {side.State.RollResidualNm:E1} N·m / pitch {side.State.PitchResidualNm:E1} N·m";

    private static ComparisonRow Row(string metric, double a, double b, string unit) =>
        new(metric, Format(a), Format(b), Signed(b - a), unit);

    private HeadToHeadSetupInput ReadSetup(string name, bool first) => new(name,
        ReadPositive(first ? AFrontSpring : BFrontSpring, $"{name} front spring") * 9800,
        ReadPositive(first ? ARearSpring : BRearSpring, $"{name} rear spring") * 9800,
        ReadPositive(first ? AFrontOd : BFrontOd, $"{name} front bar OD") / 1000,
        ReadNonNegative(first ? AFrontId : BFrontId, $"{name} front bar ID") / 1000,
        ReadPositive(first ? ARearOd : BRearOd, $"{name} rear bar OD") / 1000,
        ReadNonNegative(first ? ARearId : BRearId, $"{name} rear bar ID") / 1000);

    private void Populate(SetupFile setup, bool first)
    {
        Set(first ? AFrontSpring : BFrontSpring, setup.FrontSpringNPerM / 9800);
        Set(first ? ARearSpring : BRearSpring, setup.RearSpringNPerM / 9800);
        Set(first ? AFrontOd : BFrontOd, setup.FrontArbOdM * 1000);
        Set(first ? AFrontId : BFrontId, setup.FrontArbIdM * 1000);
        Set(first ? ARearOd : BRearOd, setup.RearArbOdM * 1000);
        Set(first ? ARearId : BRearId, setup.RearArbIdM * 1000);
    }

    private static SetupFile LoadSetup(string path) => JsonSerializer.Deserialize<SetupFile>(IO.File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new IO.InvalidDataException($"Could not load {path}.");

    private void DrawG(GCirclePoint point, double radius)
    {
        GCircleCanvas.Children.Clear(); var cx = GCircleCanvas.ActualWidth / 2; var cy = GCircleCanvas.ActualHeight / 2;
        Add(GCircleCanvas, new Ellipse { Width = 2 * radius, Height = 2 * radius, Stroke = Brushes.SlateGray, StrokeThickness = 2 }, cx - radius, cy - radius);
        Add(GCircleCanvas, new Line { X1 = cx-radius, X2 = cx+radius, Y1 = cy, Y2 = cy, Stroke = Brushes.DimGray }, 0, 0);
        Add(GCircleCanvas, new Line { X1 = cx, X2 = cx, Y1 = cy-radius, Y2 = cy+radius, Stroke = Brushes.DimGray }, 0, 0);
        Add(GCircleCanvas, new TextBlock { Text = "+LAT", Foreground = Brushes.Gray, FontSize = 10 }, cx-radius+5, cy+4);
        Add(GCircleCanvas, new TextBlock { Text = "+LONG", Foreground = Brushes.Gray, FontSize = 10 }, cx+5, cy-radius+5);
        Add(GCircleCanvas, new Ellipse { Width = 15, Height = 15, Fill = Brushes.White, Stroke = Brushes.DeepSkyBlue, StrokeThickness = 3 }, cx-point.X-7.5, cy-point.Y-7.5);
    }

    private static void DrawCar(Canvas canvas, QuasiStaticResult state, Brush accent)
    {
        canvas.Children.Clear(); var w = Math.Max(200, canvas.ActualWidth); var h = Math.Max(200, canvas.ActualHeight);
        Add(canvas, new Rectangle { Width = w*.32, Height = h*.62, RadiusX = 25, RadiusY = 25, Fill = Brushes.DarkSlateGray, Stroke = accent, StrokeThickness = 2 }, w*.34, h*.12);
        foreach (var (id, x, y) in new[] { ("FL", .13, .15), ("FR", .76, .15), ("RL", .13, .63), ("RR", .76, .63) })
        {
            var corner = state.Corners.Single(c => c.Corner == id);
            Add(canvas, new Rectangle { Width = 35, Height = 48, Fill = corner.IsLifted ? Brushes.OrangeRed : accent, Opacity = .86 }, w*x, h*y);
            Add(canvas, new TextBlock { Text = $"{id}  {corner.NormalLoadN:0} N", Foreground = Brushes.White, FontSize = 10 }, w*x-7, h*y+51);
        }
        Add(canvas, new TextBlock { Text = $"{state.Corners.Sum(x => x.NormalLoadN):0} N", Foreground = Brushes.White, FontWeight = FontWeights.Bold }, w*.43, h*.42);
    }

    private double Radius() => Math.Max(1, Math.Min(GCircleCanvas.ActualWidth, GCircleCanvas.ActualHeight) / 2 - 22);
    private static double ReadPositive(TextBox box, string name) { var value = Read(box, name); return value > 0 ? value : throw new InvalidOperationException($"{name} must be positive."); }
    private static double ReadNonNegative(TextBox box, string name) { var value = Read(box, name); return value >= 0 ? value : throw new InvalidOperationException($"{name} must be non-negative."); }
    private static double Read(TextBox box, string name) => double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : throw new InvalidOperationException($"{name} must be a finite number using . as the decimal separator.");
    private static void Set(TextBox box, double value) => box.Text = value.ToString("G", CultureInfo.InvariantCulture);
    private static string Percent(double value) => (100 * value).ToString("F2", CultureInfo.InvariantCulture) + "%";
    private static string Format(double value) => Math.Abs(value) >= 1000 ? value.ToString("N0", CultureInfo.InvariantCulture) : value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Signed(double value, string suffix = "") => value.ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture) + suffix;
    private static double Degrees(double radians) => radians * 180 / Math.PI;
    private static void Add(Canvas canvas, UIElement element, double x, double y) { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); canvas.Children.Add(element); }

    private static string RepositoryRoot()
    {
        for (var d = new IO.DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (IO.Directory.Exists(IO.Path.Combine(d.FullName, "data", "synthetic", "gr86"))) return d.FullName;
        for (var d = new IO.DirectoryInfo(Environment.CurrentDirectory); d is not null; d = d.Parent)
            if (IO.Directory.Exists(IO.Path.Combine(d.FullName, "data", "synthetic", "gr86"))) return d.FullName;
        throw new IO.DirectoryNotFoundException("Repository root containing data/synthetic/gr86 was not found.");
    }

    private sealed record SetupFile(string Name, double FrontSpringNPerM, double RearSpringNPerM,
        double FrontArbOdM, double FrontArbIdM, double RearArbOdM, double RearArbIdM);
}
