namespace SuspensionSimulation.Components;

/// <summary>Provenance is displayed with results; synthetic/estimated values are never physical measurements.</summary>
public enum DataProvenance { Synthetic, PublicSource, Measured, Estimated }

public sealed record CurvePoint(double X, double Y);

public sealed record ElementEvaluation(
    double ForceN,
    double TangentRateNPerM,
    bool IsEngaged,
    bool IsExtrapolated = false);

public interface ICompressionElement
{
    ElementEvaluation EvaluateCompression(double compressionM);
}

public interface IVelocityElement
{
    /// <summary>Positive velocity is increasing compression. Returned force opposes that velocity.</summary>
    double EvaluateForce(double velocityMPerS);
}

internal static class Guard
{
    public static void NonNegative(string name, double value)
    {
        if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(name, "Must be finite and non-negative.");
    }

    public static void Positive(string name, double value)
    {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name, "Must be finite and positive.");
    }
}
