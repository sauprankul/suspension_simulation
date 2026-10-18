using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace SuspensionSimulation.Design;

public sealed record DefinitionParameter(string Id,string Name,string Unit,double Minimum,double Maximum,string Description="");
public sealed record ParameterValue(string ParameterId,double Value);
public sealed record ParameterAssignment(string ParameterId,double? Value=null,string? FromParameterId=null);
public sealed record ResolvedInstanceConfiguration(string InstancePath,ImmutableArray<ParameterValue> Values);

[JsonPolymorphic(TypeDiscriminatorPropertyName="kind")]
[JsonDerivedType(typeof(NumericProperty),"property")]
[JsonDerivedType(typeof(NumericParameterReference),"parameter")]
public abstract record NumericDefinition;
public sealed record NumericProperty(double Value):NumericDefinition;
public sealed record NumericParameterReference(string ParameterId):NumericDefinition;

[JsonPolymorphic(TypeDiscriminatorPropertyName="kind")]
[JsonDerivedType(typeof(SpringMechanicalTemplate),"spring")]
[JsonDerivedType(typeof(TorsionMechanicalTemplate),"torsion")]
public abstract record MechanicalTemplate(string FirstPortId,string SecondPortId);
public sealed record SpringMechanicalTemplate(string FirstPortId,string SecondPortId,NumericDefinition RateKgfPerMm,double FreeLengthM,double BlockLengthM):MechanicalTemplate(FirstPortId,SecondPortId);
public sealed record TorsionMechanicalTemplate(string FirstPortId,string SecondPortId,DesignVector UnitAxis,NumericDefinition ShearModulusPa,
    NumericDefinition OutsideDiameterM,NumericDefinition InsideDiameterM,double TorsionLengthM,double RestTwistRad,double? PrescribedRateNmPerRad=null):MechanicalTemplate(FirstPortId,SecondPortId);

/// <summary>Owner declarations contain no selected values. Only an immediate importer supplies them.</summary>
public static class DefinitionParameters
{
    public static bool ValidId(string id)=>!string.IsNullOrWhiteSpace(id)&&id.Length<=200&&id.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_' or '.');
    public static void Validate(IEnumerable<DefinitionParameter> parameters)
    {
        var items=parameters.ToArray();if(items.Length>512||items.Select(p=>p.Id).Distinct(StringComparer.Ordinal).Count()!=items.Length) throw new ArgumentException("Parameter IDs must be unique and inside the 512-parameter budget.");
        foreach(var p in items)
            if(!ValidId(p.Id)||string.IsNullOrWhiteSpace(p.Name)||string.IsNullOrWhiteSpace(p.Unit)||!double.IsFinite(p.Minimum)||!double.IsFinite(p.Maximum)||p.Minimum>p.Maximum)
                throw new ArgumentException("A parameter needs a local ID, name, unit and finite ordered bounds: "+p.Id);
    }
    public static void ValidateValues(IEnumerable<ParameterValue> values)
    {
        var items=values.ToArray();if(items.Length>512||items.Select(v=>v.ParameterId).Distinct(StringComparer.Ordinal).Count()!=items.Length||items.Any(v=>!ValidId(v.ParameterId)||!double.IsFinite(v.Value)))
            throw new ArgumentException("Configuration values require unique local parameter IDs and finite numbers.");
    }
    public static ImmutableArray<ParameterValue> Resolve(IEnumerable<DefinitionParameter> declarations,IEnumerable<ParameterValue> values)
    {
        var definitions=declarations.ToArray();var selected=values.ToArray();Validate(definitions);ValidateValues(selected);
        if(!definitions.Select(p=>p.Id).ToHashSet(StringComparer.Ordinal).SetEquals(selected.Select(p=>p.ParameterId)))
            throw new ArgumentException("Configuration must assign exactly the parameters declared by its imported definition; properties cannot be overridden.");
        var lookup=selected.ToDictionary(v=>v.ParameterId,StringComparer.Ordinal);
        foreach(var p in definitions) if(lookup[p.Id].Value<p.Minimum||lookup[p.Id].Value>p.Maximum) throw new ArgumentException("Configuration is outside the declared range: "+p.Id);
        return [..definitions.Select(p=>lookup[p.Id])];
    }
    public static ImmutableArray<ParameterValue> Import(IEnumerable<DefinitionParameter> childDeclarations,IEnumerable<ParameterAssignment> assignments,
        IEnumerable<DefinitionParameter> ownerDeclarations,IEnumerable<ParameterValue> ownerValues)
    {
        var child=childDeclarations.ToArray();var bindings=assignments.ToArray();var owner=ownerDeclarations.ToDictionary(p=>p.Id,StringComparer.Ordinal);var selected=ownerValues.ToDictionary(v=>v.ParameterId,v=>v.Value,StringComparer.Ordinal);
        if(bindings.Select(b=>b.ParameterId).Distinct(StringComparer.Ordinal).Count()!=bindings.Length) throw new ArgumentException("Duplicate child parameter assignment.");
        var result=new List<ParameterValue>();
        foreach(var b in bindings)
        {
            if((b.Value is null)==(b.FromParameterId is null)) throw new ArgumentException("Assign either a concrete value or an owner parameter, never both/neither.");
            var target=child.SingleOrDefault(p=>p.Id==b.ParameterId)??throw new ArgumentException("The child does not declare parameter "+b.ParameterId+"; an imported property is read-only.");
            if(b.FromParameterId is {} id)
            {
                if(!owner.TryGetValue(id,out var source)||!selected.TryGetValue(id,out var value)) throw new ArgumentException("Forwarded parameter is not declared/configured by this immediate owner: "+id);
                if(source.Unit!=target.Unit||source.Minimum<target.Minimum||source.Maximum>target.Maximum) throw new ArgumentException("Forwarding must preserve units and stay inside the child's declared range: "+id);
                result.Add(new(target.Id,value));
            }
            else result.Add(new(target.Id,b.Value!.Value));
        }
        return Resolve(child,result);
    }
    /// <summary>Explicit temporary importer values for inspection only; never a definition default.</summary>
    public static ImmutableArray<ParameterValue> Preview(IEnumerable<DefinitionParameter> parameters)=>[..parameters.Select(p=>new ParameterValue(p.Id,p.Minimum/2+p.Maximum/2))];
}

