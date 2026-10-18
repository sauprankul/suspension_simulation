using SuspensionSimulation.Design;

namespace SuspensionSimulation.Tests;

internal static class FeaDiagnosticChecks
{
    internal static void Verify()
    {
        VerifyLiveStatusReplacement();var result=Fixture();FeaDiagnosticResults.Validate(result);var state=FeaDiagnosticResults.Evaluate(result,.0075);
        if(Math.Abs(state.ForceN-150)>1e-8||Math.Abs(state.TangentNPerM!.Value+20000)>1e-8||Math.Abs(state.WorkFromFirstSampleJ-.9375)>1e-10) throw new Exception("Signed force/tangent or work integration changed the retained diagnostic curve.");
        void Reject(FeaDiagnosticResult input) {try {FeaDiagnosticResults.Validate(input);}catch(ArgumentException) {return;}throw new Exception("Invalid diagnostic was accepted.");}
        Reject(result with {Schema="other"});Reject(result with {InputFileSha256="missing"});Reject(result with {Nodes=result.Nodes.SetItem(0,result.Nodes[0] with {Id=2})});
        Reject(result with {Triangles=result.Triangles.SetItem(0,result.Triangles[0] with {A=999})});Reject(result with {Triangles=result.Triangles.Add(result.Triangles[0])});
        Reject(result with {Frames=result.Frames.SetItem(1,result.Frames[1] with {DisplacementsM=[]})});Reject(result with {Frames=result.Frames.SetItem(1,result.Frames[1] with {ForceN=999})});
        Reject(result with {Frames=result.Frames.SetItem(1,result.Frames[1] with {DisplacementsM=result.Frames[1].DisplacementsM.SetItem(0,new(double.NaN,0,0))})});
        Reject(result with {Complete=false,CurveAccepted=true});Reject(result with {Complete=true,RequestedCompressionM=.02});Reject(result with {Curve=result.Curve.SetItem(1,result.Curve[1] with {CompressionM=0})});
        try {FeaDiagnosticResults.Evaluate(result,.02);throw new Exception("Diagnostic curve was extrapolated.");}catch(ArgumentOutOfRangeException) { }
        var folder=Path.Combine(Path.GetTempPath(),"fea-diagnostic-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var file=Path.Combine(folder,"result.json");File.WriteAllText(file,DesignJson.Serialize(result));var loaded=FeaDiagnosticResults.Load(file);
            if(loaded.Result.Frames.Length!=3||loaded.Result.Nodes[3].Id!=4||!BumpStudioContracts.IsSha(loaded.FileSha256)) throw new Exception("Diagnostic file lost exact node ordering/frames/identity.");
            File.WriteAllText(file,DesignJson.Serialize(result).Replace("\"complete\": true","\"complete\": true, \"complete\": false",StringComparison.Ordinal));
            try {FeaDiagnosticResults.Load(file);throw new Exception("Duplicate diagnostic property was accepted.");}catch(InvalidDataException) { }
        }
        finally {Directory.Delete(folder,true);}
    }
    private static void VerifyLiveStatusReplacement()
    {
        var folder=Path.Combine(Path.GetTempPath(),"fea-live-status-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var path=Path.Combine(folder,"current-job.json");var next=path+".tmp";File.WriteAllText(path,"{\"sequence\":1}");
            var replaced=false;using(var reader=SuspensionSimulation.App.LiveStatusSnapshot.OpenRead(path))
            {
                File.WriteAllText(next,"{\"sequence\":2}");
                // Windows may still deny MoveFileEx while a delete-shared handle is open.
                // The writer retries that race; the short reader must retain a coherent
                // old snapshot and release its handle so the next replacement can finish.
                try {File.Move(next,path,true);replaced=true;}catch(Exception error) when(error is IOException or UnauthorizedAccessException) { }
                using var retained=System.Text.Json.JsonDocument.Parse(reader);
                if(retained.RootElement.GetProperty("sequence").GetInt32()!=1) throw new Exception("An open status snapshot changed during atomic replacement.");
            }
            if(!replaced) File.Move(next,path,true);
            using(var current=SuspensionSimulation.App.LiveStatusSnapshot.Read(path))
            {
                if(current.RootElement.GetProperty("sequence").GetInt32()!=2) throw new Exception("Status reader did not observe the replacement.");
                using var exclusive=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
            }
            foreach(var invalid in new[]{"{","[]"})
            {
                File.WriteAllText(path,invalid);
                try {using var json=SuspensionSimulation.App.LiveStatusSnapshot.Read(path);throw new Exception("Malformed live status was accepted.");}catch(System.Text.Json.JsonException) { }
                using var exclusive=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
            }
        }
        finally {Directory.Delete(folder,true);}
    }
    internal static FeaDiagnosticResult Fixture()
    {
        var sha=new string('a',64);return new(FeaDiagnosticResults.Schema,"Synthetic schema fixture, not an FEA result",true,true,true,sha,sha,sha,.01,"Synthetic contract test only",
            [new(1,new(0,0,0)),new(2,new(.02,0,0)),new(3,new(0,.02,0)),new(4,new(0,0,.02))],
            [new("body",1,2,3),new("body",1,2,4),new("body",1,3,4),new("body",2,3,4)],[new("body","Synthetic tetrahedron",FeaDiagnosticPartRole.Body)],
            [new(0,0,0,[new(0,0,0),new(0,0,0),new(0,0,0),new(0,0,0)]),new(.5,.005,200,[new(0,0,0),new(0,0,0),new(0,0,0),new(0,0,-.005)]),new(1,.01,100,[new(0,0,0),new(0,0,0),new(0,0,0),new(0,0,-.01)])],
            [new(0,0),new(.005,200),new(.01,100)],["Schema-only test fixture; no backend verification or physical calibration"]);
    }
    internal static void VerifyActualExports()
    {
        Verify();var repo=AppContext.BaseDirectory;while(!Directory.Exists(Path.Combine(repo,"data"))) repo=Directory.GetParent(repo)?.FullName??throw new Exception("Repository missing.");
        foreach(var name in new[]{"rubber-complete","fold-incomplete"})
        {
            var loaded=FeaDiagnosticResults.Load(Path.Combine(repo,"artifacts/fea-result-viewer",name+".json"));
            if(loaded.Result.Complete!=(name=="rubber-complete")||loaded.Result.CurveAccepted!=(name=="rubber-complete")||!loaded.Result.Synthetic||loaded.Result.Parts.All(p=>p.Role!=FeaDiagnosticPartRole.Fixture)) throw new Exception("Actual exported completion/provenance/fixture roles changed.");
            Console.WriteLine($"Loaded {name}: {loaded.Result.Nodes.Length} nodes, {loaded.Result.Triangles.Length} triangles, {loaded.Result.Frames.Length} converged frames, SHA {loaded.FileSha256}.");
        }
    }
}
