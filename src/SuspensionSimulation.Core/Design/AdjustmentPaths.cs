using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace SuspensionSimulation.Design;

public enum PathOrientation { HoldStart, FollowPath }
[JsonPolymorphic(TypeDiscriminatorPropertyName="kind")]
[JsonDerivedType(typeof(FixedAdjustmentPath),"fixed")]
[JsonDerivedType(typeof(DiscreteAdjustmentPath),"discrete")]
[JsonDerivedType(typeof(StraightAdjustmentPath),"straight")]
[JsonDerivedType(typeof(ArcAdjustmentPath),"arc")]
[JsonDerivedType(typeof(MeasuredAdjustmentPath),"measured")]
[JsonDerivedType(typeof(ThreadedAdjustmentPath),"threaded")]
[JsonDerivedType(typeof(EccentricAdjustmentPath),"eccentric")]
public abstract record AdjustmentPath;
public sealed record FixedAdjustmentPath(DesignFrame Frame):AdjustmentPath;
public sealed record AdjustmentHole(string Id,string Name,DesignFrame Frame);
public sealed record DiscreteAdjustmentPath(ImmutableArray<AdjustmentHole> Choices):AdjustmentPath;
public sealed record StraightAdjustmentPath(DesignFrame Start,DesignFrame End,PathOrientation Orientation):AdjustmentPath;
public sealed record ArcAdjustmentPath(DesignVector CenterM,DesignVector UnitAxis,DesignFrame Start,double SweepRad,PathOrientation Orientation):AdjustmentPath;
public sealed record MeasuredAdjustmentPath(ImmutableArray<DesignFrame> Stations,PathOrientation Orientation):AdjustmentPath;
public sealed record ThreadedAdjustmentPath(DesignFrame Datum,double MinimumLengthM,double NominalLengthM,double MaximumLengthM,double? PitchMPerTurn=null):AdjustmentPath;
public sealed record EccentricAdjustmentPath(DesignVector CenterM,DesignVector UnitAxis,DesignFrame Zero,double MinimumAngleRad,double MaximumAngleRad,PathOrientation Orientation):AdjustmentPath;
public sealed record AdjustmentSetting(double? Fraction=null,string? ChoiceId=null);
public sealed record GeometryAdjustment(string Id,string Name,AdjustmentPath Path,AdjustmentSetting Default,
    ImmutableArray<string> PointIds,ImmutableArray<string> FrameIds,string Description);
public sealed record ResolvedAdjustment(string InstancePath,string AdjustmentId,AdjustmentSetting Selection,DesignFrame Frame);

