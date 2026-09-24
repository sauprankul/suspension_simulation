using System.Numerics;
using System.Text.Json;

namespace SuspensionSimulation.Kinematics;

public static class TubularArbMechanism3DLoader
{
    public static TubularArbMechanism3DConfig Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path)); var r = document.RootElement; var tube = r.GetProperty("tube");
        return new(r.GetProperty("id").GetString()!, Point(r,"axisStart"), Point(r,"axisEnd"), Point(r,"leftArmEnd"), Point(r,"rightArmEnd"),
            Point(r,"leftLowerJoint"), Point(r,"rightLowerJoint"), tube.GetProperty("outsideDiameterM").GetDouble(), tube.GetProperty("insideDiameterM").GetDouble(),
            tube.GetProperty("shearModulusPa").GetDouble(), tube.GetProperty("torsionLengthM").GetDouble(), r.GetProperty("provenance").GetString()!);
    }
    private static Vector3 Point(JsonElement r, string name) { var a=r.GetProperty(name); return new((float)a[0].GetDouble(),(float)a[1].GetDouble(),(float)a[2].GetDouble()); }
}
