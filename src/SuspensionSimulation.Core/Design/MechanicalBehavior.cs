using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace SuspensionSimulation.Design;

[JsonPolymorphic(TypeDiscriminatorPropertyName="kind")]
[JsonDerivedType(typeof(CompressionSpringBehavior),"compressionSpring")]
[JsonDerivedType(typeof(AxialDamperBehavior),"axialDamper")]
[JsonDerivedType(typeof(TorsionBarBehavior),"torsionBar")]
[JsonDerivedType(typeof(BumpCurveBehavior),"bumpCurve")]
public abstract record MechanicalBehavior(string FirstPortId,string SecondPortId);
public sealed record CompressionSpringBehavior(string FirstPortId,string SecondPortId,double RateKgfPerMm,double FreeLengthM,double BlockLengthM)
    :MechanicalBehavior(FirstPortId,SecondPortId);
public sealed record IntegratedSpringSeats(double UpperInsetM,double LowerInsetM,string? PerchAdjustmentId=null);
public enum DamperFamily { Strut, Shock }
public sealed record AxialDamperBehavior(string FirstPortId,string SecondPortId,DamperFamily Family,double MinimumEyeLengthM,double MaximumEyeLengthM,
    double CompressionNsPerM,double ReboundNsPerM,double GasExtensionForceN,IntegratedSpringSeats? SpringSeats=null,string? BodyLengthAdjustmentId=null)
    :MechanicalBehavior(FirstPortId,SecondPortId);
public sealed record TorsionBarBehavior(string FirstPortId,string SecondPortId,DesignVector UnitAxis,double ShearModulusPa,double OutsideDiameterM,double InsideDiameterM,
    double TorsionLengthM,double RestTwistRad=0,double? PrescribedRateNmPerRad=null):MechanicalBehavior(FirstPortId,SecondPortId)
{
    public double StiffnessNmPerRad=>PrescribedRateNmPerRad??ShearModulusPa*Math.PI*(Math.Pow(OutsideDiameterM,4)-Math.Pow(InsideDiameterM,4))/(32*TorsionLengthM);
}
public sealed record BumpCurveSample(double CompressionM,double ForceN);
public enum CurveZero { Contact, Installed }
public sealed record BumpCurveBehavior(string FirstPortId,string SecondPortId,double ContactLengthM,ImmutableArray<BumpCurveSample> Samples,
    CurveZero Zero,string Conditions,string EvidenceSha256):MechanicalBehavior(FirstPortId,SecondPortId);

