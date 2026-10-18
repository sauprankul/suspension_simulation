namespace SuspensionSimulation.Design;

/// <summary>Immutable provenance of a computed curve; estimated quality cannot imply calibration.</summary>
public sealed record BumperFeaSource(BumperJobRecipe Recipe,string RecipeSha256,string StepSha256,string MeshSha256,
    string SolverSha256,string ResultSha256,string CsvSha256,string Quality);

public static class BumperFeaSources
{
    public static void Validate(ComponentDefinition component)
    {
        if(component.BumperFeaSource is not {} source) return;
        if(source.Quality!="CoarseEstimate"||source.Recipe.Quality!=BumperSolveQuality.CoarseEstimate||source.RecipeSha256!=BumpStudioContracts.Hash(source.Recipe)||
            !new[]{source.StepSha256,source.MeshSha256,source.SolverSha256,source.ResultSha256,source.CsvSha256}.All(BumpStudioContracts.IsSha))
            throw new ArgumentException("Incomplete or inconsistent FEA curve provenance.");
        if(component.BumpCurveEvidence is not {} evidence||evidence.RawSha256!=source.CsvSha256||evidence.Provenance!=SuspensionSimulation.Components.DataProvenance.Estimated||
            component.Identity.Provenance.Kind!=SuspensionSimulation.Components.DataProvenance.Estimated)
            throw new ArgumentException("A coarse FEA curve must retain its exact CSV and Estimated classification.");
        if(component.BumperMaterial is null||DesignJson.Serialize(component.BumperMaterial)!=DesignJson.Serialize(source.Recipe.Material))
            throw new ArgumentException("The computed curve's material is a source property. Run a new job after changing the material.");
        if(evidence.ContactSeparationM!=source.Recipe.FreeLengthM||Math.Abs(ImportedBumpCurve.Parse(evidence)[^1].CompressionM-source.Recipe.RequestedCompressionM)>1e-10)
            throw new ArgumentException("Computed curve zero/stroke differs from its frozen recipe.");
    }
}
