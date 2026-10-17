using System.Collections.Immutable;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.Solvers.Design;

public sealed record ChassisPose(double HeaveM,double RollRad,double PitchRad)
{
    public DesignVector Rotate(DesignVector p)=>DesignTransform.Rotate(DesignTransform.Rotate(p,new(RollRad,0,0)),new(0,PitchRad,0));
    public DesignVector World(DesignVector p)=>Rotate(p)+new DesignVector(0,0,HeaveM);
}
public sealed record VehicleStaticInput(SetupMass Mass,double LongitudinalG=0,double LateralG=0,double SteeringWheelDegrees=0);
public sealed record LimitReaction(string Id,double ForceN,double GapM);
public sealed record SolvedRideHeight(string Id,string Name,double HeightM);
public sealed record InstalledEquilibriumResult(VehicleStaticInput Input,ChassisPose Chassis,TwinsMotionState Motion,InstalledMechanicalState Mechanics,
    ImmutableArray<WheelAlignment> Wheels,ImmutableArray<double> NormalLoadsN,ImmutableArray<LimitReaction> RigidReactions,
    ImmutableArray<SolvedRideHeight> RideHeights,double TotalMassKg,DesignVector CenterOfMassWorldM,DesignVector ForceResidualN,DesignVector MomentResidualNm,
    double MaximumNormalizedResidual,int Iterations,int PoseEvaluations)
{
    public FrozenResultManifest? Manifest {get;init;}
    public ImmutableArray<ResolvedAdjustment> Adjustments {get;init;}=[];
    public string InputSha256 {get;init;}="";
}

/// <summary>One instance per job. Installed-force equilibrium with unilateral tire contact and exact rigid limits.
/// Tangential contact forces use the commanded common G ratio times normal load; tire friction capacity is not modeled.
/// No target alignment/height. Mass policy and tire contact idealization are explicit inputs.</summary>
public sealed class InstalledEquilibrium
{
    public const double Gravity=9.80665;
    private const double LengthScale=.05,CoordinateScale=.05;
    private readonly ResolvedAssemblyGraph _graph;
    private readonly TwinsKinematics _kinematics;
    private readonly InstalledMechanics _mechanics;
    private readonly double[] _radii;
    private readonly Dictionary<(double,double,double,double),Geometry> _geometryCache=new();
    private TwinsMotionState? _poseSeed;
    private int _poseEvaluations;
    private sealed record MassPoint(double Kg,DesignVector Point);
    private sealed record Geometry(TwinsMotionState Motion,InstalledMechanicalState Mechanics,MassPoint[] Masses);
    private sealed record Configuration(Geometry Geometry,ChassisPose Chassis,DesignVector[] Patch,MassPoint[] Masses,double Potential,double[] Gaps,double[] ReactionPotentials);
    private sealed record Derivatives(Configuration State,double[] Gradient,double[,] GapJacobian,double[,] ReactionJacobian);

