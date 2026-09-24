using System.Numerics;
using SuspensionSimulation.Kinematics;

namespace SuspensionSimulation.Solvers.Kinematics;

/// <summary>
/// Solves bar arms and fixed-length endlinks in 3D. Torsion-section twist and circular-arm bending
/// act in series, matching the supplied stock reference-sheet derivation. Wheel reactions and the
/// complete coupled tangent matrix are numerical derivatives of the resulting stored energy.
/// </summary>
public sealed class TubularArbMechanism3DSolver
{
    // Bar endpoints are Vector3 single-precision coordinates. A 0.2 mm energy
    // stencil is small relative to suspension travel but remains above its noise floor.
    private const double DerivativeStepM = 2e-4;

    public TubularArbMechanismState3D Evaluate(TubularArbMechanism3DConfig config,
        Func<double, Vector3> leftLowerJointAtJounce,
        Func<double, Vector3> rightLowerJointAtJounce,
        double leftJounceM,
        double rightJounceM)
    {
        Validate(config); ArgumentNullException.ThrowIfNull(leftLowerJointAtJounce); ArgumentNullException.ThrowIfNull(rightLowerJointAtJounce);
        var axis = Vector3.Normalize(config.AxisEnd-config.AxisStart);
        var leftLength = Vector3.Distance(config.LeftArmEndReference, config.LeftLowerJointReference);
        var rightLength = Vector3.Distance(config.RightArmEndReference, config.RightLowerJointReference);
        var leftArmLength = Vector3.Cross(axis, config.LeftArmEndReference-(config.AxisEnd)).Length();
        var rightArmLength = Vector3.Cross(axis, config.RightArmEndReference-(config.AxisStart)).Length();
        if (leftArmLength <= 1e-6 || rightArmLength <= 1e-6) throw new ArgumentException("Each ARB arm requires a non-zero lever length perpendicular to the bar axis.");
        var secondMoment = SecondMoment(config);
        var leftArmBending = 3*config.YoungModulusPa*secondMoment/Math.Pow(leftArmLength,3);
        var rightArmBending = 3*config.YoungModulusPa*secondMoment/Math.Pow(rightArmLength,3);
        var torsional = TorsionalStiffness(config);
        var effectiveTorsional = config.EnergyStiffnessScale/(1/torsional+1/(leftArmBending*leftArmLength*leftArmLength)+1/(rightArmBending*rightArmLength*rightArmLength));
        ArbSideState3D Side(bool left, double jounce)
        {
            var root = left ? config.AxisEnd : config.AxisStart;
            var referenceEnd = left ? config.LeftArmEndReference : config.RightArmEndReference;
            var lower = left ? leftLowerJointAtJounce(jounce) : rightLowerJointAtJounce(jounce);
            return SolveSide(root, referenceEnd, lower, left ? leftLength : rightLength, axis);
        }
        (double Energy, ArbSideState3D Left, ArbSideState3D Right) Energy(double l, double r)
        {
            var ls = Side(true, l); var rs = Side(false, r); var twist = ls.ArmAngleRad-rs.ArmAngleRad;
            return (.5*effectiveTorsional*twist*twist, ls, rs);
        }
        var center = Energy(leftJounceM, rightJounceM); var h = DerivativeStepM;
        var lp = Energy(leftJounceM+h, rightJounceM).Energy; var lm = Energy(leftJounceM-h, rightJounceM).Energy;
        var rp = Energy(leftJounceM, rightJounceM+h).Energy; var rm = Energy(leftJounceM, rightJounceM-h).Energy;
        var pp = Energy(leftJounceM+h, rightJounceM+h).Energy; var pm = Energy(leftJounceM+h, rightJounceM-h).Energy;
        var mp = Energy(leftJounceM-h, rightJounceM+h).Energy; var mm = Energy(leftJounceM-h, rightJounceM-h).Energy;
        var kll = (lp-2*center.Energy+lm)/(h*h); var krr = (rp-2*center.Energy+rm)/(h*h);
        var klr = (pp-pm-mp+mm)/(4*h*h);
        var matrix = new[,] { { kll, klr }, { klr, krr } };
        var mechanismTwist = center.Left.ArmAngleRad-center.Right.ArmAngleRad;
        var torque = effectiveTorsional*mechanismTwist; var barTwist = torque/torsional;
        var invalid = center.Left.ConstraintResidualM > 1e-7 || center.Right.ConstraintResidualM > 1e-7 || new[] { kll,krr,klr }.Any(x => !double.IsFinite(x));
        var nearToggle = center.Left.LimitClassification != "regular" || center.Right.LimitClassification != "regular";
        var diagnostics = new List<string> { "Full 3D fixed-length-link continuation; circular arm bending and torsion-section twist act in series, and the wheel tangent is the stored-energy Hessian." };
        if (Math.Abs(config.EnergyStiffnessScale-1) > 1e-12) diagnostics.Add($"Stored-energy stiffness scale {config.EnergyStiffnessScale:G6} is explicitly declared by the source convention.");
        if (nearToggle) diagnostics.Add("A side is near a geometric toggle. The reported finite tangent is step-sensitive; continuation failure is a geometric limit, not an arbitrary infinity clamp.");
        return new(config.Id, center.Left, center.Right, Polar(config), torsional, barTwist, torque, center.Energy,
            -(lp-lm)/(2*h), -(rp-rm)/(2*h), matrix, invalid ? "invalid" : nearToggle ? "near-toggle" : "valid", diagnostics,
            secondMoment,leftArmBending,rightArmBending,effectiveTorsional,mechanismTwist);
    }

