using SuspensionSimulation.Core;

namespace SuspensionSimulation.Solvers;

public sealed partial class FourStrutQuasiStaticSolver
{
    private static NewtonSolution SolveWithTravelLimits(IReadOnlyList<CornerGeometry> geometry,
        IReadOnlyDictionary<string,CornerSpring> springs,IReadOnlyDictionary<string,AntiRollBarModel> bars,double[] target)
    {
        var free=SolveNewton(geometry,springs,bars,target);
        bool Feasible(double[] q)=>geometry.All(g=>springs[g.Corner].HardTravelLimit is not {} b ||
            Compression(g,q)>=b.MinimumJounceM-1e-10 && Compression(g,q)<=b.MaximumJounceM+1e-10);
        if(Feasible(free.State)) return free;
        // Only three body DOFs exist. At most three independent contact normals are
        // needed to represent a reaction, even when four stops touch simultaneously.
        // Enumerating this tiny set avoids history dependence and active-set cycling.
        var candidates=new List<TravelContact[]>();
        for(var code=1;code<81;code++)
        {
            var active=new List<TravelContact>();var remaining=code;var valid=true;
            for(var i=0;i<4;i++)
            {
                var state=remaining%3;remaining/=3;if(state==0) continue;
                if(springs[geometry[i].Corner].HardTravelLimit is not {} b) {valid=false;break;}
                var sign=state==1?-1d:1d;
                active.Add(new(i,sign,sign*(state==1?b.MinimumJounceM:b.MaximumJounceM)));
            }
            if(valid && active.Count<=3) candidates.Add(active.ToArray());
        }
        foreach(var active in candidates.OrderBy(x=>x.Length))
        {
            var n=3+active.Length;var x=new double[n];Array.Copy(free.State,x,3);
            double[] Residual(double[] value)
            {
                var result=new double[n];var f=Resultants(geometry,Forces(geometry,springs,bars,value).TotalDeltaLoads);
                for(var i=0;i<3;i++) result[i]=f[i]-target[i];
                for(var k=0;k<active.Length;k++)
                {
                    var c=active[k];var g=geometry[c.Index];var a=new[]{c.Sign,-g.Y*c.Sign,g.X*c.Sign};
                    for(var i=0;i<3;i++) result[i]+=a[i]*value[3+k];
                    result[3+k]=c.Sign*Compression(g,value)-c.Bound;
                }
                return result;
            }
            static double Error(double[] r)=>r.Select((v,i)=>Math.Abs(v)*(i<3?1:100_000)).Max();
            var converged=false;var iterations=0;
            try
            {
                for(;iterations<40;iterations++)
                {
                    var r=Residual(x);
                    if(Error(r)<1e-6) {converged=true;break;}
                    var matrix=new double[n,n];var elastic=Jacobian(geometry,springs,bars,x);
                    for(var i=0;i<3;i++) for(var j=0;j<3;j++) matrix[i,j]=elastic[i,j];
                    for(var k=0;k<active.Length;k++)
                    {
                        var c=active[k];var g=geometry[c.Index];var a=new[]{c.Sign,-g.Y*c.Sign,g.X*c.Sign};
                        for(var i=0;i<3;i++) matrix[i,3+k]=matrix[3+k,i]=a[i];
                    }
                    var step=SolveContactSystem(matrix,r);var scale=1d;var accepted=false;
                    while(scale>=1d/1024)
                    {
                        var next=x.Select((v,i)=>v-scale*step[i]).ToArray();
                        if(Error(Residual(next))<Error(r)) {x=next;accepted=true;break;}
                        scale/=2;
                    }
                    if(!accepted) break;
                }
            }
            catch(InvalidOperationException) {continue;} // Dependent contact normals.
            if(!converged || !Feasible(x) || active.Select((_,i)=>x[3+i]).Any(v=>v< -1e-7)) continue;
            var reactions=new double[4];var directions=Enumerable.Repeat("none",4).ToArray();
            for(var k=0;k<active.Length;k++)
            {
                var c=active[k];if(x[3+k]<=1e-7) continue;
                reactions[c.Index]=c.Sign*x[3+k];directions[c.Index]=c.Sign<0?"rebound":"bump";
            }
            DistributeFourContactReaction(geometry,springs,x,reactions,directions);
            return new(x.Take(3).ToArray(),free.Iterations+iterations,true,reactions,directions);
        }
        throw new InvalidOperationException("No feasible equilibrium satisfies the rigid travel stops and unilateral reaction directions.");
    }

