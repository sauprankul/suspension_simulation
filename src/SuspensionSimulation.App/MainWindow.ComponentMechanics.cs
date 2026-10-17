using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.App;

internal sealed record ComponentPhysicsRecovery(Dictionary<string,string> Fields,DamperFamily Family,bool Seats,bool Perch,bool Body,bool Mass,string[]? PathInputs=null,int PathKind=0,int PathAxis=0,bool Follow=false);
public partial class MainWindow
{
    private readonly Dictionary<string,TextBox> _physicalFields=new();
    private readonly Dictionary<string,TextBlock> _physicalLabels=new();
    private readonly TextBlock _damperFamilyLabel=new(){Text="Damper family"};
    private readonly ComboBox _damperFamily=new(){ItemsSource=Enum.GetValues<DamperFamily>(),SelectedIndex=1};
    private readonly CheckBox _integratedSeats=new(){Content="Integrated spring seats"},_perchAdjustment=new(){Content="Threaded spring perch"},_bodyAdjustment=new(){Content="Separate threaded body length"};
    private readonly CheckBox _useMass=new(){Content="Specify this component's mass"};
    private readonly TextBox _adjustPointIds=new(),_adjustFrameIds=new();
    private readonly ComboBox _adjustKind=new(){ItemsSource=new[]{"Straight slot","Threaded endpoint","Circular arc","Eccentric","Mounting holes","Measured path","Fixed mount"},SelectedIndex=0};
    private readonly ComboBox _adjustAxis=new(){ItemsSource=new[]{"X","Y","Z","From component origin to target"},SelectedIndex=0};
    private readonly TextBox _adjustRadius=new(){Text="10"},_adjustStart=new(){Text="-20"},_adjustEnd=new(){Text="20"},_adjustStations=new();
    private readonly CheckBox _adjustFollow=new(){Content="Rotate orientation along the path"};
    private readonly TextBox _adjustRange=new(){Text="20"};
    private void AddComponentMechanics(TabControl tabs)
    {
        var panel=new StackPanel {Margin=new Thickness(10)};
        void Field(string id,string label,string value)
        {var caption=new TextBlock {Text=label,TextWrapping=TextWrapping.Wrap};_physicalLabels.Add(id,caption);panel.Children.Add(caption);var box=new TextBox {Text=value};_physicalFields.Add(id,box);box.TextChanged+=(_,_)=>ScheduleComponentDraft();panel.Children.Add(box);}
        panel.Children.Add(new TextBlock {Text="Values apply to the selected component type. Spring and damper dimensions are owned here; Garage only selects compatible hardware and its declared adjustments.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
        Field("rate","Spring rate (kgf/mm)","3");Field("free","Spring free length (mm)","300");Field("block","Spring fully bound length (mm)","100");
        panel.Children.Add(_damperFamilyLabel);panel.Children.Add(_damperFamily);Field("minimum","Minimum eye length (mm)","350");Field("maximum","Maximum eye length (mm)","500");
        Field("compression","Compression damping (N·s/m)","1000");Field("rebound","Rebound damping (N·s/m)","2000");Field("gas","Explicit gas extension force (N)","0");
        panel.Children.Add(_integratedSeats);Field("upperSeat","Upper seat inset from eye (mm)","30");Field("lowerSeat","Lower seat inset from eye (mm)","70");panel.Children.Add(_perchAdjustment);panel.Children.Add(_bodyAdjustment);
        Field("perchMin","Perch minimum relative offset (mm)","-20");Field("perchMax","Perch maximum relative offset (mm)","20");Field("bodyMin","Body minimum relative length offset (mm)","-15");Field("bodyMax","Body maximum relative length offset (mm)","15");
        Field("shear","Bar shear modulus (GPa)","79");Field("outside","Torsion section OD (mm)","18");Field("inside","Torsion section ID (mm)","0");Field("torsionLength","Straight torsion section length (mm)","1100");Field("restTwist","Rest twist (degrees)","0");
        Field("effectiveBar","Effective torsional rate (N·m/rad; blank uses GJ/L)","");
        Field("rackTravel","Rack travel at full steering-wheel lock (mm)","55");Field("rackLock","Steering-wheel lock angle (degrees)","450");
        Field("wheelRadius","Effective rigid rolling radius (mm)","310");Field("wheelWidth","Wheel/tire visual width (mm)","220");
        panel.Children.Add(_useMass);Field("mass","Mass (kg)","1");Field("massX","Mass datum X (mm)","0");Field("massY","Mass datum Y (mm)","0");Field("massZ","Mass datum Z (mm)","0");
        foreach(var check in new[]{_integratedSeats,_perchAdjustment,_bodyAdjustment,_useMass}) {check.Checked+=(_,_)=>{ScheduleComponentDraft();UpdateMechanicalVisibility();};check.Unchecked+=(_,_)=>{ScheduleComponentDraft();UpdateMechanicalVisibility();};}
        _perchAdjustment.Checked+=(_,_)=>_integratedSeats.IsChecked=true;
        _componentKind.SelectionChanged+=(_,_)=>UpdateMechanicalVisibility();UpdateMechanicalVisibility();
        tabs.Items.Add(new TabItem {Header="Mechanics",Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
        var adjustment=new StackPanel {Margin=new Thickness(10)};adjustment.Children.Add(new TextBlock {Text="Declared geometry adjustment",FontWeight=FontWeights.Bold});adjustment.Children.Add(_adjustKind);
        adjustment.Children.Add(new TextBlock {Text="Affected point IDs (comma separated)"});adjustment.Children.Add(_adjustPointIds);adjustment.Children.Add(new TextBlock {Text="Affected frame IDs (comma separated)"});adjustment.Children.Add(_adjustFrameIds);
        adjustment.Children.Add(new TextBlock {Text="Total travel / adjustment range (mm)"});adjustment.Children.Add(_adjustRange);
        adjustment.Children.Add(new TextBlock {Text="Travel / rotation axis in component coordinates"});adjustment.Children.Add(_adjustAxis);
        foreach(var (label,box) in new[]{("Arc / eccentric radius (mm)",_adjustRadius),("Start angle (degrees)",_adjustStart),("End angle (degrees)",_adjustEnd),("Hole or measured-path frame IDs, in order",_adjustStations)}) {adjustment.Children.Add(new TextBlock {Text=label,TextWrapping=TextWrapping.Wrap});adjustment.Children.Add(box);box.TextChanged+=(_,_)=>ScheduleComponentDraft();}
        adjustment.Children.Add(_adjustFollow);
        var create=new Button {Content="Add declared adjustment",Margin=new Thickness(0,8,0,8)};create.Click+=(_,_)=>ComponentAction(()=>
        {
            ApplyComponentPreview(true);var component=_componentDefinition!;var range=DesignerNumbers.Metres(_adjustRange.Text);if(range<=0) throw new ArgumentException("Adjustment range must be positive.");
            var points=_adjustPointIds.Text.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);var frames=_adjustFrameIds.Text.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
            if(points.Length+frames.Length==0) throw new ArgumentException("Choose at least one affected point or frame.");
            var frame=frames.Length>0?component.Frames.Single(f=>f.Id==frames[0]).Frame:AssemblyAuthoring.IdentityPlacement.Frame with {OriginM=component.Points.Single(p=>p.Id==points[0]).PositionM};
            var axis=_adjustAxis.SelectedIndex switch {0=>new DesignVector(1,0,0),1=>new DesignVector(0,1,0),2=>new DesignVector(0,0,1),_=>frame.OriginM.Length>1e-9?frame.OriginM/frame.OriginM.Length:throw new ArgumentException("The target is at the component origin; choose an explicit axis.")};
            var reference=Math.Abs(axis.X)<.9?new DesignVector(1,0,0):new DesignVector(0,1,0);reference-=axis*reference.Dot(axis);reference/=reference.Length;var orientation=_adjustFollow.IsChecked==true?PathOrientation.FollowPath:PathOrientation.HoldStart;
            AdjustmentPath path;var setting=new AdjustmentSetting(.5);
            switch(_adjustKind.SelectedIndex)
            {
                case 0:path=new StraightAdjustmentPath(frame with {OriginM=frame.OriginM-axis*(range/2)},frame with {OriginM=frame.OriginM+axis*(range/2)},orientation);break;
                case 1:path=new ThreadedAdjustmentPath(new(frame.OriginM,axis,reference),0,range/2,range);break;
                case 2:case 3:
                {
                    var radius=DesignerNumbers.Metres(_adjustRadius.Text);var a=DesignerNumbers.Number(_adjustStart.Text)*Math.PI/180;var b=DesignerNumbers.Number(_adjustEnd.Text)*Math.PI/180;
                    if(a>0||b<0||b<=a||radius<=0) throw new ArgumentException("Use a positive radius and an angle range containing the nominal zero angle.");
                    var center=frame.OriginM-reference*radius;setting=new(-a/(b-a));
                    var start=frame with {OriginM=center+DesignTransform.Rotate(frame.OriginM-center,axis*a),Axis=orientation==PathOrientation.FollowPath?DesignTransform.Rotate(frame.Axis,axis*a):frame.Axis,ReferenceDirection=orientation==PathOrientation.FollowPath?DesignTransform.Rotate(frame.ReferenceDirection,axis*a):frame.ReferenceDirection};
                    path=_adjustKind.SelectedIndex==2?new ArcAdjustmentPath(center,axis,start,b-a,orientation):new EccentricAdjustmentPath(center,axis,frame,a,b,orientation);break;
                }
                case 4:
                {
                    var stations=_adjustStations.Text.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Select(id=>component.Frames.Single(f=>f.Id==id)).ToArray();
                    var nominal=stations.FirstOrDefault(f=>(f.Frame.OriginM-frame.OriginM).Length<1e-8)??throw new ArgumentException("One named hole frame must match the current mount position.");
                    path=new DiscreteAdjustmentPath([..stations.Select(f=>new AdjustmentHole(f.Id,f.Name,f.Frame))]);setting=new(ChoiceId:nominal.Id);break;
                }
                case 5:
                {
                    var stations=_adjustStations.Text.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Select(id=>component.Frames.Single(f=>f.Id==id).Frame).ToArray();
                    if(stations.Length<2) throw new ArgumentException("Choose at least two measured station frames.");
                    var lengths=stations.Skip(1).Select((s,i)=>(s.OriginM-stations[i].OriginM).Length).ToArray();var total=lengths.Sum();var accumulated=0d;double? distance=null;
                    for(var i=0;i<lengths.Length;i++) {var delta=stations[i+1].OriginM-stations[i].OriginM;var t=(frame.OriginM-stations[i].OriginM).Dot(delta)/(lengths[i]*lengths[i]);if(t>=0&&t<=1&&(stations[i].OriginM+delta*t-frame.OriginM).Length<1e-8) distance=accumulated+t*lengths[i];accumulated+=lengths[i];}
                    if(distance is null) throw new ArgumentException("The current mount must lie on the measured path.");
                    path=new MeasuredAdjustmentPath([..stations],orientation);setting=new(distance.Value/total);break;
                }
                default:path=new FixedAdjustmentPath(frame);setting=new(0);break;
            }
            var spec=new GeometryAdjustment("adjust-"+Guid.NewGuid().ToString("N")[..6],_adjustKind.SelectedItem!.ToString()!,path,setting,[..points],[..frames],"Declared hardware adjustment; locked before operating motion.");
            var next=component with {Adjustments=(component.Adjustments??[]).Add(spec)};ComponentAuthoring.ValidateForPublish(next);_componentMetadataSeed=next;_componentDefinition=next;ScheduleComponentDraft();_componentStatus.Text="Adjustment added. Publish to make it available to assemblies.";
        });adjustment.Children.Add(create);adjustment.Children.Add(new TextBlock {Text="Hole positions and measured stations use the exact named frames from the Frames tab. A slot is straight; an arc or eccentric needs its own radius and angle range. Frame and point IDs stay stable across revisions.",TextWrapping=TextWrapping.Wrap});
        tabs.Items.Add(new TabItem {Header="Adjustments",Content=adjustment});
    }
    private double Physical(string id)=>DesignerNumbers.Number(_physicalFields[id].Text);
    private void UpdateMechanicalVisibility()
    {
        var kind=_componentKind.SelectedItem is ComponentKind selected?selected:ComponentKind.RigidLink;
        foreach(var (id,field) in _physicalFields)
        {
            var visible=id switch
            {
                "rate" or "free" or "block"=>kind==ComponentKind.Spring,
                "minimum" or "maximum" or "compression" or "rebound" or "gas"=>kind==ComponentKind.Damper,
                "upperSeat" or "lowerSeat"=>kind==ComponentKind.Damper&&_integratedSeats.IsChecked==true,
                "perchMin" or "perchMax"=>kind==ComponentKind.Damper&&_perchAdjustment.IsChecked==true,
                "bodyMin" or "bodyMax"=>kind==ComponentKind.Damper&&_bodyAdjustment.IsChecked==true,
                "shear" or "outside" or "inside" or "torsionLength" or "restTwist" or "effectiveBar"=>kind==ComponentKind.AntiRollBar,
                "rackTravel" or "rackLock"=>kind==ComponentKind.Rack,
                "wheelRadius" or "wheelWidth"=>kind==ComponentKind.Wheel,
                _=>_useMass.IsChecked==true
            };
            field.Visibility=_physicalLabels[id].Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        }
        _damperFamily.Visibility=_damperFamilyLabel.Visibility=_integratedSeats.Visibility=_bodyAdjustment.Visibility=kind==ComponentKind.Damper?Visibility.Visible:Visibility.Collapsed;
        _perchAdjustment.Visibility=kind==ComponentKind.Damper&&_integratedSeats.IsChecked==true?Visibility.Visible:Visibility.Collapsed;
    }
    private double PhysicalMetres(string id)=>Physical(id)/1000;
    private ComponentMass? ReadComponentMass()=>_useMass.IsChecked==true?new(Physical("mass"),new(PhysicalMetres("massX"),PhysicalMetres("massY"),PhysicalMetres("massZ"))):null;
    private ComponentDefinition CreateElasticComponent(ComponentKind kind)
    {
        var identity=ComponentIdentity();
        if(kind==ComponentKind.Spring) return ElasticComponentFactory.Spring(identity,Physical("rate"),PhysicalMetres("free"),PhysicalMetres("block"));
        if(kind==ComponentKind.AntiRollBar) return ElasticComponentFactory.Bar(identity,new("first","second",new(0,1,0),Physical("shear")*1e9,PhysicalMetres("outside"),PhysicalMetres("inside"),PhysicalMetres("torsionLength"),Physical("restTwist")*Math.PI/180,string.IsNullOrWhiteSpace(_physicalFields["effectiveBar"].Text)?null:Physical("effectiveBar")));
        if(kind==ComponentKind.Damper)
        {
            var adjustments=new List<GeometryAdjustment>();var frame=AssemblyAuthoring.IdentityPlacement.Frame;
            void Thread(string id,string name,string minField,string maxField)
            {var min=PhysicalMetres(minField);var max=PhysicalMetres(maxField);if(min>0||max<0||max<=min) throw new ArgumentException("Threaded offset range must include zero and have positive travel.");adjustments.Add(new(id,name,new ThreadedAdjustmentPath(frame,0,-min,max-min),new(-min/(max-min)),[],[],"Physical threaded setting relative to the entered reference; locked before solving."));}
            if(_perchAdjustment.IsChecked==true) Thread("perch","Spring perch","perchMin","perchMax");if(_bodyAdjustment.IsChecked==true) Thread("body-length","Damper body length","bodyMin","bodyMax");
            return ElasticComponentFactory.Damper(identity,new("first","second",(DamperFamily)_damperFamily.SelectedItem,PhysicalMetres("minimum"),PhysicalMetres("maximum"),Physical("compression"),Physical("rebound"),Physical("gas"),
                _integratedSeats.IsChecked==true?new(PhysicalMetres("upperSeat"),PhysicalMetres("lowerSeat"),_perchAdjustment.IsChecked==true?"perch":null):null,_bodyAdjustment.IsChecked==true?"body-length":null),adjustments.Count>0?[..adjustments]:null);
        }
        throw new ArgumentException("This component type has no parametric elastic template yet.");
    }
    private void FillComponentMechanics(ComponentDefinition component)
    {
        void Set(string id,double value)=>_physicalFields[id].Text=DesignerNumbers.Text(value);
        _useMass.IsChecked=component.Mass is not null;if(component.Mass is {} mass) {Set("mass",mass.MassKg);Set("massX",mass.CenterOfMassM.X*1000);Set("massY",mass.CenterOfMassM.Y*1000);Set("massZ",mass.CenterOfMassM.Z*1000);}
        switch(component.Behavior)
        {
            case CompressionSpringBehavior s:Set("rate",s.RateKgfPerMm);Set("free",s.FreeLengthM*1000);Set("block",s.BlockLengthM*1000);break;
            case AxialDamperBehavior d:
                _damperFamily.SelectedItem=d.Family;Set("minimum",d.MinimumEyeLengthM*1000);Set("maximum",d.MaximumEyeLengthM*1000);Set("compression",d.CompressionNsPerM);Set("rebound",d.ReboundNsPerM);Set("gas",d.GasExtensionForceN);_integratedSeats.IsChecked=d.SpringSeats is not null;_perchAdjustment.IsChecked=d.SpringSeats?.PerchAdjustmentId is not null;_bodyAdjustment.IsChecked=d.BodyLengthAdjustmentId is not null;
                if(d.SpringSeats is {} seats) {Set("upperSeat",seats.UpperInsetM*1000);Set("lowerSeat",seats.LowerInsetM*1000);}break;
            case TorsionBarBehavior b:Set("shear",b.ShearModulusPa/1e9);Set("outside",b.OutsideDiameterM*1000);Set("inside",b.InsideDiameterM*1000);Set("torsionLength",b.TorsionLengthM*1000);Set("restTwist",b.RestTwistRad*180/Math.PI);_physicalFields["effectiveBar"].Text=b.PrescribedRateNmPerRad is {} rate?DesignerNumbers.Text(rate):"";break;
        }
        if(component.Rack is {} rack) {Set("rackTravel",rack.TravelAtLockM*1000);Set("rackLock",rack.SteeringWheelLockDegrees);}
        if(component.Wheel is {} wheel) {Set("wheelRadius",wheel.RollingRadiusM*1000);Set("wheelWidth",wheel.WidthM*1000);}
        if(component.Behavior is AxialDamperBehavior damper)
            foreach(var (id,minField,maxField) in new[]{(damper.SpringSeats?.PerchAdjustmentId,"perchMin","perchMax"),(damper.BodyLengthAdjustmentId,"bodyMin","bodyMax")})
                if(id is not null&&(component.Adjustments??[]).Single(a=>a.Id==id) is {} definition&&definition.Path is ThreadedAdjustmentPath t)
                {var nominal=AdjustmentPaths.Evaluate(t,definition.Default);var a=AdjustmentPaths.Evaluate(t,new(0));var b=AdjustmentPaths.Evaluate(t,new(1));Set(minField,(a.OriginM-nominal.OriginM).Dot(t.Datum.Axis)*1000);Set(maxField,(b.OriginM-nominal.OriginM).Dot(t.Datum.Axis)*1000);}
    }
    private ComponentDefinition ApplyComponentMechanicalEdits(ComponentDefinition component)
    {
        if(component.Kind==ComponentKind.Rack) component=component with {Rack=new(PhysicalMetres("rackTravel"),Physical("rackLock"))};
        if(component.Kind==ComponentKind.Wheel)
        {
            var radius=PhysicalMetres("wheelRadius");component=component with {Wheel=new(radius,PhysicalMetres("wheelWidth"))};
            var center=component.Points.SingleOrDefault(p=>p.Id=="center");var marker=component.Points.SingleOrDefault(p=>p.Id=="radius");
            if(center is not null&&marker is not null) {var direction=marker.PositionM-center.PositionM;if(direction.Length>1e-9) component=component with {Points=component.Points.Replace(marker,marker with {PositionM=center.PositionM+direction/direction.Length*radius})};}
        }
        if(component.Kind is ComponentKind.Spring or ComponentKind.Damper or ComponentKind.AntiRollBar)
        {
            var generated=CreateElasticComponent(component.Kind);var behavior=generated.Behavior!;
            if(component.Behavior is {} existing) behavior=behavior with {FirstPortId=existing.FirstPortId,SecondPortId=existing.SecondPortId};
            var adjustments=generated.Adjustments;
            if(behavior is AxialDamperBehavior d&&component.Behavior is AxialDamperBehavior old)
            {
                var perch=d.SpringSeats?.PerchAdjustmentId is null?null:old.SpringSeats?.PerchAdjustmentId??"perch";var body=d.BodyLengthAdjustmentId is null?null:old.BodyLengthAdjustmentId??"body-length";
                behavior=d with {SpringSeats=d.SpringSeats is {} seats?seats with {PerchAdjustmentId=perch}:null,BodyLengthAdjustmentId=body};
                var list=new List<GeometryAdjustment>();
                if(perch is not null) list.Add((generated.Adjustments??[]).Single(a=>a.Id=="perch") with {Id=perch});if(body is not null) list.Add((generated.Adjustments??[]).Single(a=>a.Id=="body-length") with {Id=body});
                adjustments=list.Count==0?null:[..list];
            }
            if(behavior is TorsionBarBehavior b&&component.Behavior is TorsionBarBehavior originalBar) behavior=b with {UnitAxis=originalBar.UnitAxis};
            component=component with {Behavior=behavior,Adjustments=adjustments};
        }
        return component;
    }
    private ComponentPhysicsRecovery CaptureComponentPhysics()=>new(_physicalFields.ToDictionary(x=>x.Key,x=>x.Value.Text),(DamperFamily)_damperFamily.SelectedItem,_integratedSeats.IsChecked==true,_perchAdjustment.IsChecked==true,_bodyAdjustment.IsChecked==true,_useMass.IsChecked==true,
        new[]{_adjustPointIds.Text,_adjustFrameIds.Text,_adjustRange.Text,_adjustRadius.Text,_adjustStart.Text,_adjustEnd.Text,_adjustStations.Text},_adjustKind.SelectedIndex,_adjustAxis.SelectedIndex,_adjustFollow.IsChecked==true);
    private void RestoreComponentPhysics(ComponentPhysicsRecovery? state)
    {
        if(state is null) {if(_componentMetadataSeed is {} component) FillComponentMechanics(component);return;}
        foreach(var field in state.Fields) if(_physicalFields.TryGetValue(field.Key,out var box)) box.Text=field.Value;
        _damperFamily.SelectedItem=state.Family;_integratedSeats.IsChecked=state.Seats;_perchAdjustment.IsChecked=state.Perch;_bodyAdjustment.IsChecked=state.Body;_useMass.IsChecked=state.Mass;
        if(state.PathInputs is {Length:7} values) {var boxes=new[]{_adjustPointIds,_adjustFrameIds,_adjustRange,_adjustRadius,_adjustStart,_adjustEnd,_adjustStations};for(var i=0;i<boxes.Length;i++) boxes[i].Text=values[i];}
        _adjustKind.SelectedIndex=state.PathKind;_adjustAxis.SelectedIndex=state.PathAxis;_adjustFollow.IsChecked=state.Follow;
    }
}