public static class MechanicalValidation
{
    public static void Validate(ComponentDefinition c)
    {
        if(c.Behavior is not {} behavior)
        {if(c.Kind is ComponentKind.Spring or ComponentKind.Damper or ComponentKind.BumpStop or ComponentKind.AntiRollBar) throw new ArgumentException("This elastic component needs a mechanical specification.");return;}
        void Require(bool condition,string message) {if(!condition) throw new ArgumentException(message);}
        void Positive(double v,string name)=>Require(double.IsFinite(v)&&v>0,name+" must be positive and finite.");
        void Nonnegative(double v,string name)=>Require(double.IsFinite(v)&&v>=0,name+" must be nonnegative and finite.");
        Require(behavior.FirstPortId!=behavior.SecondPortId&&c.Ports.Length==2,"A force element requires two distinct attachment ports.");
        Require(c.Ports.Any(p=>p.Id==behavior.FirstPortId)&&c.Ports.Any(p=>p.Id==behavior.SecondPortId),"Force element attachment is missing.");
        foreach(var port in c.Ports) Require(port.SupportedJoints.All(j=>j==(behavior is TorsionBarBehavior?JointKind.Fixed:JointKind.Spherical)),"Force ports support only their declared massless attachment mate.");
        Require((c.Adjustments??[]).All(a=>a.PointIds.IsEmpty&&a.FrameIds.IsEmpty),"Massless force-element adjustments select declared mechanical settings; their attachment centers come from the mating hardware.");
        switch(behavior)
        {
            case CompressionSpringBehavior spring:
                Require(c.Kind==ComponentKind.Spring,"Spring behavior belongs to a spring.");Positive(spring.RateKgfPerMm,"Spring rate");Positive(spring.BlockLengthM,"Block length");Positive(spring.FreeLengthM,"Free length");Require(spring.BlockLengthM<spring.FreeLengthM,"Block length must be below free length.");break;
            case AxialDamperBehavior damper:
                Require(c.Kind==ComponentKind.Damper&&Enum.IsDefined(damper.Family),"Invalid damper family.");Positive(damper.MinimumEyeLengthM,"Minimum eye length");Positive(damper.MaximumEyeLengthM,"Maximum eye length");Require(damper.MaximumEyeLengthM>damper.MinimumEyeLengthM,"Damper stroke must be positive.");
                Nonnegative(damper.CompressionNsPerM,"Compression damping");Nonnegative(damper.ReboundNsPerM,"Rebound damping");Nonnegative(damper.GasExtensionForceN,"Gas force");
                if(damper.SpringSeats is {} seats) {Nonnegative(seats.UpperInsetM,"Upper seat inset");Nonnegative(seats.LowerInsetM,"Lower seat inset");Require(seats.UpperInsetM+seats.LowerInsetM<damper.MinimumEyeLengthM,"Spring seats cross before minimum damper length.");if(seats.PerchAdjustmentId is {} id) Require((c.Adjustments??[]).Any(a=>a.Id==id&&a.Path is ThreadedAdjustmentPath),"Integrated perch requires a declared threaded adjustment.");}
                if(damper.BodyLengthAdjustmentId is {} bodyId) Require((c.Adjustments??[]).Any(a=>a.Id==bodyId&&a.Path is ThreadedAdjustmentPath)&&bodyId!=damper.SpringSeats?.PerchAdjustmentId,"Threaded body length must be a distinct declared adjustment.");
                Require((c.Adjustments??[]).All(a=>a.Id==damper.SpringSeats?.PerchAdjustmentId||a.Id==damper.BodyLengthAdjustmentId),"This damper adjustment has no mechanical role.");
                break;
            case TorsionBarBehavior bar:
                Require(c.Kind==ComponentKind.AntiRollBar,"Torsional behavior belongs to an anti-roll bar.");Require(bar.UnitAxis.IsFinite&&Math.Abs(bar.UnitAxis.Length-1)<1e-9,"Torsion axis must be a unit vector.");
                Positive(bar.ShearModulusPa,"Shear modulus");Positive(bar.OutsideDiameterM,"Bar OD");Nonnegative(bar.InsideDiameterM,"Bar ID");Require(bar.InsideDiameterM<bar.OutsideDiameterM,"Bar ID must be below OD.");Positive(bar.TorsionLengthM,"Torsion section length");Require(double.IsFinite(bar.RestTwistRad),"Rest twist must be finite.");if(bar.PrescribedRateNmPerRad is {} rate) Positive(rate,"Prescribed torsional rate");break;
            case BumpCurveBehavior curve:
                Require(c.Kind==ComponentKind.BumpStop&&Enum.IsDefined(curve.Zero),"Invalid bump-stop behavior.");Positive(curve.ContactLengthM,"Contact eye separation");Require(curve.Samples.Length>=2,"A bump curve requires at least two samples.");
                Require(curve.Samples[0].CompressionM==0,"Curve displacement must start at its declared zero.");if(curve.Zero==CurveZero.Contact) Require(curve.Samples[0].ForceN==0,"A contact-origin curve starts at zero force.");
                for(var i=0;i<curve.Samples.Length;i++) {var sample=curve.Samples[i];Nonnegative(sample.CompressionM,"Curve displacement");Nonnegative(sample.ForceN,"Curve force");if(i>0) Require(sample.CompressionM>curve.Samples[i-1].CompressionM&&sample.ForceN>=curve.Samples[i-1].ForceN,"Curve samples must increase in displacement without negative tangent.");}
                Require(!string.IsNullOrWhiteSpace(curve.Conditions)&&curve.EvidenceSha256 is {Length:64}&&curve.EvidenceSha256.All(Uri.IsHexDigit),"Curve conditions and evidence hash are required.");break;
            default:throw new ArgumentException("Unsupported mechanical behavior.");
        }
    }
}
