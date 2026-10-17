using System.Collections.Immutable;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.Solvers.Design;

public sealed record MechanismMobilityReport(int FreeCoordinates,int ConstraintRows,int Rank,int DegreesOfFreedom,int RedundantRows,
    double MaximumScaledClosureError,double CharacteristicLengthM,ImmutableArray<string> Diagnostics)
{
    public bool IsClosed=>MaximumScaledClosureError<1e-7;
}

/// <summary>Local constraint Jacobian rank at the declared pose; does not certify finite travel or equilibrium.</summary>
public static class MechanismMobility
{
    public static MechanismMobilityReport Inspect(ResolvedAssemblyGraph graph,int? expectedDof=null)
    {
        var free=graph.Bodies.Where(x=>!graph.GroundedBodies.Contains(x.Path)).ToArray();
        var lookup=free.Select((body,index)=>(body.Path,index)).ToDictionary(x=>x.Path,x=>x.index,StringComparer.Ordinal);
        var origins=graph.Bodies.ToDictionary(x=>x.Path,x=>x.Transform.Origin,StringComparer.Ordinal);
        var scale=Math.Max(.1,graph.Bodies.SelectMany(b=>b.Component.Points.Select(p=>b.Transform.Point(p.PositionM))).Select(p=>p.Length).DefaultIfEmpty(.1).Max());
        // Translate all origins before determining scale so a far-away assembly is not falsely ill-conditioned.
        if(graph.Bodies.Length>0)
        {var datum=graph.Bodies[0].Transform.Origin;scale=Math.Max(.1,graph.Bodies.SelectMany(b=>b.Component.Points.Select(p=>(b.Transform.Point(p.PositionM)-datum).Length)).DefaultIfEmpty(.1).Max());}
        var zero=new double[free.Length*6];
        double[] Residual(double[] q)
        {
            DesignFrame Frame(ResolvedPort p)
            {
                if(!lookup.TryGetValue(p.BodyPath,out var i)) return p.Frame;
                return DesignTransform.Displace(p.Frame,new DesignVector(q[6*i],q[6*i+1],q[6*i+2])*scale,new(q[6*i+3],q[6*i+4],q[6*i+5]),origins[p.BodyPath]);
            }
            var rows=new List<double>();
            void Vector(DesignVector v) {rows.Add(v.X);rows.Add(v.Y);rows.Add(v.Z);}
            foreach(var joint in graph.Joints)
            {
                var a=Frame(joint.First);var b=Frame(joint.Second);var d=(b.OriginM-a.OriginM)/scale;
                var x=a.ReferenceDirection;var y=a.Axis.Cross(x);
                switch(joint.Kind)
                {
                    case JointKind.Fixed:Vector(d);Vector(DesignTransform.RotationError(a,b));break;
                    case JointKind.Spherical:Vector(d);break;
                    case JointKind.Revolute:Vector(d);rows.Add(a.Axis.Dot(b.ReferenceDirection));rows.Add(a.Axis.Dot(b.Axis.Cross(b.ReferenceDirection)));break;
                    case JointKind.Prismatic:rows.Add(d.Dot(x));rows.Add(d.Dot(y));Vector(DesignTransform.RotationError(a,b));break;
                    case JointKind.Cylindrical:rows.Add(d.Dot(x));rows.Add(d.Dot(y));rows.Add(a.Axis.Dot(b.ReferenceDirection));rows.Add(a.Axis.Dot(b.Axis.Cross(b.ReferenceDirection)));break;
                    case JointKind.Universal:Vector(d);rows.Add(a.Axis.Dot(b.Axis));break;
                    case JointKind.RigidDistance:rows.Add(d.Length-joint.DistanceM!.Value/scale);break;
                    default:throw new ArgumentException("Unsupported joint.");
                }
            }
            return rows.ToArray();
        }
        var baseline=Residual(zero);var matrix=new double[baseline.Length,zero.Length];const double step=1e-6;
        for(var column=0;column<zero.Length;column++)
        {zero[column]=step;var plus=Residual(zero);zero[column]=-step;var minus=Residual(zero);zero[column]=0;for(var row=0;row<baseline.Length;row++) matrix[row,column]=(plus[row]-minus[row])/(2*step);}
        var rank=Rank(matrix,1e-7);var dof=zero.Length-rank;var redundant=baseline.Length-rank;var max=baseline.Select(Math.Abs).DefaultIfEmpty(0).Max();
        var diagnostics=new List<string>();
        if(graph.GroundedBodies.Length==0) diagnostics.Add("No body is grounded; global rigid-body motion remains.");
        if(max>=1e-7) diagnostics.Add("Joint frames do not close at this pose. Reposition/mate the parts before analysis.");
        if(redundant>0) diagnostics.Add($"{redundant} constraint rows are redundant or singular at this pose.");
        if(expectedDof is {} expected&&dof!=expected) diagnostics.Add($"Expected {expected} operating freedoms; found {dof}. Check missing links, extra constraints or a singular pose.");
        foreach(var body in free.Where(body=>!graph.Joints.Any(j=>j.First.BodyPath==body.Path||j.Second.BodyPath==body.Path))) diagnostics.Add("Disconnected body: "+body.Path);
        diagnostics.Add("Local mobility only; finite-travel continuation and loaded equilibrium are separate checks.");
        return new(zero.Length,baseline.Length,rank,dof,redundant,max,scale,[..diagnostics]);
    }
    private static int Rank(double[,] matrix,double tolerance)
    {
        var rows=matrix.GetLength(0);var cols=matrix.GetLength(1);var rank=0;
        // Complete pivoting keeps mixed joint families and closed loops from depending on input ordering.
        while(rank<Math.Min(rows,cols))
        {
            var best=0d;var pr=rank;var pc=rank;
            for(var r=rank;r<rows;r++) for(var c=rank;c<cols;c++) if(Math.Abs(matrix[r,c])>best) {best=Math.Abs(matrix[r,c]);pr=r;pc=c;}
            if(best<tolerance) break;
            for(var c=0;c<cols;c++) (matrix[rank,c],matrix[pr,c])=(matrix[pr,c],matrix[rank,c]);
            for(var r=0;r<rows;r++) (matrix[r,rank],matrix[r,pc])=(matrix[r,pc],matrix[r,rank]);
            for(var r=rank+1;r<rows;r++) {var factor=matrix[r,rank]/matrix[rank,rank];for(var c=rank+1;c<cols;c++) matrix[r,c]-=factor*matrix[rank,c];matrix[r,rank]=0;}
            rank++;
        }
        return rank;
    }
}
