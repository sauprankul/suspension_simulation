namespace SuspensionSimulation.Components;

/// <summary>
/// Small-deflection torsion-only ARB geometry. The torsion section is a circular tube;
/// arm bending, bearing compliance, link bending, and suspension kinematics are excluded.
/// </summary>
public sealed record TubularAntiRollBarConfig
{
    public required string Id { get; init; }
    public string SchemaVersion { get; init; } = "1.0";
    public DataProvenance Provenance { get; init; } = DataProvenance.Estimated;
    public string? SourceNote { get; init; }
    /// <summary>Active length that twists, excluding arms, in metres.</summary>
    public double TorsionLengthM { get; init; }
    public double OutsideDiameterM { get; init; }
    public double InsideDiameterM { get; init; }
    /// <summary>Material shear modulus in Pa; a steel value must be declared, never implied.</summary>
    public double ShearModulusPa { get; init; }
    public required ArbSideGeometry Left { get; init; }
    public required ArbSideGeometry Right { get; init; }
}

/// <summary>
/// `WheelToEndLinkVerticalMotionRatio` is d(end-link lower-joint vertical position)/d(wheel jounce).
/// It must come from measured/solved suspension geometry; end-link length and angle cannot derive it.
/// Angle is from vertical at ride height: zero is vertical and only converts vertical to axial link force.
/// </summary>
public sealed record ArbSideGeometry(
    double LeverArmM,
    double EndLinkLengthM,
    double EndLinkAngleFromVerticalRad,
    double WheelToEndLinkVerticalMotionRatio);

public sealed record TubularAntiRollBarProperties(
    double PolarSecondMomentM4,
    double TorsionalStiffnessNmPerRad);

public sealed record TubularAntiRollBarEvaluation(
    double BarTwistRad,
    double BarTorqueNm,
    double LeftEndLinkVerticalForceN,
    double RightEndLinkVerticalForceN,
    double LeftEndLinkAxialForceN,
    double RightEndLinkAxialForceN,
    double LeftWheelReactionN,
    double RightWheelReactionN,
    double StoredEnergyJ);

public sealed class TubularAntiRollBar
{
    private readonly TubularAntiRollBarConfig _config;
    private readonly TubularAntiRollBarProperties _properties;
    public TubularAntiRollBar(TubularAntiRollBarConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(config.Id)) throw new ArgumentException("An id is required.", nameof(config));
        Guard.Positive(nameof(config.TorsionLengthM), config.TorsionLengthM); Guard.Positive(nameof(config.OutsideDiameterM), config.OutsideDiameterM);
        Guard.NonNegative(nameof(config.InsideDiameterM), config.InsideDiameterM); Guard.Positive(nameof(config.ShearModulusPa), config.ShearModulusPa);
        if (config.InsideDiameterM >= config.OutsideDiameterM) throw new ArgumentException("Inside diameter must be smaller than outside diameter.", nameof(config));
        ValidateSide(config.Left, nameof(config.Left)); ValidateSide(config.Right, nameof(config.Right));
        var polar = Math.PI / 32.0 * (Math.Pow(config.OutsideDiameterM, 4) - Math.Pow(config.InsideDiameterM, 4));
        _properties = new TubularAntiRollBarProperties(polar, config.ShearModulusPa * polar / config.TorsionLengthM);
    }

    public TubularAntiRollBarProperties Properties => _properties;
    /// <summary>Immutable declared local geometry; callers must not substitute inferred chassis kinematics.</summary>
    public (ArbSideGeometry Left, ArbSideGeometry Right) Geometry => (_config.Left, _config.Right);

    /// <summary>
    /// Evaluates ideal reactions for wheel jounce in metres. Equal wheel jounce causes zero twist only
    /// when both declared wheel-to-link motion ratios and arm lengths are equal.
    /// </summary>
    public TubularAntiRollBarEvaluation Evaluate(double leftWheelJounceM, double rightWheelJounceM)
    {
        if (!double.IsFinite(leftWheelJounceM) || !double.IsFinite(rightWheelJounceM)) throw new ArgumentOutOfRangeException(nameof(leftWheelJounceM));
        var leftArmAngle = _config.Left.WheelToEndLinkVerticalMotionRatio * leftWheelJounceM / _config.Left.LeverArmM;
        var rightArmAngle = _config.Right.WheelToEndLinkVerticalMotionRatio * rightWheelJounceM / _config.Right.LeverArmM;
        var twist = leftArmAngle - rightArmAngle;
        var torque = _properties.TorsionalStiffnessNmPerRad * twist;
        // -d(1/2*k*twist^2)/d(end-link vertical displacement), one side per arm.
        var leftVertical = -torque / _config.Left.LeverArmM;
        var rightVertical = torque / _config.Right.LeverArmM;
        var leftAxial = AxialForce(leftVertical, _config.Left.EndLinkAngleFromVerticalRad);
        var rightAxial = AxialForce(rightVertical, _config.Right.EndLinkAngleFromVerticalRad);
        return new(twist, torque, leftVertical, rightVertical, leftAxial, rightAxial,
            leftVertical * _config.Left.WheelToEndLinkVerticalMotionRatio,
            rightVertical * _config.Right.WheelToEndLinkVerticalMotionRatio,
            0.5 * _properties.TorsionalStiffnessNmPerRad * twist * twist);
    }

    private static void ValidateSide(ArbSideGeometry side, string name)
    {
        ArgumentNullException.ThrowIfNull(side);
        Guard.Positive($"{name}.LeverArmM", side.LeverArmM); Guard.Positive($"{name}.EndLinkLengthM", side.EndLinkLengthM);
        if (!double.IsFinite(side.EndLinkAngleFromVerticalRad) || Math.Abs(Math.Cos(side.EndLinkAngleFromVerticalRad)) < 1e-6)
            throw new ArgumentOutOfRangeException(name, "End-link angle must be finite and not horizontal.");
        if (!double.IsFinite(side.WheelToEndLinkVerticalMotionRatio)) throw new ArgumentOutOfRangeException(name, "Wheel-to-link vertical motion ratio must be finite.");
    }
    private static double AxialForce(double verticalForceN, double angleFromVerticalRad) => verticalForceN / Math.Cos(angleFromVerticalRad);
}
