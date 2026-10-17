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
        var system=new ConstraintMechanism(graph);var free=system.FreeBodies;var scale=system.LengthScaleM;var zero=new double[system.CoordinateCount];
        double[] Residual(double[] q)=>system.Residual(q);
        var baseline=Residual(zero);var matrix=ConstraintMechanism.Jacobian(Residual,zero);
        var rank=MatrixRank(matrix,1e-7);var dof=zero.Length-rank;var redundant=baseline.Length-rank;var max=baseline.Select(Math.Abs).DefaultIfEmpty(0).Max();
        var diagnostics=new List<string>();
        if(graph.GroundedBodies.Length==0) diagnostics.Add("No body is grounded; global rigid-body motion remains.");
        if(system.GaugeCount>0) diagnostics.Add($"{system.GaugeCount} straight-rod axial spins are removed by coordinate conventions. Their geometry and mass are collinear; no mount orientation or physical torsional restraint is inferred.");
        if(max>=1e-7) diagnostics.Add("Joint frames do not close at this pose. Reposition/mate the parts before analysis.");
        if(redundant>0) diagnostics.Add($"{redundant} constraint rows are redundant or singular at this pose.");
        if(expectedDof is {} expected&&dof!=expected) diagnostics.Add($"Expected {expected} operating freedoms; found {dof}. Check missing links, extra constraints or a singular pose.");
        foreach(var body in free.Where(body=>!graph.Joints.Any(j=>j.First.BodyPath==body.Path||j.Second.BodyPath==body.Path))) diagnostics.Add("Disconnected body: "+body.Path);
        diagnostics.Add("Local mobility only; finite-travel continuation and loaded equilibrium are separate checks.");
        return new(zero.Length,baseline.Length,rank,dof,redundant,max,scale,[..diagnostics]);
    }
    internal static int MatrixRank(double[,] matrix,double tolerance)
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