    private static void DistributeFourContactReaction(IReadOnlyList<CornerGeometry> geometry,
        IReadOnlyDictionary<string,CornerSpring> springs,double[] q,double[] reactions,string[] directions)
    {
        var signs=new double[4];
        for(var i=0;i<4;i++)
        {
            if(springs[geometry[i].Corner].HardTravelLimit is not {} limit) return;
            var j=Compression(geometry[i],q);
            signs[i]=Math.Abs(j-limit.MaximumJounceM)<1e-9?1:Math.Abs(j-limit.MinimumJounceM)<1e-9?-1:0;
            if(signs[i]==0) return;
        }
        // Four rigid contacts leave one statically indeterminate reaction mode.
        // Choose the minimum-norm nonnegative solution, preserving symmetry and
        // exact body resultants instead of assigning a zero to an arbitrary corner.
        var columns=geometry.Select((g,i)=>new[]{signs[i],-g.Y*signs[i],g.X*signs[i]}).ToArray();
        var matrix=new double[3,3];for(var i=0;i<3;i++) for(var j=0;j<3;j++) matrix[i,j]=columns[j][i];
        var first=SolveContactSystem(matrix,columns[3].Select(v=>-v).ToArray());
        var nullVector=first.Concat([1d]).ToArray();var magnitudes=reactions.Select((r,i)=>r*signs[i]).ToArray();
        var low=double.NegativeInfinity;var high=double.PositiveInfinity;
        for(var i=0;i<4;i++)
        {
            if(nullVector[i]>1e-12) low=Math.Max(low,-magnitudes[i]/nullVector[i]);
            else if(nullVector[i]< -1e-12) high=Math.Min(high,-magnitudes[i]/nullVector[i]);
        }
        var t=Math.Clamp(-magnitudes.Select((v,i)=>v*nullVector[i]).Sum()/nullVector.Sum(v=>v*v),low,high);
        for(var i=0;i<4;i++)
        {
            var magnitude=Math.Max(0,magnitudes[i]+t*nullVector[i]);
            reactions[i]=magnitude>1e-7?signs[i]*magnitude:0;
            directions[i]=reactions[i]==0?"none":signs[i]>0?"bump":"rebound";
        }
    }

    private static double[] SolveContactSystem(double[,] matrix,double[] rhs)
    {
        var n=rhs.Length;var m=new double[n,n+1];
        for(var i=0;i<n;i++) {for(var j=0;j<n;j++) m[i,j]=matrix[i,j];m[i,n]=rhs[i];}
        for(var p=0;p<n;p++)
        {
            var pivot=Enumerable.Range(p,n-p).MaxBy(i=>Math.Abs(m[i,p]));
            if(Math.Abs(m[pivot,p])<1e-12) throw new InvalidOperationException("Dependent rigid contacts.");
            if(pivot!=p) for(var j=p;j<=n;j++) (m[p,j],m[pivot,j])=(m[pivot,j],m[p,j]);
            for(var i=p+1;i<n;i++) {var f=m[i,p]/m[p,p];for(var j=p;j<=n;j++) m[i,j]-=f*m[p,j];}
        }
        var solution=new double[n];
        for(var i=n-1;i>=0;i--) solution[i]=(m[i,n]-Enumerable.Range(i+1,n-i-1).Sum(j=>m[i,j]*solution[j]))/m[i,i];
        return solution;
    }
    private sealed record TravelContact(int Index,double Sign,double Bound);
}
