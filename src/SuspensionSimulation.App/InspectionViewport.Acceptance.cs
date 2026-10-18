using System.Windows;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX;

namespace SuspensionSimulation.App;

internal sealed partial class InspectionViewport
{
    internal (bool Pending,int Fits,int PointCount,int Outside) SceneFramingForProof()
    {
        var positions=_scene?.Points.Select(p=>p.PositionM).ToList()??[];
        if(_scene is {} scene) foreach(var segment in scene.Segments) {positions.Add(segment.StartM);positions.Add(segment.EndM);}
        var outside=positions.Select(p=>Viewport.Project(new Point3D(p.X,p.Y,p.Z))).Count(p=>!double.IsFinite(p.X)||!double.IsFinite(p.Y)||p.X< -1||p.Y< -1||p.X>Viewport.ActualWidth+1||p.Y>Viewport.ActualHeight+1);
        return(_fitPending,_completedFits,positions.Count,outside);
    }
    internal Point CubeScreenPoint(Point p)=>_navigation.PointToScreen(p);
    internal (Point Point,string Id) FindPickTestPoint()
    {
        foreach(var point in _scene!.Segments.Where(x=>x.Layer is "front" or "rear"))
        {
            var p=(point.StartM+point.EndM)/2;var screen=Viewport.Project(new Point3D(p.X,p.Y,p.Z));
            if(screen.X<5||screen.X>Viewport.ActualWidth-5||screen.Y<5||screen.Y>Viewport.ActualHeight-5) continue;
            var hits=Viewport.FindHits(screen);
            var id=hits.FirstOrDefault()?.ModelHit is Element3D element?element.Tag as string:null;
            if(id is not null && !id.StartsWith("ground")) return (Viewport.PointToScreen(screen),id.Replace(".marker","",StringComparison.Ordinal));
        }
        throw new InvalidOperationException("No visible component could be picked.");
    }
}
