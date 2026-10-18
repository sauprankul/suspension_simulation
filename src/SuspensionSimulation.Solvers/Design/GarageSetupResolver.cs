using SuspensionSimulation.Design;

namespace SuspensionSimulation.Solvers.Design;

/// <summary>Enforces setup ownership against the actual resolved hardware before any physical job starts.</summary>
public sealed class GarageSetupResolver(IDesignAssetSource source)
{
    public ResolvedAssemblyGraph Resolve(GarageSetup setup)
        =>Freeze(new AssemblyGraphResolver(source).Compile(setup.RootAssembly,setup.Equipment,setup.ParameterValues??[]),setup);
    public static ResolvedAssemblyGraph Freeze(ResolvedAssemblyGraph graph,GarageSetup setup)
    {
        DesignValidation.Validate(setup);
        var configured=DefinitionParameters.Resolve(graph.Root.Parameters??[],setup.ParameterValues??[]);
        if(!configured.SequenceEqual(graph.ParameterConfigurations.Single(c=>c.InstancePath=="").Values)) throw new ArgumentException("Resolved parameter configuration does not match the saved Garage setup.");
        if(graph.Root.Parameters is not null&&setup.MechanicalValues is not null) throw new ArgumentException("Properties cannot be overridden with legacy mechanical values.");
        if(graph.Root.Identity.AssetId!=setup.RootAssembly.AssetId||!graph.SelectedEquipment.SequenceEqual(setup.Equipment)) throw new ArgumentException("Resolved source does not match the saved Garage configuration.");
        foreach(var selected in setup.Adjustments)
            if(!graph.SetupAdjustments.Any(a=>a.InstancePath==selected.InstancePath&&a.Definition.Id==selected.AdjustmentId))
                throw new ArgumentException("Adjustment is not exposed to Garage: "+selected.InstancePath+":"+selected.AdjustmentId);
        var frozen=GarageMechanicalConfiguration.Apply(GraphAdjustments.Freeze(graph,setup.Adjustments),setup.MechanicalValues);
        _=new InstalledEquilibrium(frozen); // Reject absent/unsupported vehicle contacts before scheduling a job.
        if(setup.Mass.Policy==MassAccountingPolicy.ComponentSum)
            foreach(var part in frozen.Bodies.Select(b=>(b.Path,b.Component)).Concat(frozen.ElasticElements.Select(e=>(e.Path,e.Component))))
                if(part.Component.Mass is null) throw new ArgumentException("Component-sum mass is missing: "+part.Path);
        return frozen;
    }
}