public static class MechanicalTemplates
{
    public static IEnumerable<(NumericDefinition Definition,string Unit,bool Positive)> Inputs(MechanicalTemplate template)=>template switch
    {
        SpringMechanicalTemplate s=>[(s.RateKgfPerMm,"kgf/mm",true)],
        TorsionMechanicalTemplate b=>[(b.ShearModulusPa,"Pa",true),(b.OutsideDiameterM,"m",true),(b.InsideDiameterM,"m",false)],
        _=>throw new ArgumentException("Unsupported mechanical template.")
    };
    public static void Validate(ComponentDefinition component)
    {
        var parameters=component.Parameters??[];DefinitionParameters.Validate(parameters);
        if(component.MechanicalTemplate is not {} template)
        {if(!parameters.IsEmpty&&!component.IsRuntimeDerived) throw new ArgumentException("These component parameters have no supported mechanical binding.");return;}
        if(component.Behavior is not null) throw new ArgumentException("Choose a source mechanical template or a concrete behavior, not both.");
        if(component.Kind==ComponentKind.BumpStop) throw new ArgumentException("Bump-stop material is a component property, not a configurable mechanical parameter.");
        var referenced=new HashSet<string>(StringComparer.Ordinal);
        foreach(var (input,unit,positive) in Inputs(template))
        {
            if(input is NumericProperty property)
            {if(!double.IsFinite(property.Value)||(positive?property.Value<=0:property.Value<0)) throw new ArgumentException("Invalid mechanical property.");}
            else if(input is NumericParameterReference reference)
            {
                var definition=parameters.SingleOrDefault(p=>p.Id==reference.ParameterId)??throw new ArgumentException("Undeclared component parameter: "+reference.ParameterId);
                if(definition.Unit!=unit||(positive?definition.Minimum<=0:definition.Minimum<0)) throw new ArgumentException("Parameter units/range differ from their mechanical role: "+reference.ParameterId);
                referenced.Add(reference.ParameterId);
            }
            else throw new ArgumentException("A mechanical field must be a property or a declared parameter reference.");
        }
        if(!referenced.SetEquals(parameters.Select(p=>p.Id))) throw new ArgumentException("Every component parameter must bind to a supported mechanical field.");
        if(template is TorsionMechanicalTemplate {PrescribedRateNmPerRad:not null}&&referenced.Count>0) throw new ArgumentException("Parameterized torsion dimensions require geometry-derived stiffness; clear the prescribed rate.");
        // Probe intrinsic properties with range extrema that permit the widest bore.
        // This is validation, never a selected/default configuration or a physical result.
        var probe=parameters.Select(p=>new ParameterValue(p.Id,p.Minimum)).ToArray();
        if(template is TorsionMechanicalTemplate {OutsideDiameterM:NumericParameterReference od})
            probe=probe.Select(v=>v.ParameterId==od.ParameterId?v with {Value=parameters.Single(p=>p.Id==od.ParameterId).Maximum}:v).ToArray();
        MechanicalValidation.Validate(ResolveUnchecked(component,probe));
    }
    public static ComponentDefinition Resolve(ComponentDefinition component,IEnumerable<ParameterValue> values)
    {
        var selected=DefinitionParameters.Resolve(component.Parameters??[],values);
        if(component.MechanicalTemplate is null) return component;
        var result=ResolveUnchecked(component,selected);MechanicalValidation.Validate(result);return result;
    }
    private static ComponentDefinition ResolveUnchecked(ComponentDefinition c,IEnumerable<ParameterValue> values)
    {
        var selected=values.ToDictionary(v=>v.ParameterId,v=>v.Value,StringComparer.Ordinal);
        double Number(NumericDefinition input)=>input switch {NumericProperty p=>p.Value,NumericParameterReference p=>selected[p.ParameterId],_=>throw new ArgumentException("Unsupported numeric definition.")};
        var behavior=c.MechanicalTemplate switch
        {
            SpringMechanicalTemplate s=>(MechanicalBehavior)new CompressionSpringBehavior(s.FirstPortId,s.SecondPortId,Number(s.RateKgfPerMm),s.FreeLengthM,s.BlockLengthM),
            TorsionMechanicalTemplate b=>new TorsionBarBehavior(b.FirstPortId,b.SecondPortId,b.UnitAxis,Number(b.ShearModulusPa),Number(b.OutsideDiameterM),Number(b.InsideDiameterM),b.TorsionLengthM,b.RestTwistRad,b.PrescribedRateNmPerRad),
            _=>throw new ArgumentException("Unsupported mechanical template.")
        };
        return c with {Behavior=behavior,MechanicalTemplate=null,IsRuntimeDerived=true};
    }
}