    private static ArbSideState3D SolveSide(Vector3 root, Vector3 referenceEnd, Vector3 lower, double linkLength, Vector3 axis)
    {
        var arm = referenceEnd-root; var theta = 0.0;
        for (var i = 0; i < 40; i++)
        {
            var end = root+Rotate(arm, axis, theta); var link = end-lower;
            var f = Vector3.Dot(link,link)-(float)(linkLength*linkLength);
            var tangent = Vector3.Cross(axis, end-root); var df = 2*Vector3.Dot(link,tangent);
            if (Math.Abs(f) < 1e-12) break;
            if (Math.Abs(df) < 1e-10) break;
            var step = Math.Clamp(f/df, -.25, .25); theta -= step;
        }
        var solvedEnd = root+Rotate(arm,axis,theta); var solvedLink = solvedEnd-lower; var length = solvedLink.Length();
        var derivative = Vector3.Dot(Vector3.Normalize(solvedLink), Vector3.Cross(axis,solvedEnd-root));
        var classification = Math.Abs(derivative) < 1e-4 ? "toggle-or-branch-limit" : Math.Abs(derivative) < 5e-3 ? "near-toggle" : "regular";
        return new(theta, solvedEnd, lower, Vector3.Normalize(solvedLink), linkLength, Math.Abs(length-linkLength), derivative, classification);
    }

    private static Vector3 Rotate(Vector3 vector, Vector3 axis, double angle) => Vector3.Transform(vector, Quaternion.CreateFromAxisAngle(axis,(float)angle));
    private static double Polar(TubularArbMechanism3DConfig c) => Math.PI/32*(Math.Pow(c.OutsideDiameterM,4)-Math.Pow(c.InsideDiameterM,4));
    private static double SecondMoment(TubularArbMechanism3DConfig c) => Math.PI/64*(Math.Pow(c.OutsideDiameterM,4)-Math.Pow(c.InsideDiameterM,4));
    private static double TorsionalStiffness(TubularArbMechanism3DConfig c) => c.ShearModulusPa*Polar(c)/c.TorsionLengthM;
    private static void Validate(TubularArbMechanism3DConfig c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (Vector3.Distance(c.AxisStart,c.AxisEnd) <= 1e-6f) throw new ArgumentException("ARB axis requires two distinct points.");
        if (c.OutsideDiameterM <= 0 || c.InsideDiameterM < 0 || c.InsideDiameterM >= c.OutsideDiameterM) throw new ArgumentException("ARB requires OD > ID >= 0.");
        if (c.ShearModulusPa <= 0 || c.YoungModulusPa <= 0 || c.TorsionLengthM <= 0 || c.EnergyStiffnessScale <= 0) throw new ArgumentException("ARB material moduli, active torsion length, and energy stiffness scale must be positive.");
    }
}
