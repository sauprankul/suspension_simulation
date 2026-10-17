namespace SuspensionSimulation.Design;

/// <summary>Orthogonal placement, retaining reflection parity for geometry. Port frames remain right-handed.</summary>
public sealed record DesignTransform(DesignVector Origin,DesignVector X,DesignVector Y,DesignVector Z)
{
    public static DesignTransform Identity {get;}=new(new(0,0,0),new(1,0,0),new(0,1,0),new(0,0,1));
    public DesignVector Direction(DesignVector v)=>X*v.X+Y*v.Y+Z*v.Z;
    public DesignVector Point(DesignVector p)=>Origin+Direction(p);
    public DesignFrame Frame(DesignFrame f)=>new(Point(f.OriginM),Direction(f.Axis),Direction(f.ReferenceDirection));
    public DesignTransform Then(DesignTransform parent)=>new(parent.Point(Origin),parent.Direction(X),parent.Direction(Y),parent.Direction(Z));
    public static DesignTransform From(InstancePlacement placement)
    {
        DesignValidation.Frame(placement.Frame);var f=placement.Frame;
        var t=new DesignTransform(f.OriginM,f.ReferenceDirection,f.Axis.Cross(f.ReferenceDirection),f.Axis);
        if(placement.Reflection is not {} m) return t;
        DesignVector R(DesignVector p)=>p-m.UnitNormal*(2*p.Dot(m.UnitNormal));
        return new(m.OriginM+R(t.Origin-m.OriginM),R(t.X),R(t.Y),R(t.Z));
    }
    public static DesignVector Rotate(DesignVector v,DesignVector rotation)
    {
        var angle=rotation.Length;if(angle<1e-15) return v;
        var axis=rotation/angle;return v*Math.Cos(angle)+axis.Cross(v)*Math.Sin(angle)+axis*(axis.Dot(v)*(1-Math.Cos(angle)));
    }
    public static DesignFrame Displace(DesignFrame f,DesignVector translation,DesignVector rotation,DesignVector center)=>
        new(center+Rotate(f.OriginM-center,rotation)+translation,Rotate(f.Axis,rotation),Rotate(f.ReferenceDirection,rotation));
    public static DesignVector RotationError(DesignFrame a,DesignFrame b)
    {
        var ax=a.ReferenceDirection;var ay=a.Axis.Cross(ax);var az=a.Axis;
        var bx=b.ReferenceDirection;var by=b.Axis.Cross(bx);var bz=b.Axis;
        double M(int i,int j)
        {
            static double C(DesignVector v,int k)=>k==0?v.X:k==1?v.Y:v.Z;
            return C(bx,i)*C(ax,j)+C(by,i)*C(ay,j)+C(bz,i)*C(az,j);
        }
        var trace=M(0,0)+M(1,1)+M(2,2);double w,x,y,z;
        if(trace>0) {var s=Math.Sqrt(trace+1)*2;w=s/4;x=(M(2,1)-M(1,2))/s;y=(M(0,2)-M(2,0))/s;z=(M(1,0)-M(0,1))/s;}
        else if(M(0,0)>M(1,1)&&M(0,0)>M(2,2)) {var s=Math.Sqrt(1+M(0,0)-M(1,1)-M(2,2))*2;w=(M(2,1)-M(1,2))/s;x=s/4;y=(M(0,1)+M(1,0))/s;z=(M(0,2)+M(2,0))/s;}
        else if(M(1,1)>M(2,2)) {var s=Math.Sqrt(1+M(1,1)-M(0,0)-M(2,2))*2;w=(M(0,2)-M(2,0))/s;x=(M(0,1)+M(1,0))/s;y=s/4;z=(M(1,2)+M(2,1))/s;}
        else {var s=Math.Sqrt(1+M(2,2)-M(0,0)-M(1,1))*2;w=(M(1,0)-M(0,1))/s;x=(M(0,2)+M(2,0))/s;y=(M(1,2)+M(2,1))/s;z=s/4;}
        if(w<0) {w=-w;x=-x;y=-y;z=-z;}var v=new DesignVector(x,y,z);return v.Length<1e-14?v*2:v*(2*Math.Atan2(v.Length,w)/v.Length);
    }
}
