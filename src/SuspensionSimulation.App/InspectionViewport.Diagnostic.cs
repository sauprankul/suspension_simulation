using System.Numerics;
using System.Windows.Media;
using HelixToolkit.Geometry;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using SuspensionSimulation.Design;
using SuspensionSimulation.Kinematics;

namespace SuspensionSimulation.App;

internal sealed partial class InspectionViewport
{
    private readonly Dictionary<string,MeshGeometryModel3D> _diagnosticMeshes=new(StringComparer.Ordinal);
    private FeaDiagnosticResult? _diagnosticResult;private int _diagnosticFrame;
    private Dictionary<int,int> _diagnosticNodeIndices=new();
    private Dictionary<string,FeaDiagnosticTriangle[]> _diagnosticTriangles=new(StringComparer.Ordinal);
    private bool _diagnosticFixtureVisible=true,_diagnosticSelectionHook;
    internal void SetDiagnosticResult(FeaDiagnosticResult result)
    {
        foreach(var model in _diagnosticMeshes.Values) Viewport.Items.Remove(model);_diagnosticMeshes.Clear();
        _diagnosticResult=result;_diagnosticNodeIndices=result.Nodes.Select((n,i)=>(n.Id,i)).ToDictionary(n=>n.Id,n=>n.i);_diagnosticTriangles=result.Triangles.GroupBy(t=>t.PartId).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
        var minimum=new Vector3(float.PositiveInfinity);var maximum=new Vector3(float.NegativeInfinity);
        foreach(var frame in result.Frames) for(var n=0;n<result.Nodes.Length;n++) {var p=result.Nodes[n].PositionM+frame.DisplacementsM[n];var v=new Vector3((float)p.X,(float)p.Y,(float)p.Z);minimum=Vector3.Min(minimum,v);maximum=Vector3.Max(maximum,v);}
        var points=(from x in new[]{minimum.X,maximum.X} from y in new[]{minimum.Y,maximum.Y} from z in new[]{minimum.Z,maximum.Z} select new InspectionPoint($"inspection.bounds.{x}.{y}.{z}.fit-only","Solver mesh envelope","solver-boundary",new(x,y,z),SceneSource.Solved)).ToArray();
        SetScene(new(result.Label,result.PlotFileSha256,"diagnostic-fea-boundary",[..points],[],[],result.Warnings));
        if(!_cadLightsAdded) {Viewport.Items.Add(new AmbientLight3D {Color=Color.FromRgb(120,120,120)});Viewport.Items.Add(new DirectionalLight3D {Color=Colors.White,Direction=new(-1,1,-2)});_cadLightsAdded=true;}
        foreach(var part in result.Parts)
        {
            var body=part.Role==FeaDiagnosticPartRole.Body;var model=new MeshGeometryModel3D {Material=new PhongMaterial {DiffuseColor=body?new(.25f,.65f,.85f,1):new(.72f,.75f,.8f,1),AmbientColor=new(.35f,.35f,.35f,1),EmissiveColor=body?new(.07f,.16f,.21f,1):new(.12f,.12f,.13f,1),SpecularColor=new(.3f,.3f,.3f,1),SpecularShininess=25},CullMode=SharpDX.Direct3D11.CullMode.None,Tag="solver.part:"+part.Id};
            _diagnosticMeshes.Add(part.Id,model);Viewport.Items.Add(model);
        }
        if(!_diagnosticSelectionHook) {_components.SelectionChanged+=(_,_)=>ShowDiagnosticSelection();_diagnosticSelectionHook=true;}
        _components.ItemsSource=result.Parts.Select(p=>new ComponentChoice("solver.part:"+p.Id,$"Solver {p.Role.ToString().ToLowerInvariant()} · {p.Name}")).ToArray();_components.SelectedIndex=0;
        ShowLayer("ground",false);foreach(var check in _layerChecks.Values) check.Visibility=System.Windows.Visibility.Collapsed;SetDiagnosticFrame(0);RequestSceneFit();
    }
    internal void SetDiagnosticFrame(int index)
    {
        var result=_diagnosticResult??throw new InvalidOperationException("Open diagnostic data first.");if(index<0||index>=result.Frames.Length) throw new ArgumentOutOfRangeException(nameof(index));_diagnosticFrame=index;var frame=result.Frames[index];
        Vector3 At(int id) {var n=_diagnosticNodeIndices[id];var p=result.Nodes[n].PositionM+frame.DisplacementsM[n];return new((float)p.X,(float)p.Y,(float)p.Z);}
        foreach(var part in result.Parts)
        {
            var builder=new MeshBuilder();foreach(var triangle in _diagnosticTriangles[part.Id]) builder.AddTriangle(At(triangle.A),At(triangle.B),At(triangle.C));
            var geometry=builder.ToMeshGeometry3D();geometry.IsDynamic=true;_diagnosticMeshes[part.Id].Geometry=geometry;_diagnosticMeshes[part.Id].IsRendering=part.Role==FeaDiagnosticPartRole.Body||_diagnosticFixtureVisible;
        }
        _status.Text=$"Solver boundary · converged frame {index+1}/{result.Frames.Length} · actual displacement ×1 · {(result.Complete?"complete run":"INCOMPLETE RUN")} · {(result.Synthetic?"synthetic material/geometry":"source classification as exported")}";
        _technical.Text=$"Read-only diagnostic geometry; these are solver boundary surfaces, not CAD face tags.\n{result.Conditions}\n"+string.Join("\n",result.Warnings);ShowDiagnosticSelection();Viewport.InvalidateRender();
    }
    internal void ShowDiagnosticFixtures(bool visible)
    { _diagnosticFixtureVisible=visible;if(_diagnosticResult is {} result) foreach(var part in result.Parts.Where(p=>p.Role==FeaDiagnosticPartRole.Fixture)) _diagnosticMeshes[part.Id].IsRendering=visible;Viewport.InvalidateRender();}
    private void ShowDiagnosticSelection()
    {
        if(_diagnosticResult is not {} result||SelectedId is not {} id||!id.StartsWith("solver.part:",StringComparison.Ordinal)) return;
        var part=result.Parts.SingleOrDefault(p=>p.Id==id[12..]);if(part is null) return;
        _detail.Text=$"Solver {part.Role.ToString().ToLowerInvariant()}: {part.Name}\n{_diagnosticTriangles[part.Id].Length} boundary triangles · converged frame {_diagnosticFrame+1}/{result.Frames.Length} · actual displacement ×1. Solver surfaces are not CAD face identities.";
    }
    internal (int Objects,int Frame,Vector3 FirstBodyVertex) DiagnosticDisplayForProof()
    {
        var part=_diagnosticResult!.Parts.First(p=>p.Role==FeaDiagnosticPartRole.Body);var positions=_diagnosticMeshes[part.Id].Geometry?.Positions;
        return(_diagnosticMeshes.Count,_diagnosticFrame,positions is {Count:>0}?positions[0]:throw new InvalidOperationException("Diagnostic body mesh is absent."));
    }
}
