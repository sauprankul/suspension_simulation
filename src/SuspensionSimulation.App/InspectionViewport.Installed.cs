using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SuspensionSimulation.Kinematics;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.App;

internal sealed partial class InspectionViewport
{
    private InstalledAnalysisSide? _installedSide;
    internal void SetInstalledState(InstalledAnalysisSide side,InspectionScene scene,double milliseconds)
    {
        _side=null;_installedSide=side;SetScene(scene);if(!_sceneFitted) Fit();_travel.Children.Clear();
        var state=side.State;
        for(var i=0;i<4;i++)
        {
            var wheel=state.Wheels[i];var blocks=_readouts[wheel.Corner];
            blocks[0].Text=state.NormalLoadsN[i].ToString("0");blocks[1].Text=(state.Motion.Travel.Values[i]*1000).ToString("+0.00;-0.00;0.00");
            blocks[2].Text=SignedDegrees(wheel.CamberRad);blocks[3].Text=SignedDegrees(wheel.HeadingRad);blocks[4].Text=SignedDegrees(wheel.HeadingRad-side.SteeringReference[i].HeadingRad);
            blocks[4].ToolTip="Heading change from the gravity pose at this same steering angle; includes the solved loaded chassis orientation.";
            var stroke=scene.SpringStrokes.Where(s=>s.Corner.StartsWith(wheel.Corner+"-",StringComparison.Ordinal)).ToArray();
            var bump=stroke.Select(s=>s.BumpRemainingM).DefaultIfEmpty(0).Min();var rebound=stroke.Where(s=>s.Corner.EndsWith("-damper",StringComparison.Ordinal)).Select(s=>s.ReboundRemainingM).DefaultIfEmpty(0).Min();
            var reaction=state.RigidReactions.FirstOrDefault(r=>r.ForceN>1e-5&&r.Id.StartsWith(wheel.Corner+"-",StringComparison.Ordinal));
            var lifted=state.NormalLoadsN[i]<1e-5;var near=Math.Min(bump,rebound)<.01;
            var color=lifted||reaction is not null?Brushes.Tomato:near?Brushes.Goldenrod:Brushes.LightSteelBlue;
            var panel=new StackPanel();var status=lifted?"LIFTED":reaction?.Id.EndsWith(":top",StringComparison.Ordinal)==true?"TOP-OUT":reaction?.Id.EndsWith(":coil-bind",StringComparison.Ordinal)==true?"COIL BIND":reaction is not null?"BOTTOM-OUT":near?"NEAR LIMIT":"IN TRAVEL";
            var select=new Button {Content=wheel.Corner+" · "+status,Foreground=color,Padding=new Thickness(1,0,1,0),Height=18,FontSize=10};var selectedId=wheel.Corner+"-damper.damper";
            select.Click+=(_,_)=>Select(selectedId);panel.Children.Add(select);panel.Children.Add(new TextBlock {Text=$"B {bump*1000:0.0} / R {rebound*1000:0.0} mm",Foreground=color,FontSize=10,ToolTip="Remaining physical spring-bind/damper stroke. Rigid-limit loading is arrested; additional force changes its reaction."});_travel.Children.Add(panel);
        }
        void Angle(string name,double radians)=>_axleReadouts[name].Text=SignedDegrees(radians)+"°";
        for(var axle=0;axle<2;axle++)
        {
            var first=axle*2;var name=axle==0?"Front":"Rear";var a=state.Wheels[first];var b=state.Wheels[first+1];
            Angle(name+" total toe",b.HeadingRad-a.HeadingRad);Angle(name+" thrust",Math.Atan2(Math.Sin(a.HeadingRad)+Math.Sin(b.HeadingRad),Math.Cos(a.HeadingRad)+Math.Cos(b.HeadingRad)));Angle(name+" mean camber",(a.CamberRad+b.CamberRad)/2);
            _axleReadouts[name+" total toe"].ToolTip="Positive = toe-in: right wheel heading minus left wheel heading, including Ackermann while steering.";
        }
        var rack=side.Source.Components.Single(c=>c.Kind==SuspensionSimulation.Design.ComponentKind.Rack).Rack!;
        _axleReadouts["Rack travel"].Text=(-state.Input.SteeringWheelDegrees/rack.SteeringWheelLockDegrees*rack.TravelAtLockM*1000).ToString("+0.00;-0.00;0.00")+" mm";
        var mean=Math.Atan2(Math.Sin(state.Wheels[0].HeadingRad)+Math.Sin(state.Wheels[1].HeadingRad),Math.Cos(state.Wheels[0].HeadingRad)+Math.Cos(state.Wheels[1].HeadingRad))*180/Math.PI;
        _axleReadouts["Steering ratio"].Text=Math.Abs(mean)<.001?"—":Math.Abs(state.Input.SteeringWheelDegrees/mean).ToString("0.0")+":1";
        _status.Text=$"Accepted Garage revision {side.Entry.Revision} · installed equilibrium · estimated geometry";
        _technical.Text=$"{Adapter} · physical solve {milliseconds:0.0} ms · {state.Iterations} iterations / {state.PoseEvaluations} poses\nForce residual {state.ForceResidualN.Length:0.###E+0} N · moment residual {state.MomentResidualNm.Length:0.###E+0} N·m\nSource/setup SHA-256 {side.SetupSha256}\n"+string.Join("\n",state.RigidReactions.Select(r=>$"{r.Id}: {r.ForceN:0.0} N reaction · gap {r.GapM:E2} m"));
        ShowSelection();
    }
    private static string SignedDegrees(double radians)=>(radians*180/Math.PI).ToString("+0.00;-0.00;0.00");
}
