namespace SuspensionSimulation.Components;

/// <summary>
/// Small-deflection circular-section ARB geometry. Torsion-section twist and cantilever-arm
/// bending act in series. Bearing compliance, link bending, and suspension kinematics are excluded.
/// </summary>
public sealed record TubularAntiRollBarConfig : Configuration.IVersionedConfiguration
{
    /// <summary>Required only for standalone use; an enclosing ARB component supplies it when nested.</summary>
    public string Id { get; init; } = "";
    public string SchemaVersion { get; init; } = "1.0";
    public DataProvenance Provenance { get; init; } = DataProvenance.Estimated;
    public string? SourceNote { get; init; }
    /// <summary>Active length that twists, excluding arms, in metres.</summary>
    public double TorsionLengthM { get; init; }
    public double OutsideDiameterM { get; init; }
    public double InsideDiameterM { get; init; }
    /// <summary>Material shear modulus in Pa; a steel value must be declared, never implied.</summary>
    public double ShearModulusPa { get; init; }
    /// <summary>Material Young's modulus in Pa for circular cantilever-arm bending.</summary>
    public double YoungModulusPa { get; init; } = 210_000_000_000.0;
    /// <summary>Explicit source convention for mapping the derived bar rate into stored roll energy.</summary>
    public double EnergyStiffnessScale { get; init; } = 1.0;
    public required ArbSideGeometry Left { get; init; }
    public required ArbSideGeometry Right { get; init; }
}

/// <summary>
/// `WheelToEndLinkVerticalMotionRatio` is d(end-link lower-joint vertical position)/d(wheel jounce).
/// It must come from measured/solved suspension geometry; end-link length and angle cannot derive it.
/// Angle is from vertical at ride height. The first-order axial-link projection maps lower-joint vertical
/// motion to bar-end vertical motion by cos²(angle), so it affects wheel tangent and becomes singular
/// as the link approaches horizontal.
/// </summary>
public sealed record ArbSideGeometry(
    double LeverArmM,
    double EndLinkLengthM,
    double EndLinkAngleFromVerticalRad,
    double WheelToEndLinkVerticalMotionRatio);

public sealed record TubularAntiRollBarProperties(
    double PolarSecondMomentM4,
    double TorsionalStiffnessNmPerRad,
    double SecondMomentM4 = double.NaN,
    double LeftArmBendingStiffnessNPerM = double.NaN,
    double RightArmBendingStiffnessNPerM = double.NaN,
    double EffectiveSystemTorsionalStiffnessNmPerRad = double.NaN);

public sealed record TubularArbWheelTangent(double LeftSelfNPerM, double RightSelfNPerM, double CrossNPerM,
    double? SymmetricEffectiveWheelPairTangentNPerM, string Validity, IReadOnlyList<string> Warnings,
    double LeftAxialLinkExtensionPerWheelJounce = double.NaN, double RightAxialLinkExtensionPerWheelJounce = double.NaN);

