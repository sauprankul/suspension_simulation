using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace SuspensionSimulation.Design;

[JsonPolymorphic(TypeDiscriminatorPropertyName="kind")]
[JsonDerivedType(typeof(InstanceSpringRate),"springRate")]
[JsonDerivedType(typeof(InstanceTorsionSection),"torsionSection")]
public abstract record InstanceMechanicalValue(string InstancePath);
public sealed record InstanceSpringRate(string InstancePath,double RateKgfPerMm):InstanceMechanicalValue(InstancePath);
public sealed record InstanceTorsionSection(string InstancePath,double ShearModulusPa,double OutsideDiameterM,double InsideDiameterM):InstanceMechanicalValue(InstancePath);

public static class InstanceMechanicalValidation
{
    public static void Validate(ImmutableArray<InstanceMechanicalValue> values)
    {
        if(values.IsDefault||values.GroupBy(v=>v.InstancePath,StringComparer.Ordinal).Any(g=>g.Count()>1)) throw new ArgumentException("Duplicate or absent per-instance mechanical values.");
        foreach(var value in values)
        {
            if(string.IsNullOrWhiteSpace(value.InstancePath)) throw new ArgumentException("Mechanical configuration needs an instance path.");
            var valid=value switch
            {
                InstanceSpringRate s=>double.IsFinite(s.RateKgfPerMm)&&s.RateKgfPerMm>0&&double.IsFinite(s.RateKgfPerMm*DesignConventions.NewtonsPerMetrePerKgfPerMm),
                InstanceTorsionSection t=>double.IsFinite(t.ShearModulusPa)&&t.ShearModulusPa>0&&double.IsFinite(t.OutsideDiameterM)&&t.OutsideDiameterM>0&&double.IsFinite(t.InsideDiameterM)&&t.InsideDiameterM>=0&&t.InsideDiameterM<t.OutsideDiameterM,
                _=>false
            };
            if(!valid) throw new ArgumentException("Invalid per-instance mechanical values: "+value.InstancePath);
        }
    }
}