public static class AdjustmentPaths
{
    public static DesignFrame Evaluate(AdjustmentPath path,AdjustmentSetting setting)
    {
        if(path is DiscreteAdjustmentPath choices)
        {if(setting.Fraction is not null||setting.ChoiceId is null) throw new ArgumentException("Select an actual mounting-hole ID.");return choices.Choices.SingleOrDefault(x=>x.Id==setting.ChoiceId)?.Frame??throw new ArgumentException("Unknown mounting hole.");}
        if(setting.ChoiceId is not null) throw new ArgumentException("A continuous adjustment cannot select a hole ID.");
        var t=setting.Fraction??0;if(!double.IsFinite(t)||t<0||t>1) throw new ArgumentException("Adjustment fraction must be between zero and one.");
        DesignFrame Arc(DesignVector center,DesignVector axis,DesignFrame start,double angle,PathOrientation orientation)=>new(center+DesignTransform.Rotate(start.OriginM-center,axis*angle),orientation==PathOrientation.FollowPath?DesignTransform.Rotate(start.Axis,axis*angle):start.Axis,orientation==PathOrientation.FollowPath?DesignTransform.Rotate(start.ReferenceDirection,axis*angle):start.ReferenceDirection);
        return path switch
        {
            FixedAdjustmentPath fixedPath=>t==0?fixedPath.Frame:throw new ArgumentException("A fixed mount has no adjustable position."),
            StraightAdjustmentPath line=>Interpolate(line.Start,line.End,t,line.Orientation),
            ArcAdjustmentPath arc=>Arc(arc.CenterM,arc.UnitAxis,arc.Start,t*arc.SweepRad,arc.Orientation),
            EccentricAdjustmentPath e=>Arc(e.CenterM,e.UnitAxis,e.Zero,e.MinimumAngleRad+t*(e.MaximumAngleRad-e.MinimumAngleRad),e.Orientation),
            ThreadedAdjustmentPath thread=>thread.Datum with {OriginM=thread.Datum.OriginM+thread.Datum.Axis*(thread.MinimumLengthM+t*(thread.MaximumLengthM-thread.MinimumLengthM)-thread.NominalLengthM)},
            MeasuredAdjustmentPath measured=>AlongMeasured(measured,t),
            _=>throw new ArgumentException("Unsupported adjustment path.")
        };
    }
    private static DesignFrame Interpolate(DesignFrame a,DesignFrame b,double t,PathOrientation orientation)
    {var rotation=DesignTransform.RotationError(a,b)*t;return new(a.OriginM+(b.OriginM-a.OriginM)*t,orientation==PathOrientation.FollowPath?DesignTransform.Rotate(a.Axis,rotation):a.Axis,orientation==PathOrientation.FollowPath?DesignTransform.Rotate(a.ReferenceDirection,rotation):a.ReferenceDirection);}
    private static DesignFrame AlongMeasured(MeasuredAdjustmentPath path,double t)
    {
        var lengths=path.Stations.Skip(1).Select((f,i)=>(f.OriginM-path.Stations[i].OriginM).Length).ToArray();var target=t*lengths.Sum();
        for(var i=0;i<lengths.Length;i++) {if(target<=lengths[i]||i==lengths.Length-1) {var frame=Interpolate(path.Stations[i],path.Stations[i+1],target/lengths[i],path.Orientation);return path.Orientation==PathOrientation.HoldStart?frame with {Axis=path.Stations[0].Axis,ReferenceDirection=path.Stations[0].ReferenceDirection}:frame;}target-=lengths[i];}
        throw new ArgumentException("A measured path needs at least two stations.");
    }
    public static void Validate(ComponentDefinition component)
    {
        var ids=new HashSet<string>();var targets=new HashSet<string>();
        void Require(bool test,string message) {if(!test) throw new ArgumentException(message);}
        void Axis(DesignVector v)=>Require(v.IsFinite&&Math.Abs(v.Length-1)<1e-9,"Adjustment axis must be a unit vector.");
        foreach(var adjustment in component.Adjustments??[])
        {
            Require(!string.IsNullOrWhiteSpace(adjustment.Id)&&ids.Add(adjustment.Id)&&!string.IsNullOrWhiteSpace(adjustment.Name)&&!string.IsNullOrWhiteSpace(adjustment.Description),"Adjustment IDs, names and descriptions must be unique and present.");
            foreach(var id in adjustment.PointIds) Require(component.Points.Any(p=>p.Id==id)&&targets.Add("point:"+id),"Adjustment point is missing or controlled twice.");
            foreach(var id in adjustment.FrameIds) Require(component.Frames.Any(p=>p.Id==id)&&targets.Add("frame:"+id),"Adjustment frame is missing or controlled twice.");
            void Arc(DesignVector c,DesignVector axis,DesignFrame start,double sweep,PathOrientation orientation)
            {Axis(axis);DesignValidation.Frame(start);Require(c.IsFinite&&Math.Abs((start.OriginM-c).Dot(axis))<1e-9&&(start.OriginM-c).Length>1e-9,"Arc needs a nonzero radius in its declared plane.");Require(double.IsFinite(sweep)&&Math.Abs(sweep)>1e-9&&Math.Abs(sweep)<=2*Math.PI&&Enum.IsDefined(orientation),"Invalid arc travel.");}
            switch(adjustment.Path)
            {
                case FixedAdjustmentPath f:DesignValidation.Frame(f.Frame);break;
                case DiscreteAdjustmentPath d:
                    Require(d.Choices.Length>0&&d.Choices.Select(x=>x.Id).Distinct().Count()==d.Choices.Length,"Mounting-hole IDs must be unique.");foreach(var choice in d.Choices) {Require(!string.IsNullOrWhiteSpace(choice.Id)&&!string.IsNullOrWhiteSpace(choice.Name),"A mounting hole needs an ID and name.");DesignValidation.Frame(choice.Frame);}break;
                case StraightAdjustmentPath line:DesignValidation.Frame(line.Start);DesignValidation.Frame(line.End);Require((line.End.OriginM-line.Start.OriginM).Length>1e-9&&Enum.IsDefined(line.Orientation),"Slot endpoints must differ.");break;
                case ArcAdjustmentPath a:Arc(a.CenterM,a.UnitAxis,a.Start,a.SweepRad,a.Orientation);break;
                case EccentricAdjustmentPath e:Arc(e.CenterM,e.UnitAxis,e.Zero,e.MaximumAngleRad-e.MinimumAngleRad,e.Orientation);Require(double.IsFinite(e.MinimumAngleRad)&&double.IsFinite(e.MaximumAngleRad)&&e.MaximumAngleRad>e.MinimumAngleRad,"Invalid eccentric angle range.");break;
                case ThreadedAdjustmentPath thread:
                    DesignValidation.Frame(thread.Datum);Require(double.IsFinite(thread.MinimumLengthM)&&double.IsFinite(thread.MaximumLengthM)&&double.IsFinite(thread.NominalLengthM)&&thread.MinimumLengthM>=0&&thread.MaximumLengthM>thread.MinimumLengthM&&thread.NominalLengthM>=thread.MinimumLengthM&&thread.NominalLengthM<=thread.MaximumLengthM,"Invalid threaded range.");if(thread.PitchMPerTurn is {} pitch) Require(double.IsFinite(pitch)&&pitch>0,"Thread pitch must be positive.");break;
                case MeasuredAdjustmentPath m:Require(m.Stations.Length>=2&&Enum.IsDefined(m.Orientation),"A measured path needs at least two stations.");foreach(var frame in m.Stations) DesignValidation.Frame(frame);Require(m.Stations.Skip(1).Select((f,i)=>(f.OriginM-m.Stations[i].OriginM).Length).All(l=>l>1e-9),"Measured path stations must differ.");break;
                default:throw new ArgumentException("Unsupported adjustment path.");
            }
            DesignValidation.Frame(Evaluate(adjustment.Path,adjustment.Default));
        }
    }
    public static ComponentDefinition Freeze(ComponentDefinition source,IReadOnlyDictionary<string,AdjustmentSetting> selected)
    {
        Validate(source);if(selected.Keys.Any(id=>!(source.Adjustments??[]).Any(a=>a.Id==id))) throw new ArgumentException("Unknown adjustment.");
        var points=source.Points;var frames=source.Frames;
        foreach(var adjustment in source.Adjustments??[])
        {
            var a=Evaluate(adjustment.Path,adjustment.Default);var b=Evaluate(adjustment.Path,selected.GetValueOrDefault(adjustment.Id)??adjustment.Default);var rotation=DesignTransform.RotationError(a,b);
            DesignVector P(DesignVector p)=>b.OriginM+DesignTransform.Rotate(p-a.OriginM,rotation);
            points=[..points.Select(p=>adjustment.PointIds.Contains(p.Id)?p with {PositionM=P(p.PositionM)}:p)];
            frames=[..frames.Select(f=>adjustment.FrameIds.Contains(f.Id)?f with {Frame=new(P(f.Frame.OriginM),DesignTransform.Rotate(f.Frame.Axis,rotation),DesignTransform.Rotate(f.Frame.ReferenceDirection,rotation))}:f)];
        }
        // Derived runtime geometry retains the immutable source identity. It must never be republished or hashed as source.
        return source with {Points=points,Frames=frames,Template=null,Dimensions=[..source.VisualEdges.Select(e=>(DesignDimension)new MeasuredDistance(e.Id,e.StartPointId,e.EndPointId))],IsRuntimeDerived=true};
    }
}
