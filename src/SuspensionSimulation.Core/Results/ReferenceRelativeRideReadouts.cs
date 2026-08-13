namespace SuspensionSimulation.Results;

/// <summary>
/// Rigid-body small-angle state relative to a declared static reference. This is not a physical
/// ride height: tyre radius/deflection, road height, wheel centres, and hard-point kinematics are absent.
/// Positive reference-relative compression means increased wheel-to-body compression.
/// </summary>
public sealed record CornerReferenceRelativeReadout(string Corner, double ReferenceRelativeCompressionM, double ReferenceRelativeWheelVerticalPositionM);

public sealed record ReferenceRelativeAttitudeReadout(
    double HeaveCompressionM, double RollRad, double PitchRad,
    IReadOnlyList<CornerReferenceRelativeReadout> Corners,
    string Semantics = "Reference-relative rigid-body elastic state; not kinematic ride height.");

public static class ReferenceRelativeRideReadouts
{
    /// <summary>Uses the same h - y*roll + x*pitch compression sign convention as the four-corner solver.</summary>
    public static ReferenceRelativeAttitudeReadout Create(double heaveCompressionM, double rollRad, double pitchRad,
        double frontAxleToCgM, double rearAxleToCgM, double frontTrackM, double rearTrackM)
    {
        var values = new[] { heaveCompressionM, rollRad, pitchRad, frontAxleToCgM, rearAxleToCgM, frontTrackM, rearTrackM };
        if (values.Any(x => !double.IsFinite(x)) || frontAxleToCgM < 0 || rearAxleToCgM < 0 || frontTrackM <= 0 || rearTrackM <= 0)
            throw new ArgumentOutOfRangeException(nameof(frontAxleToCgM), "Geometry must be finite; axle distances non-negative and tracks positive.");
        return new(heaveCompressionM, rollRad, pitchRad,
        [Corner("FL", frontAxleToCgM, frontTrackM / 2), Corner("FR", frontAxleToCgM, -frontTrackM / 2),
         Corner("RL", -rearAxleToCgM, rearTrackM / 2), Corner("RR", -rearAxleToCgM, -rearTrackM / 2)]);
        CornerReferenceRelativeReadout Corner(string id, double x, double y)
        {
            var compression = heaveCompressionM - y * rollRad + x * pitchRad;
            return new(id, compression, -compression);
        }
    }
}
