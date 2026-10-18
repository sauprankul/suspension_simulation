using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace SuspensionSimulation.Design;

public sealed record ParameterChoice(string Id,string Name);
public sealed record DefinitionParameter(string Id,string Name,string Unit,double Minimum,double Maximum,string Description="")
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ImmutableArray<ParameterChoice>? Choices {get;init;}
    public static DefinitionParameter Choice(string id,string name,IEnumerable<ParameterChoice> choices,string description="")=>new(id,name,"choice",0,0,description) {Choices=[..choices]};
}
public sealed record ParameterValue(string ParameterId,double Value)
{
    // Numeric payload remains zero for a choice; IDs are never encoded as numeric indices.
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public string? ChoiceId {get;init;}
    public static ParameterValue Choice(string id,string choice)=>new(id,0) {ChoiceId=choice};
}
public sealed record ParameterAssignment(string ParameterId,double? Value=null,string? FromParameterId=null)
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public string? ChoiceId {get;init;}
    public static ParameterAssignment Choice(string id,string choice)=>new(id) {ChoiceId=choice};
}
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
        {
            if(!ValidId(p.Id)||string.IsNullOrWhiteSpace(p.Name)||string.IsNullOrWhiteSpace(p.Unit)||!double.IsFinite(p.Minimum)||!double.IsFinite(p.Maximum)||p.Minimum>p.Maximum)
                throw new ArgumentException("A parameter needs a local ID, name, unit and finite ordered bounds: "+p.Id);
            if(p.Choices is {} choices&&(p.Unit!="choice"||p.Minimum!=0||p.Maximum!=0||choices.IsDefaultOrEmpty||choices.Length>512||choices.Select(c=>c.Id).Distinct(StringComparer.Ordinal).Count()!=choices.Length||choices.Any(c=>string.IsNullOrWhiteSpace(c.Id)||c.Id.Length>200||string.IsNullOrWhiteSpace(c.Name)))) throw new ArgumentException("A named-choice parameter needs unique named IDs and a choice contract: "+p.Id);
            if(p.Choices is null&&p.Unit=="choice") throw new ArgumentException("Choice options are missing: "+p.Id);
        }
    }
    public static void ValidateValues(IEnumerable<ParameterValue> values)
    {
        var items=values.ToArray();if(items.Length>512||items.Select(v=>v.ParameterId).Distinct(StringComparer.Ordinal).Count()!=items.Length||items.Any(v=>!ValidId(v.ParameterId)||!double.IsFinite(v.Value)||v.ChoiceId is {} choice&&(string.IsNullOrWhiteSpace(choice)||choice.Length>200||v.Value!=0)))
            throw new ArgumentException("Configuration values require unique local parameter IDs and finite numbers.");
    }
    public static ImmutableArray<ParameterValue> Resolve(IEnumerable<DefinitionParameter> declarations,IEnumerable<ParameterValue> values)
    {
        var definitions=declarations.ToArray();var selected=values.ToArray();Validate(definitions);ValidateValues(selected);
        if(!definitions.Select(p=>p.Id).ToHashSet(StringComparer.Ordinal).SetEquals(selected.Select(p=>p.ParameterId)))
            throw new ArgumentException("Configuration must assign exactly the parameters declared by its imported definition; properties cannot be overridden.");
        var lookup=selected.ToDictionary(v=>v.ParameterId,StringComparer.Ordinal);
        foreach(var p in definitions)
        {var value=lookup[p.Id];if(p.Choices is {} choices?value.ChoiceId is null||!choices.Any(c=>c.Id==value.ChoiceId):value.ChoiceId is not null||value.Value<p.Minimum||value.Value>p.Maximum) throw new ArgumentException("Configuration is outside the declared type/range/choices: "+p.Id);}
        return [..definitions.Select(p=>lookup[p.Id])];
    }
    public static ImmutableArray<ParameterValue> Import(IEnumerable<DefinitionParameter> childDeclarations,IEnumerable<ParameterAssignment> assignments,
        IEnumerable<DefinitionParameter> ownerDeclarations,IEnumerable<ParameterValue> ownerValues)
    {
        var child=childDeclarations.ToArray();var bindings=assignments.ToArray();var owner=ownerDeclarations.ToDictionary(p=>p.Id,StringComparer.Ordinal);var selected=ownerValues.ToDictionary(v=>v.ParameterId,StringComparer.Ordinal);
        if(bindings.Select(b=>b.ParameterId).Distinct(StringComparer.Ordinal).Count()!=bindings.Length) throw new ArgumentException("Duplicate child parameter assignment.");
        var result=new List<ParameterValue>();
        foreach(var b in bindings)
        {
            if((b.Value is null?0:1)+(b.ChoiceId is null?0:1)+(b.FromParameterId is null?0:1)!=1) throw new ArgumentException("Assign one numeric literal, named choice or owner parameter.");
            var target=child.SingleOrDefault(p=>p.Id==b.ParameterId)??throw new ArgumentException("The child does not declare parameter "+b.ParameterId+"; an imported property is read-only.");
            if(b.FromParameterId is {} id)
            {
                if(!owner.TryGetValue(id,out var source)||!selected.TryGetValue(id,out var value)) throw new ArgumentException("Forwarded parameter is not declared/configured by this immediate owner: "+id);
                if(!CanForward(source,target)) throw new ArgumentException("Forwarding must preserve type/units and stay inside the child's range/choices: "+id);
                result.Add(value with {ParameterId=target.Id});
            }
            else result.Add(b.ChoiceId is {} choice?ParameterValue.Choice(target.Id,choice):new(target.Id,b.Value!.Value));
        }
        return Resolve(child,result);
    }
    /// <summary>Explicit temporary importer values for inspection only; never a definition default.</summary>
    public static ImmutableArray<ParameterValue> Preview(IEnumerable<DefinitionParameter> parameters)=>[..parameters.Select(p=>p.Choices is {} choices?ParameterValue.Choice(p.Id,choices[0].Id):new ParameterValue(p.Id,p.Minimum/2+p.Maximum/2))];
    public static bool CanForward(DefinitionParameter source,DefinitionParameter target)=>source.Unit==target.Unit&&(source.Choices is {} choices?target.Choices is {} allowed&&choices.All(c=>allowed.Any(a=>a.Id==c.Id)):target.Choices is null&&source.Minimum>=target.Minimum&&source.Maximum<=target.Maximum);
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
        var referenced=ComponentParameters.ValidateBindings(component);
        if(component.MechanicalTemplate is not {} template)
        {if(!component.IsRuntimeDerived&&!referenced.SetEquals(parameters.Select(p=>p.Id))) throw new ArgumentException("These component parameters have no supported mechanical/adjustment binding.");return;}
        if(component.Behavior is not null) throw new ArgumentException("Choose a source mechanical template or a concrete behavior, not both.");
        if(component.Kind==ComponentKind.BumpStop) throw new ArgumentException("Bump-stop material is a component property, not a configurable mechanical parameter.");
        foreach(var (input,unit,positive) in Inputs(template))
        {
            if(input is NumericProperty property)
            {if(!double.IsFinite(property.Value)||(positive?property.Value<=0:property.Value<0)) throw new ArgumentException("Invalid mechanical property.");}
            else if(input is NumericParameterReference reference)
            {
                var definition=parameters.SingleOrDefault(p=>p.Id==reference.ParameterId)??throw new ArgumentException("Undeclared component parameter: "+reference.ParameterId);
                if(definition.Choices is not null||definition.Unit!=unit||(positive?definition.Minimum<=0:definition.Minimum<0)) throw new ArgumentException("Parameter units/range differ from their mechanical role: "+reference.ParameterId);
                referenced.Add(reference.ParameterId);
            }
            else throw new ArgumentException("A mechanical field must be a property or a declared parameter reference.");
        }
        if(!referenced.SetEquals(parameters.Select(p=>p.Id))) throw new ArgumentException("Every component parameter must bind to a supported mechanical field.");
        if(template is TorsionMechanicalTemplate {PrescribedRateNmPerRad:not null}&&Inputs(template).Any(i=>i.Definition is NumericParameterReference)) throw new ArgumentException("Parameterized torsion dimensions require geometry-derived stiffness; clear the prescribed rate.");
        // Probe intrinsic properties with range extrema that permit the widest bore.
        // This is validation, never a selected/default configuration or a physical result.
        var probe=parameters.Select(p=>p.Choices is {} choices?ParameterValue.Choice(p.Id,choices[0].Id):new ParameterValue(p.Id,p.Minimum)).ToArray();
        if(template is TorsionMechanicalTemplate {OutsideDiameterM:NumericParameterReference od})
            probe=probe.Select(v=>v.ParameterId==od.ParameterId?v with {Value=parameters.Single(p=>p.Id==od.ParameterId).Maximum}:v).ToArray();
        MechanicalValidation.Validate(ResolveUnchecked(component,probe));
    }
    public static ComponentDefinition Resolve(ComponentDefinition component,IEnumerable<ParameterValue> values)
    {
        var selected=DefinitionParameters.Resolve(component.Parameters??[],values);
        var result=component.MechanicalTemplate is null?component:ResolveUnchecked(component,selected);
        result=ComponentParameters.FreezeResolved(result,selected);MechanicalValidation.Validate(result);return result;
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
