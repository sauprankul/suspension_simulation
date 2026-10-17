using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SuspensionSimulation.Kinematics;
using SuspensionSimulation.Solvers.HeadToHead;
using SuspensionSimulation.Configuration;
using SuspensionSimulation.Components;

namespace SuspensionSimulation.Solvers.Kinematics;

/// <summary>Connectivity is explicit. Display poses never feed back into equilibrium stiffness.</summary>
public sealed class InspectionSceneBuilder
{
    private readonly Gr86HardPointPackage3D _geometry;
    private readonly SteeringGeometry _steering;
    private readonly TubularArbMechanism3DConfig[] _bars;
    private readonly ImmutableDictionary<string,RigidCornerPose3D> _reference;
    private readonly ImmutableArray<InspectionSegment> _trailing;
    private readonly IReadOnlyDictionary<string,(double Minimum,double Maximum)> _strokeLengths;
    public string PackageFingerprint { get; }
    public double TireRadiusM { get; }
    public double TireWidthM { get; }

    public InspectionSceneBuilder(string directory,SteeringGeometry? steering=null)
    {
        _steering=steering??SteeringGeometry.Load(directory);_steering.Validate();
        var loaded=Gr86HardPointPackageLoader.Load(directory);
        _geometry=loaded with {Front=loaded.Front.ToImmutableDictionary(x=>x.Key,x=>_steering.Apply(x.Value))};
        _bars=[TubularArbMechanism3DLoader.Load(Path.Combine(directory,"front-tubular-arb.json")),
            TubularArbMechanism3DLoader.Load(Path.Combine(directory,"rear-tubular-arb.json"))];
        var solver=new RigidMechanism3DSolver();
        _reference=_geometry.Front.Select(x=>solver.Solve(x.Value,0)).Concat(_geometry.Rear.Select(x=>solver.Solve(x.Value,0))).ToImmutableDictionary(x=>x.Id);
        var strokePath=Path.Combine(directory,"travel-limits.json");
        _strokeLengths=File.Exists(strokePath)?JsonConfigurationLoader.Load<TravelLimitsConfig>(strokePath).Limits
            .SelectMany(x=>x.Corners.Select(c=>new {Corner=c.ToString(),Minimum=_reference[c.ToString()].SpringLengthM-x.CompressionComponentTravelMm/1000,
                Maximum=_reference[c.ToString()].SpringLengthM+x.ReboundComponentTravelMm/1000})).ToDictionary(x=>x.Corner,x=>(x.Minimum,x.Maximum))
            :new Dictionary<string,(double,double)>();
        if(_strokeLengths.Values.Any(x=>x.Minimum<=0||x.Maximum<=x.Minimum)) throw new InvalidDataException("Component stroke requires positive compressed length and maximum > minimum.");
        using var config=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"visualization.json")));
        TireRadiusM=config.RootElement.GetProperty("tireRadiusM").GetDouble();
        TireWidthM=config.RootElement.GetProperty("tireWidthM").GetDouble();
        if(!double.IsFinite(TireRadiusM)||!double.IsFinite(TireWidthM)||TireRadiusM<=0||TireWidthM<=0)
            throw new InvalidDataException("Visual tire radius and width must be finite and positive.");
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(var file in Directory.EnumerateFiles(directory,"*.json").OrderBy(Path.GetFileName,StringComparer.Ordinal))
        {hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(file)+"\n"));hash.AppendData(File.ReadAllBytes(file));}
        hash.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_steering)));
        PackageFingerprint=Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        using var rear=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"rear-double-wishbone-hard-points.json")));
        Vector3 Point(string id,float side)
        {var a=rear.RootElement.GetProperty("left").GetProperty(id);return new(a[0].GetSingle(),side*a[1].GetSingle(),a[2].GetSingle());}
        _trailing=new[]{("RL",1f),("RR",-1f)}.Select(x=>new InspectionSegment(x.Item1+".trailing-reference",
            "Trailing link — not constrained by current solver",x.Item1,"reference",Point("trailingLinkInner",x.Item2),
            Point("trailingLinkOuter",x.Item2),SceneSource.UnconstrainedReference)).ToImmutableArray();
    }

    public InspectionScene BuildReference(double cgHeightM) => Build("static-reference",_reference,
        new(0,0,0,cgHeightM),ImmutableHashSet<string>.Empty);

    public AcceptedInspectionSnapshot CreateSnapshot(long revision,HeadToHeadComparisonResult comparison,
        ModularSetup vehicle,IReadOnlyDictionary<string,RigidCornerTravelMap3D> maps,double solveMilliseconds,double steeringWheelDegrees=0)
    {
        InspectionSideSnapshot Side(HeadToHeadSideResult side,string setup)
        {
            if(!side.State.Converged) throw new InvalidOperationException($"{setup}: equilibrium did not converge.");
            if(side.State.Corners.Any(x=>x.NormalLoadN<=0)) throw new InvalidOperationException($"{setup}: non-positive raw normal load; contact/lift equilibrium is not solved.");
            var rack=_steering.RackDisplacementM(steeringWheelDegrees);
            var poses=side.State.Corners.ToImmutableDictionary(x=>x.Corner,x=>
            {
                var centered=maps[x.Corner].EvaluatePose(x.RelativeCompressionM);
                return _geometry.Front.TryGetValue(x.Corner,out var front)?RackSteeringSolver.Solve(front,centered,rack):centered;
            });
            var bars=_bars.Select((bar,index)=>
            {
                var front=index==0;var left=front?"FL":"RL";var right=front?"FR":"RR";
                var configured=bar with {OutsideDiameterM=front?side.Input.FrontArbOutsideDiameterM:side.Input.RearArbOutsideDiameterM,
                    InsideDiameterM=front?side.Input.FrontArbInsideDiameterM:side.Input.RearArbInsideDiameterM};
                var l=poses[left];var r=poses[right];
                var ld=maps[left].Evaluate(l.WheelJounceM).ArbLowerDerivativePerWheelJounce;
                var rd=maps[right].Evaluate(r.WheelJounceM).ArbLowerDerivativePerWheelJounce;
                return new TubularArbMechanism3DSolver().Evaluate(configured,
                    j=>l.SolvedPoints["arbLinkLower"]+ld*(float)(j-l.WheelJounceM),
                    j=>r.SolvedPoints["arbLinkLower"]+rd*(float)(j-r.WheelJounceM),l.WheelJounceM,r.WheelJounceM);
            }).ToArray();
            var body=new InspectionBodyPose(side.State.HeaveCompressionM,side.State.RollRad,side.State.PitchRad,vehicle.Chassis.CgHeightM);
            var diagnostics=side.Inspection??throw new InvalidOperationException("Missing evaluation/final pose diagnostics.");
            var scene=Build($"revision {revision} · {setup} · final",poses,body,diagnostics.Limits.Where(x=>x.Active).Select(x=>x.Corner).ToImmutableHashSet(),bars,rack);
            var alignment=scene.Wheels.Select(w=>
            {
                var measured=AlignmentMeasurement.Measure(w.Corner,w.Orientation);
                var reference=_geometry.Front.TryGetValue(w.Corner,out var front)?RackSteeringSolver.Solve(front,_reference[w.Corner],rack):_reference[w.Corner];
                var neutralHeading=AlignmentMeasurement.Measure(w.Corner,Rotation(reference.UprightPose.RotationVectorRad)*_steering.WheelReference(w.Corner)).Heading;
                var currentHeading=AlignmentMeasurement.Measure(w.Corner,Rotation(poses[w.Corner].UprightPose.RotationVectorRad)*_steering.WheelReference(w.Corner)).Heading;
                var sign=w.Corner.EndsWith("L")?-1:1;
                return new WheelAlignment(w.Corner,measured.Heading,measured.Camber,sign*measured.Heading,sign*(currentHeading-neutralHeading));
            }).ToImmutableArray();
            return new(side.Input,body,scene,poses,bars.Select(x=>new InspectionBarPose(x.Id,x.Left,x.Right,x.BarTwistRad,x.BarTorqueNm)).ToImmutableArray(),
                side.State.Corners.ToImmutableDictionary(x=>x.Corner,x=>x.NormalLoadN),side.State.RollStiffnessDistributionFront,
                side.State.RollCoupleBiasPercentagePoints,diagnostics)
            {
                Steering=new(steeringWheelDegrees,rack,_steering,alignment,
                    AlignmentMeasurement.Axle(alignment.Single(x=>x.Corner=="FL"),alignment.Single(x=>x.Corner=="FR")),
                    AlignmentMeasurement.Axle(alignment.Single(x=>x.Corner=="RL"),alignment.Single(x=>x.Corner=="RR")))
            };
        }
        return new(revision,PackageFingerprint,comparison.SharedLoadCase,vehicle.Chassis,Side(comparison.A,"A"),Side(comparison.B,"B"),solveMilliseconds);
    }

    public InspectionScene Build(string stateId,IReadOnlyDictionary<string,RigidCornerPose3D> poses,
        InspectionBodyPose body,IReadOnlySet<string> activeCorners,
        IReadOnlyList<TubularArbMechanismState3D>? barStates=null,double rackDisplacementM=0)
    {
        var points=ImmutableArray.CreateBuilder<InspectionPoint>();
        var lines=ImmutableArray.CreateBuilder<InspectionSegment>();
        var wheels=ImmutableArray.CreateBuilder<InspectionWheel>();
        var strokes=ImmutableArray.CreateBuilder<InspectionSpringStroke>();
        var warnings=ImmutableArray.CreateBuilder<string>();
        warnings.Add("Synthetic hard points; tire size/basis estimated; bar supports schematic; separate rear damper mounts unavailable.");
        void Line(string corner,string id,string label,string layer,Vector3 a,Vector3 b,SceneSource source=SceneSource.Solved) =>
            lines.Add(new(corner+"."+id,label,corner,layer,body.ToWorld(a),body.ToWorld(b),source,activeCorners.Contains(corner)));
        void Joint(string corner,string id,Vector3 position,SceneSource source) =>
            points.Add(new(corner+"."+id,id,corner,body.ToWorld(position),source));
        foreach(var (corner,pose) in poses.OrderBy(x=>x.Key))
        {
            if(!pose.Converged || pose.MaximumConstraintResidualM>1e-4) throw new InvalidOperationException($"{corner} invalid rigid pose.");
            var p=pose.SolvedPoints; var front=_geometry.Front.ContainsKey(corner); var layer=front?"front":"rear";
            Vector3 lowerFront,lowerRear,springUpper,tieInner;
            if(front)
            {
                var g=_geometry.Front[corner];lowerFront=g.LowerArmFrontPivot;lowerRear=g.LowerArmRearPivot;
                springUpper=g.SpringUpper;tieInner=g.TieRodInner+new Vector3(0,(float)rackDisplacementM,0);
                Line(corner,"strut","Front strut",layer,g.StrutTopMount,p["strutLower"]);
                Joint(corner,"strutTop",g.StrutTopMount,SceneSource.FixedChassis);
            }
            else
            {
                var g=_geometry.Rear[corner];lowerFront=g.LowerArmFrontPivot;lowerRear=g.LowerArmRearPivot;
                springUpper=g.SpringUpper;tieInner=g.ToeLinkInner;
                Line(corner,"upper-arm-front","Upper wishbone front branch",layer,g.UpperArmFrontPivot,p["upperBallJoint"]);
                Line(corner,"upper-arm-rear","Upper wishbone rear branch",layer,g.UpperArmRearPivot,p["upperBallJoint"]);
                Line(corner,"upper-axis","Upper arm pivot axis","chassis",g.UpperArmFrontPivot,g.UpperArmRearPivot,SceneSource.FixedChassis);
                Joint(corner,"upperFrontPivot",g.UpperArmFrontPivot,SceneSource.FixedChassis);
                Joint(corner,"upperRearPivot",g.UpperArmRearPivot,SceneSource.FixedChassis);
            }
            Line(corner,"lower-arm-front","Lower arm front branch",layer,lowerFront,p["lowerBallJoint"]);
            Line(corner,"lower-arm-rear","Lower arm rear branch",layer,lowerRear,p["lowerBallJoint"]);
            Line(corner,"lower-axis","Lower arm pivot axis","chassis",lowerFront,lowerRear,SceneSource.FixedChassis);
            Line(corner,"upright","Upright",layer,p["lowerBallJoint"],p["upperBallJoint"]);
            Line(corner,"hub","Wheel hub",layer,p["lowerBallJoint"],p["wheelCenter"]);
            Line(corner,"steering",front?"Steering tie rod":"Rear toe link",layer,tieInner,p["tieRodOuter"]);
            Line(corner,"spring","Spring axis",layer,springUpper,p["springLower"]);
            if(_strokeLengths.TryGetValue(corner,out var stroke))
            {
                var lower=p["springLower"];var length=Vector3.Distance(springUpper,lower);
                if(length<stroke.Minimum-1e-5 || length>stroke.Maximum+1e-5)
                    throw new InvalidOperationException($"{corner}: steered spring-axis pose exceeds its rigid component stroke. Change steering or load to regain a feasible pose.");
                var bump=Math.Max(0,length-stroke.Minimum);var rebound=Math.Max(0,stroke.Maximum-length);
                // Pose reconstruction and Vector3 introduce micron-scale differences
                // from the interpolated stroke intersection. Close the marker at contact.
                if(bump<1e-5) bump=0;if(rebound<1e-5) rebound=0;
                var marker=springUpper+Vector3.Normalize(lower-springUpper)*(float)bump;
                Joint(corner,"stroke-marker",marker,SceneSource.Solved);
                strokes.Add(new(corner,body.ToWorld(springUpper),body.ToWorld(lower),body.ToWorld(marker),length,stroke.Minimum,stroke.Maximum,bump,rebound));
            }
            Joint(corner,"lowerFrontPivot",lowerFront,SceneSource.FixedChassis);Joint(corner,"lowerRearPivot",lowerRear,SceneSource.FixedChassis);
            Joint(corner,"springUpper",springUpper,SceneSource.FixedChassis);Joint(corner,"tieInner",tieInner,SceneSource.FixedChassis);
            foreach(var (name,point) in p.Where(x=>x.Key!="strutLower"||front)) Joint(corner,name,point,SceneSource.Solved);
            var rotation=Rotation(pose.UprightPose.RotationVectorRad);
            var orientation=Quaternion.Normalize(body.Rotation*rotation*_steering.WheelReference(corner)); var center=body.ToWorld(p["wheelCenter"]);
            var axle=Vector3.Transform(Vector3.UnitY,orientation);
            var radialZ=TireRadiusM*Math.Sqrt(Math.Max(0,1-axle.Z*axle.Z));
            var gap=center.Z-radialZ-TireWidthM*.5*Math.Abs(axle.Z);
            wheels.Add(new(corner,center,orientation,TireRadiusM,TireWidthM,gap,SceneSource.VisualEstimate));
            var refWheel=_reference[corner].SolvedPoints["wheelCenter"];
            var contact=new Vector3(refWheel.X,refWheel.Y,0);
            points.Add(new(corner+".ideal-contact","Ideal load-application contact",corner,contact,SceneSource.VisualEstimate));
        }
        var rackLeft=_geometry.Front["FL"].TieRodInner;var rackRight=_geometry.Front["FR"].TieRodInner;
        var rackMotion=new Vector3(0,(float)rackDisplacementM,0);
        Line("rack","bar","Steering rack","front",rackRight+rackMotion,rackLeft+rackMotion);
        Line("rack","housing","Rack housing axis","chassis",rackRight-new Vector3(0,.06f,0),rackLeft+new Vector3(0,.06f,0),SceneSource.VisualEstimate);
        for(var i=0;i<2;i++)
        {
            var bar=_bars[i];var prefix=i==0?"front-bar":"rear-bar";
            var left=i==0?"FL":"RL";var right=i==0?"FR":"RR";
            var state=barStates?[i] ?? new TubularArbMechanism3DSolver().Evaluate(bar,
                _=>poses[left].SolvedPoints["arbLinkLower"],_=>poses[right].SolvedPoints["arbLinkLower"],0,0);
            if(state.Validity=="invalid") throw new InvalidOperationException($"{prefix}: fixed endlinks cannot reach this pose (residual {Math.Max(state.Left.ConstraintResidualM,state.Right.ConstraintResidualM)*1000:0.00} mm). Reduce steering/rack travel or revise the synthetic mounts.");
            Line(prefix,"axis","Torsion axis","bars",bar.AxisStart,bar.AxisEnd,SceneSource.FixedChassis);
            foreach(var (corner,root,side) in new[]{(left,bar.AxisEnd,state.Left),(right,bar.AxisStart,state.Right)})
            {
                Line(corner,"bar-arm","ARB arm","bars",root,side.ArmEnd);
                Line(corner,"endlink","ARB fixed-length endlink","bars",side.ArmEnd,side.LowerJoint);
                Line(corner,"bar-support","Schematic bar support","bars",root-new Vector3(0,0,.04f),root+new Vector3(0,0,.04f),SceneSource.VisualEstimate);
                Joint(corner,"endlinkUpper",side.ArmEnd,SceneSource.Solved);
            }
        }
        foreach(var link in _trailing) lines.Add(link with {StartM=body.ToWorld(link.StartM),EndM=body.ToWorld(link.EndM)});
        var mounts=points.Where(p=>p.Source==SceneSource.FixedChassis && p.Id.EndsWith("lowerFrontPivot")).OrderBy(p=>p.Corner).ToArray();
        foreach(var side in new[]{new[]{"FL","RL"},new[]{"FR","RR"},new[]{"FL","FR"},new[]{"RL","RR"}})
            lines.Add(new("chassis."+string.Join("-",side),"Schematic chassis connection","","chassis",mounts.Single(x=>x.Corner==side[0]).PositionM,mounts.Single(x=>x.Corner==side[1]).PositionM,SceneSource.VisualEstimate));
        points.Add(new("cg","CG","",body.ToWorld(new(0,0,(float)body.CgHeightM)),SceneSource.VisualEstimate));
        return new(stateId,PackageFingerprint,"synthetic-geometry / provisional-equilibrium",points.ToImmutable(),lines.ToImmutable(),wheels.ToImmutable(),warnings.ToImmutable()) {SpringStrokes=strokes.ToImmutable()};
    }
    public static Quaternion Rotation(Vector3 rotationVector)
    {
        var angle=rotationVector.Length();return angle<1e-12f?Quaternion.Identity:Quaternion.CreateFromAxisAngle(rotationVector/angle,angle);
    }
}
