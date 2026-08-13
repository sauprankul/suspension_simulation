namespace SuspensionSimulation.Results;

/// <summary>Visible result state; callers must not turn non-valid values into ordinary plot data.</summary>
public enum ResultStatus { Valid, Warning, Invalid }
public enum DiagnosticSeverity { Information, Warning, Error }

public sealed record SolverDiagnostic(string Code, DiagnosticSeverity Severity, string Message);

/// <summary>Auditable identity of one deterministic calculation and the exact setup it used.</summary>
public sealed record ResultProvenance(
    string SolverVersion,
    string InputFingerprintSha256,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> OmittedEffects);

public sealed record ScalarResult(string Name, double Value, string Unit, ResultStatus Status = ResultStatus.Valid);

/// <summary>Raw points only: display interpolation and smoothing belong outside the calculation result.</summary>
public sealed record SeriesPoint(double X, double Y, ResultStatus Status = ResultStatus.Valid);
public sealed record ResultSeries(string Name, string XUnit, string YUnit, IReadOnlyList<SeriesPoint> Points);

public sealed record AnalysisResult(
    ResultStatus Status,
    ResultProvenance Provenance,
    IReadOnlyList<ScalarResult> Scalars,
    IReadOnlyList<ResultSeries> Series,
    IReadOnlyList<SolverDiagnostic> Diagnostics)
{
    public static ResultStatus DeriveStatus(IEnumerable<SolverDiagnostic> diagnostics) =>
        diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error) ? ResultStatus.Invalid :
        diagnostics.Any(x => x.Severity == DiagnosticSeverity.Warning) ? ResultStatus.Warning : ResultStatus.Valid;
}
