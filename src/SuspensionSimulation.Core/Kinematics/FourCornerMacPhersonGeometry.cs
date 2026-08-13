using System.Numerics;
using System.Text.Json;

namespace SuspensionSimulation.Kinematics;

/// <summary>Declared ARB end-link motion at the hard-point reference pose.</summary>
public sealed record ArbTravelAttachment(string Axle, string ArbId, string LeftCorner, string RightCorner,
    double LeftReferenceWheelToEndLinkMotionRatio, double RightReferenceWheelToEndLinkMotionRatio, string Provenance);

public sealed record FourCornerMacPhersonExpected(double ReferenceJounceM, double TravelCheckM,
    double MaxReferenceConstraintResidualM, double MaxReferenceTieRodLengthResidualM, string Validity);

/// <summary>
/// The geometry belongs to a vehicle package; its fixture provenance is preserved so synthetic
/// pickups can never be mistaken for public chassis-envelope values.
/// </summary>
public sealed record FourCornerMacPhersonGeometry(
    IReadOnlyDictionary<string, MacPhersonPlanarCornerConfig> Corners,
    IReadOnlyDictionary<string, ArbTravelAttachment> ArbAttachments,
    FourCornerMacPhersonExpected Expected,
    string Provenance);

public static class FourCornerMacPhersonGeometryLoader
{
    public static FourCornerMacPhersonGeometry Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var hardPointPath = Path.Combine(directory, "strut-hard-points.json");
        var expectedPath = Path.Combine(directory, "strut-geometry-expected.json");
        if (!File.Exists(hardPointPath) || !File.Exists(expectedPath))
            throw new FileNotFoundException("Four-corner geometry requires strut-hard-points.json and strut-geometry-expected.json in the vehicle package.");
        using var hardPoints = JsonDocument.Parse(File.ReadAllText(hardPointPath));
        using var expected = JsonDocument.Parse(File.ReadAllText(expectedPath));
        var root = hardPoints.RootElement;
        var ground = root.GetProperty("ground_plane_z_m").GetDouble();
        var corners = root.GetProperty("corners").EnumerateArray().Select(x => ToCorner(x, ground)).ToArray();
        if (corners.Length != 4 || corners.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
            throw new InvalidDataException("Hard-point package must contain exactly FL/FR/RL/RR.");
        var attachments = root.GetProperty("arb_attachments").EnumerateArray().Select(x => new ArbTravelAttachment(
            x.GetProperty("axle").GetString()!, x.GetProperty("arb_id").GetString()!, x.GetProperty("left_corner").GetString()!, x.GetProperty("right_corner").GetString()!,
            x.GetProperty("left_wheel_to_end_link_vertical_motion_ratio").GetDouble(), x.GetProperty("right_wheel_to_end_link_vertical_motion_ratio").GetDouble(), x.GetProperty("source").GetString()!)).ToArray();
        if (attachments.Length != 2 || attachments.Select(x => x.Axle).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2)
            throw new InvalidDataException("Hard-point package must have one front and one rear ARB attachment.");
        var expectedRoot = expected.RootElement;
        var expectedContract = new FourCornerMacPhersonExpected(expectedRoot.GetProperty("reference_jounce_m").GetDouble(), expectedRoot.GetProperty("travel_check_m").GetDouble(),
            expectedRoot.GetProperty("max_reference_constraint_residual_m").GetDouble(), expectedRoot.GetProperty("max_reference_tie_rod_length_residual_m").GetDouble(), expectedRoot.GetProperty("validity").GetString()!);
        return new(corners.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase), attachments.ToDictionary(x => x.Axle, StringComparer.OrdinalIgnoreCase), expectedContract,
            root.GetProperty("provenance").GetString()!);
    }

    private static MacPhersonPlanarCornerConfig ToCorner(JsonElement x, double ground) => new()
    {
        Id = x.GetProperty("id").GetString()!,
        UpperStrutMountM = Vector(x.GetProperty("upper_strut_mount")),
        LowerArmInnerPivotM = Vector(x.GetProperty("lower_arm_inner_pivot")),
        LowerBallJointReferenceM = Vector(x.GetProperty("lower_ball_joint")),
        StrutLowerReferenceM = Vector(x.GetProperty("strut_lower")),
        WheelCentreReferenceM = Vector(x.GetProperty("wheel_center")),
        TieRodInnerM = Vector(x.GetProperty("tie_rod_inner")),
        TieRodOuterReferenceM = Vector(x.GetProperty("tie_rod_outer")),
        GroundPlaneZM = ground
    };
    private static Vector3 Vector(JsonElement values) => new(values[0].GetSingle(), values[1].GetSingle(), values[2].GetSingle());
}
