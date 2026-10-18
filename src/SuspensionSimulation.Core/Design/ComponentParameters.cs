using System.Collections.Immutable;

namespace SuspensionSimulation.Design;

/// <summary>Configuration changes only explicitly bound mechanical positions, never source geometry/properties.</summary>
public static class ComponentParameters
{
    public static HashSet<string> ValidateBindings(ComponentDefinition component)
    {
        var references=new HashSet<string>(StringComparer.Ordinal);var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var binding in component.AdjustmentParameters??[])
        {
            if(!seen.Add(binding.AdjustmentId)) throw new ArgumentException("Adjustment is bound more than once: "+binding.AdjustmentId);
            var adjustment=(component.Adjustments??[]).SingleOrDefault(a=>a.Id==binding.AdjustmentId)??throw new ArgumentException("Bound adjustment is missing: "+binding.AdjustmentId);
            var parameter=(component.Parameters??[]).SingleOrDefault(p=>p.Id==binding.ParameterId)??throw new ArgumentException("Adjustment parameter is undeclared: "+binding.ParameterId);
            if(adjustment.Path is FixedAdjustmentPath) throw new ArgumentException("A fixed mounting position is a property, not an adjustment parameter.");
            if(adjustment.Path is DiscreteAdjustmentPath holes)
            {if(parameter.Choices is not {} choices||choices.Any(c=>!holes.Choices.Any(h=>h.Id==c.Id))) throw new ArgumentException("Mounting-hole parameters must select actual named holes: "+parameter.Id);}
            else if(parameter.Choices is not null||parameter.Unit!="fraction"||parameter.Minimum<0||parameter.Maximum>1) throw new ArgumentException("Continuous adjustment parameters use a bounded fraction in [0,1]: "+parameter.Id);
            references.Add(parameter.Id);
        }
        return references;
    }
    public static ImmutableDictionary<string,AdjustmentSetting> Settings(ComponentDefinition component,IEnumerable<ParameterValue> values)
    {
        var selected=DefinitionParameters.Resolve(component.Parameters??[],values).ToDictionary(p=>p.ParameterId,StringComparer.Ordinal);
        return (component.AdjustmentParameters??[]).ToImmutableDictionary(b=>b.AdjustmentId,b=>selected[b.ParameterId].ChoiceId is {} choice?new AdjustmentSetting(ChoiceId:choice):new AdjustmentSetting(selected[b.ParameterId].Value),StringComparer.Ordinal);
    }
    public static ComponentDefinition FreezeResolved(ComponentDefinition source,IEnumerable<ParameterValue> values)=>source.AdjustmentParameters is null?source:AdjustmentPaths.Freeze(source,Settings(source,values));
}