    public InstalledEquilibrium(ResolvedAssemblyGraph source)
    {
        _graph=GraphAdjustments.Freeze(source,source.AppliedAdjustments.Select(a=>a.Selection.ChoiceId is {} id?(AdjustmentSelection)new DiscreteAdjustmentSelection(a.InstancePath,a.AdjustmentId,id):new ContinuousAdjustmentSelection(a.InstancePath,a.AdjustmentId,a.Selection.Fraction??0)));
        _kinematics=new(_graph);_mechanics=new(_kinematics.System);
        var vehicle=_graph.Root.Vehicle??throw new ArgumentException("This assembly has no declared wheel contacts or ride-height datums.");
        _radii=new[]{"FL","FR","RL","RR"}.Select(c=>
        {var contact=vehicle.Contacts.Single(p=>p.Corner==c);var body=_graph.Bodies.Single(b=>b.Path==contact.InstancePath);return body.Component.Wheel?.RollingRadiusM??throw new ArgumentException("Wheel contact requires a selected wheel component.");}).ToArray();
    }
    public InstalledEquilibriumResult Solve(VehicleStaticInput input,InstalledEquilibriumResult? seed=null,CancellationToken cancellation=default)
    {
        ValidateMass(input.Mass);if(!double.IsFinite(input.LongitudinalG)||!double.IsFinite(input.LateralG)) throw new ArgumentException("Acceleration must be finite.");
        _geometryCache.Clear();_poseSeed=seed?.Manifest is {} manifest&&manifest.Dependencies.SequenceEqual(_graph.Manifest.Dependencies)&&seed.Adjustments.SequenceEqual(_graph.AppliedAdjustments)?seed.Motion:null;_poseEvaluations=0;
        var initial=seed is null?new double[7]:seed.Motion.Travel.Values.Concat(new[]{seed.Chassis.HeaveM,seed.Chassis.RollRad,seed.Chassis.PitchRad}).Select(v=>v/CoordinateScale).ToArray();
        var x=initial;var first=Evaluate(x,input,cancellation);var totalMass=first.Masses.Sum(m=>m.Kg);var forceScale=totalMass*Gravity;var energyScale=forceScale*LengthScale;
        var active=new List<int>{0,1,2,3};var lambda=new Dictionary<int,double>();foreach(var index in active) lambda[index]=.25;
        if(seed is not null) for(var i=0;i<4;i++) lambda[i]=seed.NormalLoadsN[i]/forceScale;
        double[] Multipliers()=>active.Select(i=>lambda.GetValueOrDefault(i)).ToArray();
        var iterations=0;
        for(;iterations<70;iterations++)
        {
            cancellation.ThrowIfCancellationRequested();var d=Differentiate(x,input,energyScale,cancellation);var multipliers=Multipliers();
            var violation=Enumerable.Range(0,d.State.Gaps.Length).Where(i=>!active.Contains(i)&&d.State.Gaps[i]<-1e-8).OrderBy(i=>d.State.Gaps[i]).FirstOrDefault(-1);
            if(violation>=0) {active.Add(violation);lambda[violation]=0;continue;}
            var residual=Residual(d,active,multipliers,forceScale,energyScale);
            var max=residual.Select(Math.Abs).DefaultIfEmpty(0).Max();
            if(max<1e-7)
            {
                var release=active.Where(i=>lambda[i]<-1e-7).OrderBy(i=>lambda[i]).FirstOrDefault(-1);
                if(release>=0) {active.Remove(release);lambda.Remove(release);continue;}
                return Accepted(input,d.State,active,multipliers,forceScale,max,iterations);
            }
            var hessian=Hessian(x,input,active,multipliers,forceScale,energyScale,cancellation);var n=7+active.Count;var matrix=new double[n,n];
            for(var r=0;r<7;r++) for(var c=0;c<7;c++) matrix[r,c]=hessian[r,c];
            for(var j=0;j<active.Count;j++) for(var i=0;i<7;i++) {matrix[i,7+j]=-d.ReactionJacobian[active[j],i]*forceScale/energyScale;matrix[7+j,i]=d.GapJacobian[active[j],i]/LengthScale;}
            double[] step;
            try {step=SolveLinear(matrix,residual);}catch(InvalidOperationException error) {throw new InvalidOperationException("Installed equilibrium has singular/indeterminate rigid constraints or reactions. "+error.Message,error);}
            var maxCoordinateStep=step.Take(7).Select(Math.Abs).DefaultIfEmpty(0).Max();var alpha=Math.Min(1,2/Math.Max(2,maxCoordinateStep));var oldNorm=residual.Sum(v=>v*v);var accepted=false;var added=false;
            for(;alpha>=1d/256;alpha*=.5)
            {
                cancellation.ThrowIfCancellationRequested();var candidate=x.Select((v,i)=>v-alpha*step[i]).ToArray();
                var candidateLambda=multipliers.Select((v,i)=>v-alpha*step[7+i]).ToArray();
                try
                {
                    var state=Evaluate(candidate,input,cancellation);
                    var hit=Enumerable.Range(0,state.Gaps.Length).Where(i=>!active.Contains(i)&&state.Gaps[i]<-1e-8).OrderBy(i=>state.Gaps[i]).FirstOrDefault(-1);
                    if(hit>=0) {active.Add(hit);lambda[hit]=0;added=true;break;}
                    var next=Differentiate(candidate,input,energyScale,cancellation);var nextResidual=Residual(next,active,candidateLambda,forceScale,energyScale);
                    if(nextResidual.Sum(v=>v*v)<oldNorm*(1-1e-4*alpha))
                    {x=candidate;for(var i=0;i<active.Count;i++) lambda[active[i]]=candidateLambda[i];accepted=true;break;}
                }
                catch(InvalidOperationException) { }
                catch(ArgumentOutOfRangeException) { }
            }
            if(added) continue;
            if(!accepted) throw new InvalidOperationException($"Installed equilibrium did not converge (normalized residual {max:0.###E+0}). No partial result was accepted.");
        }
        throw new InvalidOperationException("Installed equilibrium exceeded its active-set iteration limit.");
    }
    private Geometry GeometryAt(double[] x,VehicleStaticInput input,CancellationToken cancellation)
    {
        var key=(x[0]*CoordinateScale,x[1]*CoordinateScale,x[2]*CoordinateScale,x[3]*CoordinateScale);
        if(_geometryCache.TryGetValue(key,out var cached)) return cached;
        cancellation.ThrowIfCancellationRequested();var motion=_kinematics.Solve(new(key.Item1,key.Item2,key.Item3,key.Item4),input.SteeringWheelDegrees,_poseSeed);_poseSeed=motion;_poseEvaluations++;
        var mechanics=_mechanics.Evaluate(motion.Pose.Coordinates,false);var masses=new List<MassPoint>();
        if(input.Mass.Policy==MassAccountingPolicy.AggregateBase) masses.Add(new(input.Mass.AggregateBaseMassKg!.Value,input.Mass.BaseCenterOfMassM));
        else
        {
            foreach(var body in _graph.Bodies)
            {var mass=body.Component.Mass??throw new ArgumentException("Component-sum mass is missing: "+body.Path);if(mass.MassKg>0) masses.Add(new(mass.MassKg,_kinematics.System.Position(body.Path,mass.CenterOfMassM,motion.Pose.Coordinates)));}
            foreach(var element in _graph.ElasticElements)
            {
                var mass=element.Component.Mass??throw new ArgumentException("Component-sum mass is missing: "+element.Path);if(mass.MassKg==0) continue;
                var behavior=element.Component.Behavior!;var sourceA=element.Component.Frames.Single(f=>f.Id==element.Component.Ports.Single(p=>p.Id==behavior.FirstPortId).FrameId).Frame.OriginM;
                var sourceB=element.Component.Frames.Single(f=>f.Id==element.Component.Ports.Single(p=>p.Id==behavior.SecondPortId).FrameId).Frame.OriginM;var axis=sourceB-sourceA;
                if(axis.Length<1e-9||(mass.CenterOfMassM-sourceA).Cross(axis).Length/axis.Length>1e-8) throw new ArgumentException("Force-element mass datum must lie on its declared attachment axis: "+element.Path);
                var fraction=(mass.CenterOfMassM-sourceA).Dot(axis)/axis.Dot(axis);var a=_kinematics.System.Frame(element.First,motion.Pose.Coordinates).OriginM;var b=_kinematics.System.Frame(element.Second,motion.Pose.Coordinates).OriginM;masses.Add(new(mass.MassKg,a+(b-a)*fraction));
            }
        }
        masses.AddRange(input.Mass.AddedPayloads.Select(p=>new MassPoint(p.MassKg,p.PositionM)));if(masses.Sum(m=>m.Kg)<=0) throw new ArgumentException("Total supported mass must be positive.");
        var result=new Geometry(motion,mechanics,masses.ToArray());if(_geometryCache.Count>512) _geometryCache.Clear();_geometryCache.Add(key,result);return result;
    }
    private Configuration Evaluate(double[] x,VehicleStaticInput input,CancellationToken cancellation,DesignVector[]? materialAnchors=null)
    {
        if(x.Length!=7||x.Any(v=>!double.IsFinite(v))||x.Take(5).Any(v=>Math.Abs(v*CoordinateScale)>.5)||x.Skip(5).Any(v=>Math.Abs(v*CoordinateScale)>.8)) throw new InvalidOperationException("Vehicle pose is outside this quasi-static continuation range.");
        var geometry=GeometryAt(x,input,cancellation);var body=new ChassisPose(x[4]*CoordinateScale,x[5]*CoordinateScale,x[6]*CoordinateScale);
        var patches=geometry.Motion.Wheels.Select((w,i)=>body.World(w.CenterM)-new DesignVector(0,0,_radii[i])).ToArray();var masses=geometry.Masses.Select(m=>m with {Point=body.World(m.Point)}).ToArray();
        double EffectiveHeight(DesignVector point)=>point.Z+input.LongitudinalG*point.X+input.LateralG*point.Y;
        var potential=geometry.Mechanics.EnergyJ+masses.Sum(m=>m.Kg*Gravity*EffectiveHeight(m.Point));
        var reactionPoints=materialAnchors is null?patches:Contacts().Select((c,i)=>body.World(_kinematics.System.Position(c.InstancePath,materialAnchors[i],geometry.Motion.Pose.Coordinates))).ToArray();
        return new(geometry,body,patches,masses,potential,patches.Select(p=>p.Z).Concat(geometry.Mechanics.Limits.Select(l=>l.GapM)).ToArray(),reactionPoints.Select(EffectiveHeight).Concat(geometry.Mechanics.Limits.Select(l=>l.GapM)).ToArray());
    }
    private WheelGroundContact[] Contacts()=>new[]{"FL","FR","RL","RR"}.Select(c=>_graph.Root.Vehicle!.Contacts.Single(p=>p.Corner==c)).ToArray();
    private DesignVector[] MaterialAnchors(Configuration state)=>Contacts().Select((contact,i)=>
    {
        var body=_graph.Bodies.Single(b=>b.Path==contact.InstancePath);var center=body.Component.Points.Single(p=>p.Id==contact.CenterPointId).PositionM;var down=new DesignVector(0,0,-_radii[i]);
        DesignVector Axis(DesignVector v)=>state.Chassis.Rotate(_kinematics.System.Direction(contact.InstancePath,v,state.Geometry.Motion.Pose.Coordinates));
        // Virtual work follows the material contact point, not the reselected lowest point of a rolling envelope.
        return center+new DesignVector(down.Dot(Axis(new(1,0,0))),down.Dot(Axis(new(0,1,0))),down.Dot(Axis(new(0,0,1))));
    }).ToArray();
    private Derivatives Differentiate(double[] x,VehicleStaticInput input,double energyScale,CancellationToken cancellation)
    {
        const double h=2e-4;var state=Evaluate(x,input,cancellation);var anchors=MaterialAnchors(state);var gradient=new double[7];var gap=new double[state.Gaps.Length,7];var reaction=new double[state.Gaps.Length,7];
        for(var i=0;i<7;i++)
        {var p=(double[])x.Clone();var m=(double[])x.Clone();p[i]+=h;m[i]-=h;var a=Evaluate(p,input,cancellation,anchors);var b=Evaluate(m,input,cancellation,anchors);gradient[i]=(a.Potential-b.Potential)/(2*h*energyScale);for(var j=0;j<state.Gaps.Length;j++) {gap[j,i]=(a.Gaps[j]-b.Gaps[j])/(2*h);reaction[j,i]=(a.ReactionPotentials[j]-b.ReactionPotentials[j])/(2*h);}}
        return new(state,gradient,gap,reaction);
    }
    private double[,] Hessian(double[] x,VehicleStaticInput input,IReadOnlyList<int> active,double[] lambda,double forceScale,double energyScale,CancellationToken cancellation)
    {
        var anchors=MaterialAnchors(Evaluate(x,input,cancellation));
        double L(double[] u) {var c=Evaluate(u,input,cancellation,anchors);return (c.Potential-active.Select((index,j)=>lambda[j]*forceScale*c.ReactionPotentials[index]).Sum())/energyScale;}
        const double h=.004;var result=new double[7,7];var center=L(x);
        for(var i=0;i<7;i++)
        {
            var p=(double[])x.Clone();var m=(double[])x.Clone();p[i]+=h;m[i]-=h;result[i,i]=(L(p)-2*center+L(m))/(h*h);
            for(var j=0;j<i;j++) {var pp=(double[])x.Clone();var pm=(double[])x.Clone();var mp=(double[])x.Clone();var mm=(double[])x.Clone();pp[i]+=h;pp[j]+=h;pm[i]+=h;pm[j]-=h;mp[i]-=h;mp[j]+=h;mm[i]-=h;mm[j]-=h;result[i,j]=result[j,i]=(L(pp)-L(pm)-L(mp)+L(mm))/(4*h*h);}
        }
        return result;
    }
    private static double[] Residual(Derivatives d,IReadOnlyList<int> active,double[] lambda,double forceScale,double energyScale)
    {
        var result=new double[7+active.Count];Array.Copy(d.Gradient,result,7);
        for(var j=0;j<active.Count;j++) {for(var i=0;i<7;i++) result[i]-=lambda[j]*forceScale/energyScale*d.ReactionJacobian[active[j],i];result[7+j]=d.State.Gaps[active[j]]/LengthScale;}
        return result;
    }
    private InstalledEquilibriumResult Accepted(VehicleStaticInput input,Configuration state,IReadOnlyList<int> active,double[] lambda,double forceScale,double residual,int iterations)
    {
        var normal=new double[4];var reactions=new List<LimitReaction>();
        for(var j=0;j<active.Count;j++) {var index=active[j];var force=Math.Max(0,lambda[j]*forceScale);if(index<4) normal[index]=force;else {var limit=state.Geometry.Mechanics.Limits[index-4];reactions.Add(new(limit.Id,force,limit.GapM));}}
        if(state.Gaps.Any(g=>g< -1e-8)||active.Any(i=>Math.Abs(state.Gaps[i])>1e-8)) throw new InvalidOperationException("Converged equations still violate a rigid contact or travel boundary.");
        _mechanics.Evaluate(state.Geometry.Motion.Pose.Coordinates);var direction=new DesignVector(input.LongitudinalG,input.LateralG,1);var mass=state.Masses.Sum(m=>m.Kg);
        var forceResidual=direction*(normal.Sum()-mass*Gravity);var moment=state.Masses.Aggregate(new DesignVector(0,0,0),(sum,m)=>sum+m.Point.Cross(direction*(-m.Kg*Gravity)));
        for(var i=0;i<4;i++) moment+=new DesignVector(state.Patch[i].X,state.Patch[i].Y,0).Cross(direction*normal[i]);
        if(forceResidual.Length>.05||moment.Length>.05) throw new InvalidOperationException($"Final force/moment closure failed ({forceResidual.Length:0.###} N, {moment.Length:0.###} N·m).");
        var cg=state.Masses.Aggregate(new DesignVector(0,0,0),(sum,m)=>sum+m.Point*m.Kg)/mass;
        var wheels=state.Geometry.Motion.Wheels.Select(w=>
        {var forward=state.Chassis.Rotate(w.Forward);var outward=state.Chassis.Rotate(w.Outward);return w with {CenterM=state.Chassis.World(w.CenterM),Forward=forward,Outward=outward,CamberRad=-Math.Atan2(outward.Z,Math.Sqrt(outward.X*outward.X+outward.Y*outward.Y)),HeadingRad=Math.Atan2(forward.Y,forward.X)};}).ToImmutableArray();
        var inputHash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(DesignJson.Serialize(new {input,manifest=_graph.Manifest,adjustments=_graph.AppliedAdjustments}))));
        return new(input,state.Chassis,state.Geometry.Motion,state.Geometry.Mechanics,wheels,[..normal],[..reactions],[.._graph.Root.Vehicle!.RideHeightDatums.Select(p=>new SolvedRideHeight(p.Id,p.Name,state.Chassis.World(p.PositionM).Z))],mass,cg,forceResidual,moment,residual,iterations,_poseEvaluations)
            {Manifest=_graph.Manifest,Adjustments=_graph.AppliedAdjustments,InputSha256=inputHash};
    }
    private static void ValidateMass(SetupMass mass)
    {
        if(!Enum.IsDefined(mass.Policy)||!mass.BaseCenterOfMassM.IsFinite) throw new ArgumentException("Invalid mass policy/datum.");
        if(mass.Policy==MassAccountingPolicy.AggregateBase&&(!mass.AggregateBaseMassKg.HasValue||!double.IsFinite(mass.AggregateBaseMassKg.Value)||mass.AggregateBaseMassKg.Value<=0)) throw new ArgumentException("Aggregate base mass must be positive.");
        if(mass.Policy==MassAccountingPolicy.ComponentSum&&mass.AggregateBaseMassKg is not null) throw new ArgumentException("Component-sum mass cannot include aggregate base mass.");
        if(mass.AddedPayloads.IsDefault||mass.AddedPayloads.Select(p=>p.Id).Distinct().Count()!=mass.AddedPayloads.Length||mass.AddedPayloads.Any(p=>string.IsNullOrWhiteSpace(p.Id)||!double.IsFinite(p.MassKg)||p.MassKg<=0||!p.PositionM.IsFinite)) throw new ArgumentException("Invalid or duplicate payload mass.");
    }
    private static double[] SolveLinear(double[,] matrix,double[] rhs)
    {
        var n=rhs.Length;var a=(double[,])matrix.Clone();var b=(double[])rhs.Clone();
        for(var p=0;p<n;p++) {var pivot=p;for(var r=p+1;r<n;r++) if(Math.Abs(a[r,p])>Math.Abs(a[pivot,p])) pivot=r;if(Math.Abs(a[pivot,p])<1e-10) throw new InvalidOperationException("Constraint Jacobian is rank deficient.");for(var c=p;c<n;c++) (a[p,c],a[pivot,c])=(a[pivot,c],a[p,c]);(b[p],b[pivot])=(b[pivot],b[p]);for(var r=p+1;r<n;r++) {var f=a[r,p]/a[p,p];for(var c=p;c<n;c++) a[r,c]-=f*a[p,c];b[r]-=f*b[p];}}
        var result=new double[n];for(var r=n-1;r>=0;r--) {var sum=b[r];for(var c=r+1;c<n;c++) sum-=a[r,c]*result[c];result[r]=sum/a[r,r];}return result;
    }
}