public sealed record TubularAntiRollBarEvaluation(
    double BarTwistRad,
    double BarTorqueNm,
    double LeftEndLinkVerticalForceN,
    double RightEndLinkVerticalForceN,
    double LeftEndLinkAxialForceN,
    double RightEndLinkAxialForceN,
    double LeftWheelReactionN,
    double RightWheelReactionN,
    double StoredEnergyJ,
    double MechanismTwistRad = double.NaN);

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
        Guard.Positive(nameof(config.YoungModulusPa), config.YoungModulusPa); Guard.Positive(nameof(config.EnergyStiffnessScale), config.EnergyStiffnessScale);
        if (config.InsideDiameterM >= config.OutsideDiameterM) throw new ArgumentException("Inside diameter must be smaller than outside diameter.", nameof(config));
        ValidateSide(config.Left, nameof(config.Left)); ValidateSide(config.Right, nameof(config.Right));
        var second = Math.PI / 64.0 * (Math.Pow(config.OutsideDiameterM, 4) - Math.Pow(config.InsideDiameterM, 4));
        var polar = 2*second; var torsional = config.ShearModulusPa * polar / config.TorsionLengthM;
        var leftArm = 3*config.YoungModulusPa*second/Math.Pow(config.Left.LeverArmM,3);
        var rightArm = 3*config.YoungModulusPa*second/Math.Pow(config.Right.LeverArmM,3);
        var effective = config.EnergyStiffnessScale/(1/torsional+1/(leftArm*config.Left.LeverArmM*config.Left.LeverArmM)+1/(rightArm*config.Right.LeverArmM*config.Right.LeverArmM));
        _properties = new TubularAntiRollBarProperties(polar,torsional,second,leftArm,rightArm,effective);
    }

    public TubularAntiRollBarProperties Properties => _properties;
    /// <summary>Immutable declared local geometry; callers must not substitute inferred chassis kinematics.</summary>
    public (ArbSideGeometry Left, ArbSideGeometry Right) Geometry => (_config.Left, _config.Right);

    /// <summary>Wheel-plane energy Hessian. A single pair rate is exact only for symmetric local side geometry.</summary>
    public TubularArbWheelTangent WheelTangent() => WheelTangent(_config.Left.WheelToEndLinkVerticalMotionRatio, _config.Right.WheelToEndLinkVerticalMotionRatio);

    /// <summary>
    /// Wheel-plane energy Hessian at a solved travel point. The supplied derivatives are
    /// d(end-link vertical position)/d(wheel jounce), normally obtained from hard-point travel.
    /// They are not inferred from link length or static angle.
    /// </summary>
    public TubularArbWheelTangent WheelTangent(double leftWheelToEndLinkVerticalMotionRatio, double rightWheelToEndLinkVerticalMotionRatio)
    {
        if (!double.IsFinite(leftWheelToEndLinkVerticalMotionRatio) || !double.IsFinite(rightWheelToEndLinkVerticalMotionRatio))
            throw new ArgumentOutOfRangeException(nameof(leftWheelToEndLinkVerticalMotionRatio), "Travel derivatives must be finite.");
        // A link angle alone does not determine how its upper attachment moves: that requires full
        // 3D hard-point continuation. Refuse an alleged geometry-derived wheel Hessian when the
        // supplied derivative is only lower-joint vertical motion and the link is materially angled.
        if (Math.Abs(Math.Sin(_config.Left.EndLinkAngleFromVerticalRad)) > 1e-3 || Math.Abs(Math.Sin(_config.Right.EndLinkAngleFromVerticalRad)) > 1e-3)
            return new(double.NaN, double.NaN, double.NaN, null, "invalid",
                ["End-link angle is non-vertical. Lower-joint vertical motion plus link angle cannot determine bar-end motion; provide a solved bar-end travel derivative from 3D kinematics."],
                leftWheelToEndLinkVerticalMotionRatio * Math.Cos(_config.Left.EndLinkAngleFromVerticalRad), rightWheelToEndLinkVerticalMotionRatio * Math.Cos(_config.Right.EndLinkAngleFromVerticalRad));
        var aL = EffectiveBarEndMotionRatio(leftWheelToEndLinkVerticalMotionRatio, _config.Left) / _config.Left.LeverArmM;
        var aR = EffectiveBarEndMotionRatio(rightWheelToEndLinkVerticalMotionRatio, _config.Right) / _config.Right.LeverArmM;
        var left = _properties.EffectiveSystemTorsionalStiffnessNmPerRad * aL * aL;
        var right = _properties.EffectiveSystemTorsionalStiffnessNmPerRad * aR * aR;
        var cross = -_properties.EffectiveSystemTorsionalStiffnessNmPerRad * aL * aR;
        var symmetric = Math.Abs(aL - aR) <= 1e-10 * Math.Max(1, Math.Max(Math.Abs(aL), Math.Abs(aR)));
        return new(left, right, cross, symmetric ? left : null, symmetric ? "valid" : "warning",
            symmetric ? [] : ["Asymmetric side geometry: retain the coupled stiffness matrix; a single direct wheel-pair rate is not exact."],
            leftWheelToEndLinkVerticalMotionRatio, rightWheelToEndLinkVerticalMotionRatio);
    }

    /// <summary>
    /// Evaluates ideal reactions for wheel jounce in metres. Equal wheel jounce causes zero twist only
    /// when both declared wheel-to-link motion ratios and arm lengths are equal.
    /// </summary>
    public TubularAntiRollBarEvaluation Evaluate(double leftWheelJounceM, double rightWheelJounceM)
    {
        if (!double.IsFinite(leftWheelJounceM) || !double.IsFinite(rightWheelJounceM)) throw new ArgumentOutOfRangeException(nameof(leftWheelJounceM));
        var leftEffective = EffectiveBarEndMotionRatio(_config.Left.WheelToEndLinkVerticalMotionRatio, _config.Left);
        var rightEffective = EffectiveBarEndMotionRatio(_config.Right.WheelToEndLinkVerticalMotionRatio, _config.Right);
        var leftArmAngle = leftEffective * leftWheelJounceM / _config.Left.LeverArmM;
        var rightArmAngle = rightEffective * rightWheelJounceM / _config.Right.LeverArmM;
        var mechanismTwist = leftArmAngle - rightArmAngle;
        var torque = _properties.EffectiveSystemTorsionalStiffnessNmPerRad * mechanismTwist;
        var barTwist = torque/_properties.TorsionalStiffnessNmPerRad;
        // -d(1/2*k*twist^2)/d(end-link vertical displacement), one side per arm.
        var leftVertical = -torque / _config.Left.LeverArmM;
        var rightVertical = torque / _config.Right.LeverArmM;
        var leftAxial = AxialForce(leftVertical, _config.Left.EndLinkAngleFromVerticalRad);
        var rightAxial = AxialForce(rightVertical, _config.Right.EndLinkAngleFromVerticalRad);
        return new(barTwist, torque, leftVertical, rightVertical, leftAxial, rightAxial,
            leftVertical * leftEffective,
            rightVertical * rightEffective,
            0.5 * _properties.EffectiveSystemTorsionalStiffnessNmPerRad * mechanismTwist * mechanismTwist,
            mechanismTwist);
    }

    private static void ValidateSide(ArbSideGeometry side, string name)
    {
        ArgumentNullException.ThrowIfNull(side);
        Guard.Positive($"{name}.LeverArmM", side.LeverArmM); Guard.Positive($"{name}.EndLinkLengthM", side.EndLinkLengthM);
        if (!double.IsFinite(side.EndLinkAngleFromVerticalRad) || Math.Abs(Math.Cos(side.EndLinkAngleFromVerticalRad)) < 1e-6)
            throw new ArgumentOutOfRangeException(name, "End-link angle must be finite and not horizontal.");
        if (!double.IsFinite(side.WheelToEndLinkVerticalMotionRatio)) throw new ArgumentOutOfRangeException(name, "Wheel-to-link vertical motion ratio must be finite.");
    }
    private static double EffectiveBarEndMotionRatio(double wheelToLowerLinkVerticalMotionRatio, ArbSideGeometry side)
    {
        // This bounded planar component evaluator remains available for force inspection. WheelTangent
        // above refuses to promote an angled-link result to geometry-backed wheel stiffness.
        return wheelToLowerLinkVerticalMotionRatio;
    }
    private static double AxialForce(double verticalForceN, double angleFromVerticalRad) => verticalForceN / Math.Cos(angleFromVerticalRad);
}
