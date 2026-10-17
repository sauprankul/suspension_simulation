using System.Windows;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX;

namespace SuspensionSimulation.App;

internal sealed partial class InspectionViewport
{
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
