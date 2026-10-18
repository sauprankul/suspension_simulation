using SuspensionSimulation.Components;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;
using SuspensionSimulation.Solvers.HeadToHead;

namespace SuspensionSimulation.Tests;

internal static class ImportedBumperIntegrationChecks
{
    internal static void Verify()=>VerifyAsync().GetAwaiter().GetResult();
    private static async Task VerifyAsync()
    {
        var repository=AppContext.BaseDirectory;while(!Directory.Exists(Path.Combine(repository,"data"))) repository=Directory.GetParent(repository)?.FullName??throw new Exception("Repository missing.");
        var folder=Path.Combine(Path.GetTempPath(),"bumper-integration-"+Guid.NewGuid().ToString("N"));
        try
        {
            var library=new DesignAssetLibrary(Path.Combine(folder,"design"));var garage=new GarageLibrary(Path.Combine(folder,"garage"));
            new ReferenceGarageCatalog(Path.Combine(repository,"data/reference/stock-86-brz/reference-sheet.json")).Seed(garage);new PhysicalGarageCatalog(library,repository).Migrate(garage);
            var stock=garage.Load(ReferenceGarageCatalog.Gr86Id)!;var engine=new InstalledAnalysisEngine(library);
            var baseline=await engine.SolveAsync(1,stock,stock,0,0,0);var length=baseline.A.State.Mechanics.Elements.Single(e=>e.Path=="FL-damper").LengthM;
            var evidence=new ImportedBumpCurveEvidence("compression_mm,force_n\n0,0\n5,400\n10,1400\n20,4200\n40,9000\n",CurveDisplacementUnit.Millimetres,CurveForceUnit.Newtons,CurveZero.Contact,length+.005,
                "Synthetic whole-part axial compression on the declared strut-eye reference datum; no physical correlation","Generated integration test",DataProvenance.Synthetic);
            var bumper=library.Publish(ElasticComponentFactory.ImportedBumper(new("integration-bumper","draft","Synthetic imported integration bumper",new(DataProvenance.Synthetic,evidence.SourceNote,null)),evidence),null);
            var sourceRoot=library.LoadAssembly(stock.Configuration!.RootAssembly.AssetId)!;
            var root=sourceRoot with {Identity=sourceRoot.Identity with {AssetId="bumper-integration-vehicle",RevisionId="draft",Name="Synthetic GR86 with imported FL bumper"}};
            var first=new InstancePort("front","left-shock-upper");var second=new InstancePort("front","left-shock-lower");
            var configuration=stock.Configuration.ParameterValues??[];
            root=BumperInstallation.Add(root,library,new(bumper.Identity.AssetId),"FL-bumper",first,second,true,configuration);root=library.Publish(root,null,configuration);
            var graph=new AssemblyGraphResolver(library).Compile(new(root.Identity.AssetId),[],configuration);var element=graph.ElasticElements.Single(e=>e.Path=="FL-bumper");
            if(element.First.BodyPath!=graph.Interfaces["front:left-shock-upper"].BodyPath||element.Second.BodyPath!=graph.Interfaces["front:left-shock-lower"].BodyPath) throw new Exception("Installed bumper lost its selected physical supports.");
            // A source component may use any local origin/orientation. Installing it may not rewrite that geometry.
            DesignVector Rotate(DesignVector value)=>new(value.Z,value.X,value.Y);var shift=new DesignVector(.012,-.034,.056);
            var rotated=library.Publish(bumper with {Identity=bumper.Identity with {AssetId="rotated-integration-bumper",RevisionId="draft"},
                Points=[..bumper.Points.Select(p=>p with {PositionM=Rotate(p.PositionM)+shift})],
                Frames=[..bumper.Frames.Select(f=>f with {Frame=new(Rotate(f.Frame.OriginM)+shift,Rotate(f.Frame.Axis),Rotate(f.Frame.ReferenceDirection))})]},null);
            var rotatedRoot=BumperInstallation.Add(sourceRoot,library,new(rotated.Identity.AssetId),"rotated-bumper",first,second,false,configuration);
            var placed=DesignTransform.From(rotatedRoot.Instances.Single(i=>i.Id=="rotated-bumper").Placement);
            var localFirst=rotated.Frames.Single(f=>f.Id=="first").Frame.OriginM;var localSecond=rotated.Frames.Single(f=>f.Id=="second").Frame.OriginM;
            if((placed.Point(localFirst)-element.First.Frame.OriginM).Length>1e-10||(placed.Direction(localSecond-localFirst).Cross(element.Second.Frame.OriginM-element.First.Frame.OriginM)).Length>1e-10||library.LoadComponent(rotated.Identity.AssetId)!.Points[0].PositionM!=shift)
                throw new Exception("Bumper installation changed arbitrary source-frame geometry or misaligned its axial reference.");
            try {BumperInstallation.Add(root,library,new(bumper.Identity.AssetId),"another",first,first,false,configuration);throw new Exception("Same-support bumper was installed.");}catch(ArgumentException) { }
            try {BumperInstallation.Add(root,library,new(bumper.Identity.AssetId),"FL-bumper",first,second,false,configuration);throw new Exception("Duplicate bumper instance was installed.");}catch(ArgumentException) { }
            var custom=garage.Duplicate(stock);custom=garage.Save(custom with {Name="Synthetic saved imported bumper",Configuration=custom.Configuration! with {RootAssembly=new(root.Identity.AssetId)}});
            custom=garage.Load(custom.Id)!;var snapshot=await engine.SolveAsync(2,custom,custom,0,0,0);
            var firstState=snapshot.A.State;var bumperState=firstState.Mechanics.Elements.Single(e=>e.Path=="FL-bumper");
            if(bumperState.ForceOrTorque<=0||Math.Abs(firstState.Chassis.HeaveM-baseline.A.State.Chassis.HeaveM)<1e-5||Math.Abs(firstState.Wheels[0].CamberRad-baseline.A.State.Wheels[0].CamberRad)<1e-6) throw new Exception("Saved imported bumper did not affect actual equilibrium and alignment.");
            var revisedEvidence=evidence with {CsvUtf8="compression_mm,force_n\n0,0\n5,800\n10,2800\n20,8400\n40,18000\n"};
            var revised=library.Publish(bumper with {Behavior=ImportedBumpCurve.Behavior(revisedEvidence),BumpCurveEvidence=revisedEvidence},bumper.Identity.RevisionId);
            if(engine.SourcesMatch(snapshot.A,custom)) throw new Exception("Revised imported curve did not invalidate its saved result.");
            var updated=await engine.SolveAsync(3,custom,custom,0,0,0,previous:snapshot);
            if(updated.A.Source.Components.Single(c=>c.Identity.AssetId==bumper.Identity.AssetId).Identity.RevisionId!=revised.Identity.RevisionId||updated.A.SetupSha256==snapshot.A.SetupSha256||Math.Abs(updated.A.State.Wheels[0].CamberRad-firstState.Wheels[0].CamberRad)<1e-6) throw new Exception("Published bumper revision did not propagate through frozen production analysis.");
            var frozen=DesignJson.Deserialize<InstalledAnalysisSnapshot>(DesignJson.Serialize(snapshot));
            var replayGraph=GarageSetupResolver.Freeze(AssemblyGraphResolver.OpenBundle(frozen.A.Source),frozen.A.Entry.Configuration!);
            var replay=new InstalledEquilibrium(replayGraph).Solve(frozen.A.State.Input,frozen.A.State);
            if(Math.Abs(replay.Chassis.HeaveM-firstState.Chassis.HeaveM)>1e-8||replayGraph.Components.Single(c=>c.Identity.AssetId==bumper.Identity.AssetId).BumpCurveEvidence!.CsvUtf8!=evidence.CsvUtf8) throw new Exception("Exported result did not replay the original exact curve after source revision.");
            var incompatible=library.Publish(revised with {Ports=revised.Ports.SetItem(0,revised.Ports[0] with {Id="renamed-first"}),Behavior=ImportedBumpCurve.Behavior(revisedEvidence,"renamed-first")},revised.Identity.RevisionId);
            try {await engine.SolveAsync(4,custom,custom,0,0,0,previous:updated);throw new Exception("Changed bumper attachment interface was accepted.");}catch(InvalidOperationException error) when(error.Message.Contains("Unresolved port",StringComparison.Ordinal)) { }
            var outOfRange=revisedEvidence with {ContactSeparationM=1,CsvUtf8="compression_mm,force_n\n0,0\n1,100\n"};
            var invalid=library.Publish(revised with {Behavior=ImportedBumpCurve.Behavior(outOfRange),BumpCurveEvidence=outOfRange},incompatible.Identity.RevisionId);
            try {await engine.SolveAsync(5,custom,custom,0,0,0,previous:updated);throw new Exception("Out-of-range bumper was extrapolated into a result.");}catch(ArgumentOutOfRangeException) { }
            library.Publish(revised,invalid.Identity.RevisionId);var recovered=await engine.SolveAsync(6,custom,custom,0,0,0,previous:updated);
            if(recovered.A.SetupSha256!=updated.A.SetupSha256||snapshot.A.State!=firstState) throw new Exception("Source failure recovery altered a frozen accepted result.");
            var output=Path.Combine(repository,"artifacts/imported-bumper-integration");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"frozen-original.json"),DesignJson.Serialize(snapshot));File.WriteAllText(Path.Combine(output,"frozen-revised.json"),DesignJson.Serialize(updated));
        }
        finally {if(Directory.Exists(folder)) Directory.Delete(folder,true);}
    }
}
