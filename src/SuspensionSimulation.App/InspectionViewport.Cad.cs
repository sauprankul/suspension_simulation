using System.Collections.Immutable;
using System.Numerics;
using System.Windows.Media;
using HelixToolkit.SharpDX;
using HelixToolkit.Geometry;
using HelixToolkit.Wpf.SharpDX;
using SuspensionSimulation.Design;
using SuspensionSimulation.Kinematics;

namespace SuspensionSimulation.App;

internal sealed partial class InspectionViewport
{
    private readonly Dictionary<int,MeshGeometryModel3D> _cadFaces=new();
    private bool _cadLightsAdded;
    public event Action<int>? CadFacePicked;
    internal void SetCadInspection(BumperCadInspection cad,DesignFrame frame,IEnumerable<RigidBumperFixture> fixtures)
    {
        foreach(var face in _cadFaces.Values) Viewport.Items.Remove(face);_cadFaces.Clear();
        var nodes=cad.Nodes.ToDictionary(n=>n.Tag,n=>new Vector3((float)n.PositionM.X,(float)n.PositionM.Y,(float)n.PositionM.Z));
        var points=(from x in new[]{cad.Bounds.MinimumM.X,cad.Bounds.MaximumM.X} from y in new[]{cad.Bounds.MinimumM.Y,cad.Bounds.MaximumM.Y} from z in new[]{cad.Bounds.MinimumM.Z,cad.Bounds.MaximumM.Z} select new InspectionPoint($"cad.bounds.{x}.{y}.{z}","CAD extent","CAD",new((float)x,(float)y,(float)z),SceneSource.Authored)).ToImmutableArray();
        var size=(cad.Bounds.MaximumM-cad.Bounds.MinimumM).Length*.25;var start=V(frame.OriginM);var axis=V(frame.Axis)*((float)size);var reference=V(frame.ReferenceDirection)*((float)size);
        var lines=new List<InspectionSegment> {new("cad.axis","Compression frame +Z (drive toward −Z)","CAD","front",start,start+axis,SceneSource.Authored),new("cad.reference","Compression frame +X","CAD","bars",start,start+reference,SceneSource.Authored)};
        foreach(var fixture in fixtures)
        {
            var f=fixture.FrameInCad;var x=f.ReferenceDirection;var y=f.Axis.Cross(x);var radius=fixture.OuterRadiusM;
            for(var n=0;n<48;n++)
            {DesignVector At(double t,double z)=>f.OriginM+x*(radius*Math.Cos(t))+y*(radius*Math.Sin(t))+f.Axis*z;var t=n*2*Math.PI/48;var u=(n+1)*2*Math.PI/48;
                foreach(var z in fixture.Kind==RigidBumperFixtureKind.Cylinder?new[]{0d,fixture.LengthM}:new[]{0d}) lines.Add(new($"fixture.{fixture.Id}.{n}.{z}",fixture.Id,fixture.Id,"rear",V(At(t,z)),V(At(u,z)),SceneSource.Authored));
                if(fixture.InnerRadiusM>0) {DesignVector Inner(double a)=>f.OriginM+x*(fixture.InnerRadiusM*Math.Cos(a))+y*(fixture.InnerRadiusM*Math.Sin(a));lines.Add(new($"fixture.{fixture.Id}.inner.{n}",fixture.Id,fixture.Id,"rear",V(Inner(t)),V(Inner(u)),SceneSource.Authored));}
                if(fixture.Kind==RigidBumperFixtureKind.Cylinder&&n%12==0) lines.Add(new($"fixture.{fixture.Id}.shaft.{n}",fixture.Id,fixture.Id,"rear",V(At(t,0)),V(At(t,fixture.LengthM)),SceneSource.Authored));
            }
        }
        SetScene(new(cad.SourceFileName,cad.SourceSha256,"cad-inspection-only",points,[..lines],[],["CAD surface inspection and fixture recipe only; no deformation or force solution."]));
        if(!_cadLightsAdded)
        {Viewport.Items.Add(new AmbientLight3D {Color=Color.FromRgb(120,120,120)});Viewport.Items.Add(new DirectionalLight3D {Color=Colors.White,Direction=new(-1,1,-2)});_cadLightsAdded=true;}
        // Individual CAD faces remain independently pickable; tessellation is never mistaken for the future FE volume mesh.
        var triangles=cad.Triangles.ToLookup(t=>t.FaceTag);
        foreach(var face in cad.Faces)
        {
            var builder=new MeshBuilder();foreach(var t in triangles[face.Tag]) builder.AddTriangle(nodes[t.A],nodes[t.B],nodes[t.C]);
            var model=new MeshGeometryModel3D {Geometry=builder.ToMeshGeometry3D(),Material=CadMaterial(false),CullMode=SharpDX.Direct3D11.CullMode.None,Tag="cad.face."+face.Tag};
            _cadFaces.Add(face.Tag,model);Viewport.Items.Add(model);
        }
        // Vertices remain available to Fit without drawing thousands of point markers.
        foreach(var point in points) if(_models.TryGetValue(point.Id+".marker",out var marker)) marker.Model.IsRendering=false;
        _status.Text="STEP solid inspection · actual CAD face IDs · no FEA result";Viewport.InvalidateRender();
        static Vector3 V(DesignVector value)=>new((float)value.X,(float)value.Y,(float)value.Z);
    }
    internal void HighlightCadFaces(IEnumerable<int> selected)
    {
        var tags=selected.ToHashSet();foreach(var (tag,face) in _cadFaces) face.Material=CadMaterial(tags.Contains(tag));
        Viewport.InvalidateRender();
    }
    private static PhongMaterial CadMaterial(bool selected)=>new()
    {DiffuseColor=selected?new(1,.65f,.12f,1):new(.28f,.62f,.78f,1),AmbientColor=new(.35f,.35f,.35f,1),EmissiveColor=selected?new(.3f,.16f,.015f,1):new(.08f,.16f,.21f,1),SpecularColor=new(.3f,.3f,.3f,1),SpecularShininess=25};
    internal void SelectCadFace(int tag)
    {if(!_cadFaces.ContainsKey(tag)) return;HighlightCadFaces([tag]);CadFacePicked?.Invoke(tag);}
    internal int PickCadFaceForProof()
    {
        foreach(var face in _cadFaces.Values)
        {
            if(face.Geometry?.Positions is not {} positions) continue;
            for(var n=0;n+2<positions.Count;n+=3)
            {
                var p=(positions[n]+positions[n+1]+positions[n+2])/3;var screen=Viewport.Project(new System.Windows.Media.Media3D.Point3D(p.X,p.Y,p.Z));
                var hit=Viewport.FindHits(screen).FirstOrDefault();if(hit?.ModelHit is Element3D {Tag:string id}&&id.StartsWith("cad.face.",StringComparison.Ordinal)&&int.TryParse(id[9..],out var tag)) {SelectCadFace(tag);return tag;}
            }
        }
        throw new InvalidOperationException("No actual CAD triangle could be picked in the rendered inspection scene.");
    }
}
