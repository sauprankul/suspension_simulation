using SuspensionSimulation.Design;

namespace SuspensionSimulation.Solvers.Design;

public sealed record MechanismDriver(string BodyPath,string PointId,DesignVector UnitDirection,double TargetCoordinateM);
public sealed record MechanismPose(double[] Coordinates,double MaximumScaledResidual,int Iterations,bool Converged);

/// <summary>Double-precision rigid-body graph constraints shared by mobility and finite-displacement solving.</summary>
public sealed class ConstraintMechanism
{
    public ResolvedAssemblyGraph Graph {get;}
    public ResolvedBody[] FreeBodies {get;}
    public double LengthScaleM {get;}
    public int CoordinateCount=>FreeBodies.Length*6;
    private readonly Dictionary<string,int> _lookup;
    private readonly Dictionary<string,ResolvedBody> _bodies;
    private readonly (ResolvedBody Body,DesignVector Axis,DesignVector Normal)[] _gauges;
    public int GaugeCount=>_gauges.Length;
    public ConstraintMechanism(ResolvedAssemblyGraph graph)
    {
        Graph=graph;FreeBodies=graph.Bodies.Where(x=>!graph.GroundedBodies.Contains(x.Path)).ToArray();
        _lookup=FreeBodies.Select((body,index)=>(body.Path,index)).ToDictionary(x=>x.Path,x=>x.index,StringComparer.Ordinal);_bodies=graph.Bodies.ToDictionary(x=>x.Path,StringComparer.Ordinal);
        var datum=graph.Bodies.FirstOrDefault()?.Transform.Origin??new DesignVector(0,0,0);
        LengthScaleM=Math.Max(.1,graph.Bodies.SelectMany(b=>b.Component.Points.Select(p=>(b.Transform.Point(p.PositionM)-datum).Length)).DefaultIfEmpty(.1).Max());
        _gauges=FreeBodies.Where(b=>b.Component.AxialGauge is not null).Where(b=>
        {
            var mates=graph.Joints.Where(j=>j.First.BodyPath==b.Path||j.Second.BodyPath==b.Path).ToArray();
            return mates.Length==2&&mates.All(j=>j.Kind==JointKind.Spherical)&&mates.Select(j=>j.First.BodyPath==b.Path?j.First.PortId:j.Second.PortId).Distinct().Count()==2;
        }).Select(b=>
        {
            var g=b.Component.AxialGauge!;var axis=b.Component.Points.Single(p=>p.Id==g.EndPointId).PositionM-b.Component.Points.Single(p=>p.Id==g.StartPointId).PositionM;axis/=axis.Length;
            var candidate=Math.Abs(axis.Z)<.9?new DesignVector(0,0,1):new DesignVector(1,0,0);var normal=candidate-axis*axis.Dot(candidate);normal/=normal.Length;return (b,axis,normal);
        }).ToArray();
    }
    public DesignFrame Frame(ResolvedPort port,double[] q)=>WorldFrame(port.BodyPath,port.Frame,q);
    private DesignFrame WorldFrame(string bodyPath,DesignFrame world,double[] q)
    {
        if(q.Length!=CoordinateCount) throw new ArgumentException("Pose coordinate count differs from this graph.");
        if(!_lookup.TryGetValue(bodyPath,out var i)) return world;
        return DesignTransform.Displace(world,new DesignVector(q[6*i],q[6*i+1],q[6*i+2])*LengthScaleM,new(q[6*i+3],q[6*i+4],q[6*i+5]),_bodies[bodyPath].Transform.Origin);
    }
    public DesignVector Point(string bodyPath,string pointId,double[] q)
    {
        var body=_bodies[bodyPath];var point=body.Component.Points.SingleOrDefault(x=>x.Id==pointId)??throw new ArgumentException("Missing point "+bodyPath+":"+pointId);
        var f=new DesignFrame(body.Transform.Point(point.PositionM),new(0,0,1),new(1,0,0));return WorldFrame(bodyPath,f,q).OriginM;
    }
    public DesignVector Direction(string bodyPath,DesignVector local,double[] q)
    {
        var body=_bodies[bodyPath];var v=body.Transform.Direction(local);return _lookup.TryGetValue(bodyPath,out var i)?DesignTransform.Rotate(v,new(q[6*i+3],q[6*i+4],q[6*i+5])):v;
    }
    public double[] Residual(double[] q,IReadOnlyList<MechanismDriver>? drivers=null)
    {
        var rows=new List<double>();void Vector(DesignVector v) {rows.Add(v.X);rows.Add(v.Y);rows.Add(v.Z);}
        foreach(var joint in Graph.Joints)
        {
            var a=Frame(joint.First,q);var b=Frame(joint.Second,q);var d=(b.OriginM-a.OriginM)/LengthScaleM;var x=a.ReferenceDirection;var y=a.Axis.Cross(x);
            switch(joint.Kind)
            {
                case JointKind.Fixed:Vector(d);Vector(DesignTransform.RotationError(a,b));break;
                case JointKind.Spherical:Vector(d);break;
                case JointKind.Revolute:Vector(d);rows.Add(a.Axis.Dot(b.ReferenceDirection));rows.Add(a.Axis.Dot(b.Axis.Cross(b.ReferenceDirection)));break;
                case JointKind.Prismatic:rows.Add(d.Dot(x));rows.Add(d.Dot(y));Vector(DesignTransform.RotationError(a,b));break;
                case JointKind.Cylindrical:rows.Add(d.Dot(x));rows.Add(d.Dot(y));rows.Add(a.Axis.Dot(b.ReferenceDirection));rows.Add(a.Axis.Dot(b.Axis.Cross(b.ReferenceDirection)));break;
                case JointKind.Universal:Vector(d);rows.Add(a.Axis.Dot(b.Axis));break;
                case JointKind.RigidDistance:rows.Add(d.Length-joint.DistanceM!.Value/LengthScaleM);break;
                default:throw new ArgumentException("Unsupported joint.");
            }
        }
        foreach(var (body,axisLocal,normalLocal) in _gauges)
        {
            var axis0=body.Transform.Direction(axisLocal);var normal0=body.Transform.Direction(normalLocal);
            var axis=Direction(body.Path,axisLocal,q);var normal=Direction(body.Path,normalLocal,q);var cross=axis0.Cross(axis);var sine=cross.Length;var cosine=axis0.Dot(axis);
            if(cosine<-.999999) throw new InvalidOperationException("Axial rod gauge crossed its 180 degree chart boundary; continue from a closer pose.");
            var transported=sine<1e-14?normal0:DesignTransform.Rotate(normal0,cross/sine*Math.Atan2(sine,cosine));
            rows.Add(normal.Dot(axis.Cross(transported)));
        }
        if(drivers is not null) foreach(var driver in drivers)
        {
            if(!driver.UnitDirection.IsFinite||Math.Abs(driver.UnitDirection.Length-1)>1e-9||!double.IsFinite(driver.TargetCoordinateM)) throw new ArgumentException("Invalid operating driver.");
            rows.Add((Point(driver.BodyPath,driver.PointId,q).Dot(driver.UnitDirection)-driver.TargetCoordinateM)/LengthScaleM);
        }
        return rows.ToArray();
    }
    public static double[,] Jacobian(Func<double[],double[]> residual,double[] q)
    {
        var rowCount=residual(q).Length;var result=new double[rowCount,q.Length];const double step=1e-6;
        for(var column=0;column<q.Length;column++)
        {var plus=(double[])q.Clone();var minus=(double[])q.Clone();plus[column]+=step;minus[column]-=step;var rp=residual(plus);var rm=residual(minus);for(var row=0;row<rowCount;row++) result[row,column]=(rp[row]-rm[row])/(2*step);}
        return result;
    }
    public MechanismPose Solve(IReadOnlyList<MechanismDriver> drivers,MechanismPose? seed=null)
    {
        var q=seed is null?new double[CoordinateCount]:(double[])seed.Coordinates.Clone();
        double[] R(double[] state)=>Residual(state,drivers);
        for(var iteration=0;iteration<60;iteration++)
        {
            var r=R(q);var max=r.Select(Math.Abs).DefaultIfEmpty(0).Max();
            if(max<1e-10) return new(q,max,iteration,MechanismMobility.MatrixRank(Jacobian(R,q),1e-8)==CoordinateCount);
            var jacobian=Jacobian(R,q);double[] delta;
            try {delta=SolveLeastSquares(jacobian,r);}catch(InvalidOperationException) {return new(q,max,iteration,false);}
            var accepted=false;var norm=r.Sum(x=>x*x);
            for(var alpha=1d;alpha>=1d/1024;alpha*=.5)
            {
                var candidate=q.Select((x,i)=>x-alpha*delta[i]).ToArray();var rc=R(candidate);
                if(rc.All(double.IsFinite)&&rc.Sum(x=>x*x)<norm) {q=candidate;accepted=true;break;}
            }
            if(!accepted) return new(q,max,iteration,false);
        }
        return new(q,R(q).Select(Math.Abs).DefaultIfEmpty(0).Max(),60,false);
    }
    private static double[] SolveLeastSquares(double[,] source,double[] residual)
    {
        var rows=source.GetLength(0);var columns=source.GetLength(1);if(rows<columns) throw new InvalidOperationException("Insufficient operating drivers.");
        var a=(double[,])source.Clone();var b=(double[])residual.Clone();var order=Enumerable.Range(0,columns).ToArray();
        // Householder QR with column pivoting avoids squaring the condition number in normal equations.
        for(var k=0;k<columns;k++)
        {
            var pivot=k;var normBest=0d;for(var c=k;c<columns;c++) {var n=0d;for(var r=k;r<rows;r++) n+=a[r,c]*a[r,c];if(n>normBest) {normBest=n;pivot=c;}}
            if(normBest<1e-18) throw new InvalidOperationException("Singular mechanism/driver Jacobian.");
            for(var r=0;r<rows;r++) (a[r,k],a[r,pivot])=(a[r,pivot],a[r,k]);(order[k],order[pivot])=(order[pivot],order[k]);
            var norm=Math.Sqrt(normBest);var sign=a[k,k]>=0?1d:-1d;var v=new double[rows-k];for(var r=k;r<rows;r++) v[r-k]=a[r,k];v[0]+=sign*norm;
            var vv=v.Sum(x=>x*x);
            for(var c=k;c<columns;c++) {var dot=0d;for(var r=k;r<rows;r++) dot+=v[r-k]*a[r,c];for(var r=k;r<rows;r++) a[r,c]-=2*v[r-k]*dot/vv;}
            var db=0d;for(var r=k;r<rows;r++) db+=v[r-k]*b[r];for(var r=k;r<rows;r++) b[r]-=2*v[r-k]*db/vv;
        }
        var x=new double[columns];for(var r=columns-1;r>=0;r--) {var sum=b[r];for(var c=r+1;c<columns;c++) sum-=a[r,c]*x[c];x[r]=sum/a[r,r];}
        var output=new double[columns];for(var c=0;c<columns;c++) output[order[c]]=x[c];return output;
    }
}
