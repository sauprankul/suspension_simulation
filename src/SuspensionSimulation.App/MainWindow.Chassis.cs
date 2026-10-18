using System.Windows;
using System.Windows.Controls;
using SuspensionSimulation.Design;
using SuspensionSimulation.Solvers.Design;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    private void AddChassisFields(Action<string,string,string> field)
    {
        field("chassisLength","Chassis axle-reference spacing (mm)","2575");field("chassisFrontX","Front axle reference X (mm)","1210.25");field("chassisFrontOverhang","Front overhang beyond axle (mm)","800");field("chassisRearOverhang","Rear overhang beyond axle (mm)","890");
        field("chassisHalfWidth","Rail half spacing (mm)","500");field("chassisRailHeight","Rail height (mm)","180");field("chassisRackSetback","Rack crossmember behind front axle (mm)","160");field("chassisRackHeight","Rack crossmember height (mm)","290");
        field("chassisVerticalEI","Longitudinal / vertical-plane bending rigidity EI_y (MN·m²)","4.62");field("chassisLateralEI","Lateral-plane bending rigidity EI_z (MN·m²)","6");field("chassisTorsionGJ","Long-axis torsional rigidity GJ (MN·m²)","2.95");
    }
    private ComponentDefinition ReadChassisComponent()
    {
        var s=new ChassisSpecification(PhysicalMetres("chassisLength"),PhysicalMetres("chassisFrontX"),PhysicalMetres("chassisFrontOverhang"),PhysicalMetres("chassisRearOverhang"),PhysicalMetres("chassisHalfWidth"),PhysicalMetres("chassisRailHeight"),PhysicalMetres("chassisRackSetback"),PhysicalMetres("chassisRackHeight"),Physical("chassisVerticalEI")*1e6,Physical("chassisLateralEI")*1e6,Physical("chassisTorsionGJ")*1e6);
        var component=ChassisAuthoring.Create(ComponentIdentity(),s,_componentMass);
        if(_componentParameters.Count>0||_componentAdjustmentBindings.Any(b=>!string.IsNullOrWhiteSpace(b.ParameterId))) throw new ArgumentException("Chassis length and all three rigidities are owner properties; importing assemblies and Garage cannot override them.");
        return component;
    }
    private void FillChassisFields(ChassisSpecification s)
    {
        void Set(string id,double value)=>_physicalFields[id].Text=GarageNumber(value);
        Set("chassisLength",s.LengthM*1000);Set("chassisFrontX",s.FrontAxleXM*1000);Set("chassisFrontOverhang",s.FrontOverhangM*1000);Set("chassisRearOverhang",s.RearOverhangM*1000);Set("chassisHalfWidth",s.RailHalfWidthM*1000);Set("chassisRailHeight",s.RailHeightM*1000);Set("chassisRackSetback",s.RackSetbackM*1000);Set("chassisRackHeight",s.RackHeightM*1000);
        Set("chassisVerticalEI",s.VerticalBendingRigidityNm2/1e6);Set("chassisLateralEI",s.LateralBendingRigidityNm2/1e6);Set("chassisTorsionGJ",s.TorsionalRigidityNm2/1e6);
    }
    private static string ChassisDescription(ChassisSpecification s)=>$"Chassis source properties · axle-reference spacing {s.LengthM*1000:0.###}mm; overhangs {s.FrontOverhangM*1000:0.###}/{s.RearOverhangM*1000:0.###}mm\nEI_y {s.VerticalBendingRigidityNm2/1e6:0.######} / EI_z {s.LateralBendingRigidityNm2/1e6:0.######} / GJ {s.TorsionalRigidityNm2/1e6:0.######} MN·m²\nEquivalent end-couple rates: bend {s.VerticalBendingRigidityNm2/s.LengthM*Math.PI/180:0}/{s.LateralBendingRigidityNm2/s.LengthM*Math.PI/180:0}, twist {s.TorsionalRigidityNm2/s.LengthM*Math.PI/180:0} N·m/deg. Inextensible three-mode beam; estimated equivalent properties, no local shell modes.";
    private static ConstraintMechanism InstalledMechanism(ResolvedAssemblyGraph graph,InstalledEquilibriumResult result)
    {var mechanism=new ConstraintMechanism(graph);mechanism.Chassis?.Set(result.Motion.ChassisFlex??ChassisFlexState.Zero);return mechanism;}
}
