using System.Collections.Immutable;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.Tests;

internal static class BumpStudioPreparationChecks
{
    internal static void VerifyContracts()
    {
        var sha=new string('a',64);var bounds=new CadBounds(new(0,0,0),new(.01,.01,.01));
        var model=new BumperCadInspection(sha,"synthetic-tetra.step",StepBumperPreparation.GmshSha256,StepLengthUnit.Metres,"m",false,.003,1,bounds,
            [new(1,bounds,[1,2,3]),new(2,bounds,[1,4,5]),new(3,bounds,[2,4,6]),new(4,bounds,[3,5,6])],
            [new(1,new(0,0,0)),new(2,new(.01,0,0)),new(3,new(0,.01,0)),new(4,new(0,0,.01))],
            [new(1,1,2,3),new(2,1,2,4),new(3,1,3,4),new(4,2,3,4)],["Synthetic contract fixture, not imported CAD"]);
        var material=BumpStudioContracts.MaterialCatalog().Single(c=>c.Id=="user-defined");var frame=AssemblyAuthoring.IdentityPlacement.Frame;
        var recipe=new BumperJobRecipe("Contract fixture",BumpStudioContracts.Hash(model),frame,.01,.004,
            [new("lower",[1],[],"lower",0,false),new("upper",[4],[],"upper",0,false)],
            [new("lower",RigidBumperFixtureKind.Plane,frame,0,.02,0,false),new("upper",RigidBumperFixtureKind.Plane,frame with {OriginM=new(0,0,.01)},0,.02,0,true)],material,new(.002,2,true));
        BumpStudioContracts.ValidateRecipe(recipe,model);
        void Reject(Action action) {try {action();}catch(ArgumentException) {return;}catch(InvalidOperationException) {return;}throw new Exception("Invalid Studio contract was accepted.");}
        Reject(()=>BumpStudioContracts.ValidateMaterial(material with {DensityKgPerM3=double.PositiveInfinity}));Reject(()=>BumpStudioContracts.ValidateMaterial(material with {ShoreA=double.NaN}));
        Reject(()=>BumpStudioContracts.ValidateInspection(model with {Bounds=bounds with {MinimumM=new(1,1,1)}}));Reject(()=>BumpStudioContracts.ValidateInspection(model with {Faces=model.Faces.SetItem(0,model.Faces[0] with {Bounds=bounds with {MaximumM=new(double.NaN,1,1)}})}));
        Reject(()=>BumpStudioContracts.ValidateRecipe(recipe with {Contacts=recipe.Contacts.SetItem(0,recipe.Contacts[0] with {SlaveFaceTags=[999]})},model));
        Reject(()=>BumpStudioContracts.ValidateRecipe(recipe with {RequestedCompressionM=.011},model));Reject(()=>BumpStudioContracts.ValidateRecipe(recipe with {BudgetSeconds=28801},model));
        Reject(()=>BumpStudioContracts.RequireVerifiedWorker(recipe,model,new HashSet<string>(),false));Reject(()=>BumpStudioContracts.RequireVerifiedWorker(recipe,model,new HashSet<string>(),true));
        var fitted=material with {Readiness=MaterialReadiness.Estimated,EvidenceSha256=[sha],Law=new("synthetic-contract-law",new Dictionary<string,double>{{"syntheticParameter",1}}.ToImmutableDictionary(),"Synthetic schema fixture only; no physical material prediction",sha),Validity=new(.3,"quasi-static loading","Synthetic contract conditions")};
        Reject(()=>BumpStudioContracts.RequireVerifiedWorker(recipe with {Material=fitted},model,new HashSet<string>{"synthetic-contract-law"},true));
        foreach(var card in EstimatedBumperMaterials.Catalog())
        {
            BumpStudioContracts.ValidateMaterial(card);if(card.Readiness!=MaterialReadiness.Estimated||card.Law is null) throw new Exception("Research preset was incorrectly classified.");
            var keys=EstimatedBumperMaterials.Keys(card);var values=keys.Select(k=>card.Law.ParametersSi[k]).ToArray();values[0]*=1.2;var changed=EstimatedBumperMaterials.Override(card,values);
            if(changed.Law!.ParametersSi[keys[0]]==card.Law.ParametersSi[keys[0]]||changed.Readiness!=MaterialReadiness.Estimated||BumpStudioContracts.Hash(changed)==BumpStudioContracts.Hash(card)) throw new Exception("Material override did not retain independent Estimated coefficients.");
            values[0]=-1;Reject(()=>EstimatedBumperMaterials.Override(card,values));
            Reject(()=>BumpStudioContracts.RequireVerifiedWorker(recipe with {Material=card},model,new HashSet<string>{card.Law.ModelKey},false));
        }
        var round=DesignJson.Deserialize<BumperJobRecipe>(DesignJson.Serialize(recipe));if(BumpStudioContracts.Hash(round)!=BumpStudioContracts.Hash(recipe)) throw new Exception("Frozen recipe identity did not round trip.");
    }
    internal static void Verify()=>VerifyAsync().GetAwaiter().GetResult();
    private static async Task VerifyAsync()
    {
        VerifyContracts();
        var repo=AppContext.BaseDirectory;while(!Directory.Exists(Path.Combine(repo,"data"))) repo=Directory.GetParent(repo)?.FullName??throw new Exception("Repository missing.");
        var step=Path.Combine(repo,"artifacts/fea-step-mesh-spike/asymmetric-mm.step");
        if(!File.Exists(step)) throw new Exception("Generate the explicit synthetic STEP benchmark before CAD preparation checks.");
        var folder=Path.Combine(Path.GetTempPath(),"studio-preparation-"+Guid.NewGuid().ToString("N"));
        try
        {
            var service=new StepBumperPreparation(repo,folder);var original=File.ReadAllBytes(step);
            if(StepBumperPreparation.DeclaredUnit(original)!=StepLengthUnit.Millimetres) throw new Exception("STEP source units changed.");
            try {await service.ImportAsync(step,StepLengthUnit.Metres,.004);throw new Exception("Contradictory units were silently rescaled.");}catch(ArgumentException) { }
            var model=await service.ImportAsync(step,StepLengthUnit.Millimetres,.004);
            if(Math.Abs(model.Bounds.MaximumM.Z-model.Bounds.MinimumM.Z-.04)>3e-7||model.HealingApplied||model.Faces.Length<5||model.Triangles.Select(t=>t.FaceTag).Distinct().Count()!=model.Faces.Length||!File.ReadAllBytes(service.SourcePath(model.SourceSha256)).SequenceEqual(original)) throw new Exception("STEP dimensions, original bytes or CAD-face mapping were lost.");
            var inspectionHash=BumpStudioContracts.Hash(model);if(BumpStudioContracts.Hash(service.LoadInspection(inspectionHash))!=inspectionHash) throw new Exception("Frozen CAD inspection did not round trip.");
            var upper=model.Faces.Where(f=>Math.Abs(f.Bounds.MinimumM.Z-.04)<1e-6&&Math.Abs(f.Bounds.MaximumM.Z-.04)<1e-6).Select(f=>f.Tag).ToArray();
            var lower=model.Faces.Where(f=>Math.Abs(f.Bounds.MinimumM.Z)<1e-6&&Math.Abs(f.Bounds.MaximumM.Z)<1e-6).Select(f=>f.Tag).ToArray();
            if(upper.Length==0||lower.Length==0) throw new Exception("Actual upper/lower CAD faces are missing.");
            var material=BumpStudioContracts.MaterialCatalog().Single(c=>c.Id=="printed-tpu-95") with {PrintRecipe=new(PrintedGeometryRepresentation.ExplicitPrintedGeometry,"Unspecified 95A filament; calibration pending",new(0,0,1),.0002,.0004,.00045,3,.00135,4,4,"Explicitly modeled CAD internal walls",100,"Processing not supplied","Conditioning not supplied")};
            var frame=AssemblyAuthoring.IdentityPlacement.Frame;var recipe=new BumperJobRecipe("Synthetic STEP preparation",inspectionHash,frame,.04,.02,
                [new("lower",[..lower],[],"lower",0,false),new("upper",[..upper],[],"upper",0,false)],
                [new("lower",RigidBumperFixtureKind.AnnularPlane,frame,0,.03,0,false),new("upper",RigidBumperFixtureKind.AnnularPlane,frame with {OriginM=new(0,0,.04)},0,.03,0,true)],material,new(.003,2,true));
            var hash=service.SaveRecipe(recipe);if(service.LoadRecipe(hash).Material.PrintRecipe!=material.PrintRecipe) throw new Exception("Job recipe/print settings did not round trip.");
            var evidenceFile=Path.Combine(folder,"synthetic-evidence.txt");File.WriteAllText(evidenceFile,"Synthetic evidence-retention fixture; no material calibration claim.");var evidenceSha=service.PreserveMaterialEvidence(evidenceFile);
            var withEvidence=recipe with {Material=material with {EvidenceSha256=material.EvidenceSha256.Add(evidenceSha)}};var evidenceRecipeHash=service.SaveRecipe(withEvidence);File.WriteAllText(service.MaterialEvidencePath(evidenceSha),"tampered");
            try {service.LoadRecipe(evidenceRecipeHash);throw new Exception("Modified material evidence was accepted.");}catch(InvalidDataException) { }
            void Reject(Action action) {try {action();}catch(ArgumentException) {return;}catch(InvalidOperationException) {return;}throw new Exception("Invalid preparation recipe was accepted.");}
            Reject(()=>service.SaveRecipe(recipe with {Contacts=recipe.Contacts.SetItem(0,recipe.Contacts[0] with {SlaveFaceTags=[int.MaxValue]})}));
            Reject(()=>service.SaveRecipe(recipe with {Material=material with {Readiness=MaterialReadiness.Calibrated}}));
            Reject(()=>BumpStudioContracts.RequireVerifiedWorker(recipe,model,new HashSet<string>(),false));Reject(()=>BumpStudioContracts.RequireVerifiedWorker(recipe,model,new HashSet<string>(),true));
            if(hash==BumpStudioContracts.Hash(recipe with {Fixtures=recipe.Fixtures.SetItem(0,recipe.Fixtures[0] with {InnerRadiusM=.002})})||hash==BumpStudioContracts.Hash(recipe with {CompressionFrameInCad=frame with {Axis=new(1,0,0),ReferenceDirection=new(0,1,0)}})) throw new Exception("Fixture/orientation changes did not invalidate the job identity.");
            var output=Path.Combine(repo,"artifacts/bump-studio-preparation");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"inspection.json"),DesignJson.Serialize(model));File.WriteAllText(Path.Combine(output,"recipe.json"),DesignJson.Serialize(recipe));
            File.WriteAllBytes(service.SourcePath(model.SourceSha256),[1,2,3]);try {service.LoadRecipe(hash);throw new Exception("Modified original STEP was accepted.");}catch(InvalidDataException) { }
        }
        finally {if(Directory.Exists(folder)) Directory.Delete(folder,true);}
    }
}
