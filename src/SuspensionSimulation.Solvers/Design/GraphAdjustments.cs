using System.Collections.Immutable;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.Solvers.Design;

/// <summary>Derived installed geometry. Exact source revisions/hashes remain untouched in the manifest and bundle.</summary>
public static class GraphAdjustments
{
    public static ResolvedAssemblyGraph Freeze(ResolvedAssemblyGraph source,IEnumerable<AdjustmentSelection> selections)
    {
        var selected=selections.ToArray();
        if(selected.GroupBy(x=>(x.InstancePath,x.AdjustmentId)).Any(g=>g.Count()>1)) throw new ArgumentException("Duplicate adjustment selection.");
        var sourceDefinitions=source.Components.ToDictionary(c=>c.Identity.AssetId,StringComparer.Ordinal);
        var paths=source.Bodies.Select(b=>b.Path).Concat(source.ElasticElements.Select(e=>e.Path)).ToHashSet(StringComparer.Ordinal);
        if(selected.Any(s=>!paths.Contains(s.InstancePath))) throw new ArgumentException("Adjustment instance path does not exist.");
        var resolved=new List<ResolvedAdjustment>();
        ComponentDefinition Adjust(string path,ComponentDefinition component)
        {
            component=sourceDefinitions[component.Identity.AssetId];
            component=MechanicalTemplates.Resolve(component,source.ParameterConfigurations.SingleOrDefault(c=>c.InstancePath==path)?.Values??[]);
            var values=new Dictionary<string,AdjustmentSetting>(StringComparer.Ordinal);
            foreach(var selection in selected.Where(s=>s.InstancePath==path)) values.Add(selection.AdjustmentId,selection switch
            {ContinuousAdjustmentSelection c=>new(c.Fraction),DiscreteAdjustmentSelection d=>new(ChoiceId:d.ChoiceId),_=>throw new ArgumentException("Unsupported adjustment selection.")});
            foreach(var a in component.Adjustments??[])
            {var setting=values.GetValueOrDefault(a.Id)??a.Default;resolved.Add(new(path,a.Id,setting,AdjustmentPaths.Evaluate(a.Path,setting)));}
            return AdjustmentPaths.Freeze(component,values);
        }
        var bodies=source.Bodies.Select(b=>b with {Component=Adjust(b.Path,b.Component)}).ToImmutableArray();var lookup=bodies.ToDictionary(b=>b.Path,StringComparer.Ordinal);
        ResolvedPort Port(ResolvedPort p)
        {
            var body=lookup[p.BodyPath];var port=body.Component.Ports.Single(x=>x.Id==p.PortId);
            return p with {Frame=body.Transform.Frame(body.Component.Frames.Single(f=>f.Id==port.FrameId).Frame)};
        }
        var result=source with {Bodies=bodies,Joints=[..source.Joints.Select(j=>j with {First=Port(j.First),Second=Port(j.Second)})],
            Exports=source.Exports.ToImmutableDictionary(x=>x.Key,x=>Port(x.Value),StringComparer.Ordinal),Interfaces=source.Interfaces.ToImmutableDictionary(x=>x.Key,x=>Port(x.Value),StringComparer.Ordinal),
            ElasticElements=[..source.ElasticElements.Select(e=>e with {Component=Adjust(e.Path,e.Component),First=Port(e.First),Second=Port(e.Second)})],AppliedAdjustments=[..resolved]};
        return GarageMechanicalConfiguration.Apply(result,source.ConfiguredMechanics);
    }
    public static double ThreadedOffset(ResolvedAssemblyGraph graph,string path,string? adjustmentId)
    {
        if(adjustmentId is null) return 0;
        var selected=graph.AppliedAdjustments.SingleOrDefault(a=>a.InstancePath==path&&a.AdjustmentId==adjustmentId)??throw new ArgumentException("Mechanical adjustment was not frozen: "+path+":"+adjustmentId);
        var component=graph.ElasticElements.Single(e=>e.Path==path).Component;var definition=(component.Adjustments??[]).Single(a=>a.Id==adjustmentId);
        var nominal=AdjustmentPaths.Evaluate(definition.Path,definition.Default);var thread=definition.Path as ThreadedAdjustmentPath??throw new ArgumentException("Mechanical length setting must be threaded.");
        return (selected.Frame.OriginM-nominal.OriginM).Dot(thread.Datum.Axis);
    }
}
