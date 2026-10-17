using System.Numerics;
using System.Windows;
using System.Windows.Media;
using HelixToolkit.SharpDX;

namespace SuspensionSimulation.App;

internal sealed partial class InspectionViewport
{
    private string? _labelLayoutSignature;
    private void UpdateLabelLayout()
    {
        if(!_visible.Contains("labels") || Viewport.Camera is not HelixToolkit.Wpf.SharpDX.OrthographicCamera camera || Viewport.ActualWidth<1 || Viewport.ActualHeight<1) return;
        var signature=$"{camera.Position}|{camera.LookDirection}|{camera.UpDirection}|{camera.Width}|{Viewport.ActualWidth}|{Viewport.ActualHeight}";
        if(signature==_labelLayoutSignature) return;
        var frame=ReadCamera();var forward=Vector3.Normalize(frame.Target-frame.Position);
        var right=Vector3.Normalize(Vector3.Cross(forward,frame.Up));var up=Vector3.Normalize(Vector3.Cross(right,forward));
        var width=Viewport.ActualWidth;var height=Viewport.ActualHeight;var scale=camera.Width/width;
        Point Project(Vector3 p)=>new(width/2+Vector3.Dot(p-frame.Target,right)/scale,height/2-Vector3.Dot(p-frame.Target,up)/scale);
        // Equal screen cells keep white text rectangles separate, including at minimum size.
        // Greedy nearest-cell assignment preserves a short leader back to each physical part.
        var columns=_scene?.Validity=="converged-installed-equilibrium"?Math.Max(1,(int)Math.Ceiling(width/100)):Math.Max(1,(int)(width/110));var rows=Math.Max(1,(int)(height/18));
        var cells=(from x in Enumerable.Range(0,columns) from y in Enumerable.Range(0,rows)
            select new Point((x+.5)*width/columns,(y+.5)*height/rows)).ToList();
        foreach(var (id,entry) in _labels.Where(x=>_visible.Contains(x.Value.Layer)).OrderBy(x=>Project(x.Value.Anchor).Y).ThenBy(x=>x.Key))
        {
            var anchor=Project(entry.Anchor);
            var cell=cells.Count==0?anchor:cells.MinBy(p=>(p-anchor).LengthSquared);
            cells.Remove(cell);
            // Keep all labels on a plane in front of the model, so parts and other
            // leaders cannot draw through the white text background.
            var position=frame.Position+forward*.5f+right*(float)((cell.X-width/2)*scale)-up*(float)((cell.Y-height/2)*scale);
            // Orthographic world units per pixel give a stable 12-pixel font through
            // zoom and resize, independent of the renderer's fixed-size shader path.
            if(entry.Model.Geometry is BillboardSingleText3D text)
            {
                if(text.IsInitialized && text.BillboardVertices.Count==1 && text.TextInfo is {} info && info.Text==entry.Caption)
                {
                    var vertex=text.BillboardVertices[0];var ratio=(float)scale/info.Scale;
                    vertex.Position=new(position,1);vertex.OffTL*=ratio;vertex.OffTR*=ratio;vertex.OffBL*=ratio;vertex.OffBR*=ratio;
                    text.BillboardVertices[0]=vertex;info.Origin=position;info.Scale=(float)scale;text.UpdateBounds();text.UpdateVertices();
                }
                else text.TextInfo=new TextInfo(entry.Caption,position){Scale=(float)scale};
            }
            SetLine("label-leader."+id,"labels",entry.Anchor,position+forward*.01f,Colors.SlateGray);
            _models["label-leader."+id].Model.IsHitTestVisible=false;_models["label-leader."+id].Model.Thickness=.7;
        }
        _labelLayoutSignature=signature;ApplyLabelLeaderVisibility();Viewport.InvalidateRender();
    }
    private void ApplyLabelLeaderVisibility()
    {
        foreach(var (id,entry) in _labels)
            if(_models.TryGetValue("label-leader."+id,out var line)) line.Model.IsRendering=_visible.Contains("labels") && _visible.Contains(entry.Layer);
    }
}
