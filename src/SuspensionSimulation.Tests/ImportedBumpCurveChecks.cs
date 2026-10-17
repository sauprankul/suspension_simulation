using SuspensionSimulation.Components;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.Tests;

internal static class ImportedBumpCurveChecks
{
    internal static void Verify()
    {
        var evidence=new ImportedBumpCurveEvidence("\uFEFFcompression_mm,force_kn\r\n0,0\r\n5,.4\r\n10,1.4\r\n20,4.2\r\n40,9\r\n",CurveDisplacementUnit.Millimetres,CurveForceUnit.Kilonewtons,CurveZero.Contact,.53,
            "Synthetic quasi-static numerical example; no part correlation","Generated verification series, not a measured stock bumper",DataProvenance.Synthetic);
        var behavior=ImportedBumpCurve.Behavior(evidence);
        if(behavior.Samples[1].CompressionM!=.005||behavior.Samples[1].ForceN!=400) throw new Exception("Imported curve units changed.");
        var state=ElasticLaws.Bump(behavior,.5225);
        if(Math.Abs(state.ForceN-900)>1e-8||Math.Abs(state.TangentNPerM-200000)>1e-8) throw new Exception("Imported curve interpolation changed its force/tangent.");
        if(ElasticLaws.Bump(behavior,.54).ForceN!=0) throw new Exception("Contact-origin curve supplied tensile force.");
        try {ElasticLaws.Bump(behavior,.48);throw new Exception("Imported curve was extrapolated beyond its valid stroke.");}catch(ArgumentOutOfRangeException) { }
        void Reject(ImportedBumpCurveEvidence input)
        {try {ImportedBumpCurve.Parse(input);}catch(ArgumentException) {return;}throw new Exception("Invalid imported curve was accepted.");}
        Reject(evidence with {CsvUtf8="compression,force\n0,0\n0,1"});Reject(evidence with {CsvUtf8="compression,force\n0,0\n1,-1"});Reject(evidence with {CsvUtf8="compression,force\n0,0\n1,2\n2,1"});
        Reject(evidence with {CsvUtf8="compression,force\n0,0\n1,NaN"});Reject(evidence with {DisplacementUnit=CurveDisplacementUnit.Metres});
        if(ImportedBumpCurve.Fingerprint(evidence)==ImportedBumpCurve.Fingerprint(evidence with {Conditions="Different fixture/temperature"})) throw new Exception("Installation conditions did not invalidate the evidence identity.");
        var installed=evidence with {Zero=CurveZero.Installed,CsvUtf8="compression,force\n0,.2\n5,.4"};
        if(ElasticLaws.Bump(ImportedBumpCurve.Behavior(installed),installed.ContactSeparationM).ForceN!=200) throw new Exception("Installed-origin initial reaction was discarded.");
        var folder=Path.Combine(Path.GetTempPath(),"imported-bump-"+Guid.NewGuid().ToString("N"));
        try
        {
            var library=new DesignAssetLibrary(folder);var identity=new RevisionIdentity("imported-bumper","draft","Imported synthetic bumper",new(DataProvenance.Synthetic,"Synthetic force mapping test",null));
            var component=library.Publish(ElasticComponentFactory.ImportedBumper(identity,evidence),null);var reopened=library.LoadComponent(component.Identity.AssetId)!;
            if(reopened.BumpCurveEvidence!.RawSha256!=evidence.RawSha256||reopened.BumpCurveEvidence.CsvUtf8!=evidence.CsvUtf8) throw new Exception("Immutable bumper lost exact raw evidence.");
            try {library.Publish(reopened with {BumpCurveEvidence=evidence with {Conditions="tampered"}},component.Identity.RevisionId);throw new Exception("Mismatched curve evidence was published.");}catch(ArgumentException) { }
            // Place an imported parallel element on the real front strut eyes; no spring-MR substitute.
            var repository=AppContext.BaseDirectory;while(!Directory.Exists(Path.Combine(repository,"data"))) repository=Directory.GetParent(repository)?.FullName??throw new Exception("Repository missing.");
            var models=TwinsInstalledCatalog.Seed(library,repository);var resolver=new AssemblyGraphResolver(library);var source=resolver.Compile(new(models.Gr86.Identity.AssetId));
            var input=new VehicleStaticInput(new(MassAccountingPolicy.AggregateBase,1360.778,new(.0515,0,.46),[]));var baseline=new InstalledEquilibrium(source).Solve(input);
            var length=baseline.Mechanics.Elements.Single(e=>e.Path=="FL-damper").LengthM;
            evidence=evidence with {ContactSeparationM=length+.005};var bumper=library.Publish(ElasticComponentFactory.ImportedBumper(identity,evidence),component.Identity.RevisionId);
            var root=library.Publish(models.Gr86 with {Identity=models.Gr86.Identity with {AssetId="curve-vehicle",RevisionId="draft"},Instances=models.Gr86.Instances.Add(new ComponentInstance("FL-bumper",new(bumper.Identity.AssetId),AssemblyAuthoring.IdentityPlacement)),
                Joints=models.Gr86.Joints.Add(new("bumper-upper",JointKind.Spherical,new("FL-bumper","first"),new("front","left-shock-upper"))).Add(new("bumper-lower",JointKind.Spherical,new("FL-bumper","second"),new("front","left-shock-lower")))},null);
            var withBumper=resolver.Compile(new(root.Identity.AssetId));var loaded=new InstalledEquilibrium(withBumper).Solve(input,baseline);
            if(loaded.Mechanics.Elements.Single(e=>e.Path=="FL-bumper").ForceOrTorque<=0||loaded.Mechanics.Elements.Single(e=>e.Path=="FL-damper").LengthM<=length+.0001||Math.Abs(loaded.Wheels[0].CamberRad-baseline.Wheels[0].CamberRad)<1e-6||loaded.MomentResidualNm.Length>.05) throw new Exception("Imported bumper did not alter engagement, installed geometry and equilibrium through its actual mounts.");
            var portable=AssemblyGraphResolver.Export(withBumper);var frozen=AssemblyGraphResolver.OpenBundle(DesignJson.Deserialize<PortableDesignBundle>(DesignJson.Serialize(portable)));
            if(frozen.Components.Single(c=>c.Identity.AssetId==bumper.Identity.AssetId).BumpCurveEvidence!.RawSha256!=evidence.RawSha256) throw new Exception("Portable assembly omitted imported curve evidence.");
        }
        finally {if(Directory.Exists(folder)) Directory.Delete(folder,true);}
    }
}
