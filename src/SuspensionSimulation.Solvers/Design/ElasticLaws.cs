using SuspensionSimulation.Design;

namespace SuspensionSimulation.Solvers.Design;

public sealed record AxialElasticState(double ForceN,double TangentNPerM,double EnergyJ,double CompressionM,bool Unseated,
    double? MinimumLengthM,double? MaximumLengthM);
public sealed record TorsionalElasticState(double TorqueNm,double TangentNmPerRad,double EnergyJ);

public static class ElasticLaws
{
    // Force is positive when the element tries to extend. Reactions at rigid limits are separate unknowns.
    public static AxialElasticState Spring(CompressionSpringBehavior spring,double seatLengthM)
    {
        if(!double.IsFinite(seatLengthM)||seatLengthM<spring.BlockLengthM-1e-9) throw new ArgumentOutOfRangeException(nameof(seatLengthM),"Coil bind is a rigid unilateral constraint; do not evaluate a penetrated spring.");
        var compression=Math.Max(0,spring.FreeLengthM-seatLengthM);var k=spring.RateKgfPerMm*DesignConventions.NewtonsPerMetrePerKgfPerMm;
        return new(k*compression,compression>0?k:0,.5*k*compression*compression,compression,seatLengthM>spring.FreeLengthM,spring.BlockLengthM,null);
    }
    public static double DamperForce(AxialDamperBehavior damper,double eyeLengthM,double compressionVelocityMPerS)
    {
        if(!double.IsFinite(eyeLengthM)||eyeLengthM<damper.MinimumEyeLengthM-1e-9||eyeLengthM>damper.MaximumEyeLengthM+1e-9||!double.IsFinite(compressionVelocityMPerS)) throw new ArgumentOutOfRangeException(nameof(eyeLengthM),"Damper stroke is a rigid unilateral constraint.");
        return damper.GasExtensionForceN+compressionVelocityMPerS*(compressionVelocityMPerS>=0?damper.CompressionNsPerM:damper.ReboundNsPerM);
    }
    public static double SeatSeparation(AxialDamperBehavior damper,double eyeLengthM,double perchOffsetM)
    {
        var seats=damper.SpringSeats??throw new ArgumentException("This damper has no integrated spring seats.");
        if(!double.IsFinite(eyeLengthM)||!double.IsFinite(perchOffsetM)) throw new ArgumentException("Seat dimensions must be finite.");
        return eyeLengthM-seats.UpperInsetM-seats.LowerInsetM-perchOffsetM;
    }
    public static TorsionalElasticState Bar(TorsionBarBehavior bar,double firstAngleRad,double secondAngleRad)
    {
        if(!double.IsFinite(firstAngleRad)||!double.IsFinite(secondAngleRad)) throw new ArgumentException("Bar angles must be finite.");
        var twist=secondAngleRad-firstAngleRad-bar.RestTwistRad;var k=bar.StiffnessNmPerRad;return new(k*twist,k,.5*k*twist*twist);
    }
    public static AxialElasticState Bump(BumpCurveBehavior curve,double eyeLengthM)
    {
        if(!double.IsFinite(eyeLengthM)) throw new ArgumentException("Bump-stop separation must be finite.");
        var compression=curve.ContactLengthM-eyeLengthM;
        if(compression<0)
        {
            if(curve.Zero==CurveZero.Installed) throw new ArgumentOutOfRangeException(nameof(eyeLengthM),"Installed-origin curve does not define the unloaded branch.");
            return new(0,0,0,0,false,null,null);
        }
        if(compression>curve.Samples[^1].CompressionM+1e-12) throw new ArgumentOutOfRangeException(nameof(eyeLengthM),"Compression is beyond the validated curve interval. No artificial rigid stop or extrapolation is supplied.");
        var energy=0d;
        for(var i=1;i<curve.Samples.Length;i++)
        {
            var a=curve.Samples[i-1];var b=curve.Samples[i];var dx=Math.Min(compression,b.CompressionM)-a.CompressionM;var k=(b.ForceN-a.ForceN)/(b.CompressionM-a.CompressionM);
            if(dx>=0) energy+=a.ForceN*dx+.5*k*dx*dx;
            if(compression<=b.CompressionM+1e-12) return new(a.ForceN+k*Math.Max(0,dx),k,energy,compression,false,null,null);
        }
        throw new InvalidOperationException("Curve interpolation failed.");
    }
}
