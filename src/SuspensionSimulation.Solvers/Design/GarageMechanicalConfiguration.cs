using System.Collections.Immutable;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.Solvers.Design;

/// <summary>Car-owned scalar force-law values; source definitions and manifests stay immutable.</summary>
public static class GarageMechanicalConfiguration
{
    public static ImmutableArray<InstanceMechanicalValue> Capture(ResolvedAssemblyGraph graph)=>[..graph.ElasticElements.Select(e=>e.Component.Behavior switch
    {
        CompressionSpringBehavior s=>(InstanceMechanicalValue?)new InstanceSpringRate(e.Path,s.RateKgfPerMm),
        TorsionBarBehavior t=>new InstanceTorsionSection(e.Path,t.PrescribedRateNmPerRad is {} rate?rate*32*t.TorsionLengthM/(Math.PI*(Math.Pow(t.OutsideDiameterM,4)-Math.Pow(t.InsideDiameterM,4))):t.ShearModulusPa,t.OutsideDiameterM,t.InsideDiameterM),
        _=>null
    }).OfType<InstanceMechanicalValue>()];
    public static ResolvedAssemblyGraph Apply(ResolvedAssemblyGraph graph,ImmutableArray<InstanceMechanicalValue>? values)
    {
        if(values is null) return graph with {ConfiguredMechanics=null}; // Legacy frozen replay only.
        InstanceMechanicalValidation.Validate(values.Value);var lookup=values.Value.ToDictionary(v=>v.InstancePath,StringComparer.Ordinal);
        var required=graph.ElasticElements.Where(e=>e.Component.Behavior is CompressionSpringBehavior or TorsionBarBehavior).Select(e=>e.Path).ToHashSet(StringComparer.Ordinal);
        if(!required.SetEquals(lookup.Keys)) throw new ArgumentException("Saved car mechanical values must cover exactly its installed spring/bar instances. Reopen Garage and review the selected hardware.");
        var elements=graph.ElasticElements.Select(e=>
        {
            if(!lookup.TryGetValue(e.Path,out var setting)) return e;
            var behavior=(setting,e.Component.Behavior) switch
            {
                (InstanceSpringRate s,CompressionSpringBehavior spring)=>(MechanicalBehavior)(spring with {RateKgfPerMm=s.RateKgfPerMm}),
                (InstanceTorsionSection t,TorsionBarBehavior bar)=>bar with {ShearModulusPa=t.ShearModulusPa,OutsideDiameterM=t.OutsideDiameterM,InsideDiameterM=t.InsideDiameterM,PrescribedRateNmPerRad=null},
                _=>throw new ArgumentException("Saved mechanical value type differs from installed hardware: "+e.Path)
            };
            var component=e.Component with {Behavior=behavior,IsRuntimeDerived=true};MechanicalValidation.Validate(component);
            if(behavior is TorsionBarBehavior torsion&&(!double.IsFinite(torsion.StiffnessNmPerRad)||torsion.StiffnessNmPerRad<=0)) throw new ArgumentException("Configured torsional stiffness is invalid: "+e.Path);
            return e with {Component=component};
        }).ToImmutableArray();
        return graph with {ElasticElements=elements,ConfiguredMechanics=values};
    }
}
