using System.Numerics;
using System.Text.Json;

namespace SuspensionSimulation.Kinematics;

public sealed record Gr86HardPointPackage3D(
    IReadOnlyDictionary<string, MacPhersonCorner3D> Front,
    IReadOnlyDictionary<string, DoubleWishboneCorner3D> Rear,
    string Provenance,
    string CoordinateFrame);

public static class Gr86HardPointPackageLoader
{
    public static Gr86HardPointPackage3D Load(string directory)
    {
        var frontPath = Path.Combine(directory, "front-strut-hard-points.json");
        var rearPath = Path.Combine(directory, "rear-double-wishbone-hard-points.json");
        using var frontDocument = JsonDocument.Parse(File.ReadAllText(frontPath));
        using var rearDocument = JsonDocument.Parse(File.ReadAllText(rearPath));
        var f = frontDocument.RootElement; var r = rearDocument.RootElement;
        var frontLeft = Front("FL", f.GetProperty("left"), 1);
        var frontRight = Front("FR", f.GetProperty("left"), -1);
        var rearLeft = Rear("RL", r.GetProperty("left"), 1);
        var rearRight = Rear("RR", r.GetProperty("left"), -1);
        return new(new Dictionary<string, MacPhersonCorner3D>(StringComparer.OrdinalIgnoreCase) { ["FL"] = frontLeft, ["FR"] = frontRight },
            new Dictionary<string, DoubleWishboneCorner3D>(StringComparer.OrdinalIgnoreCase) { ["RL"] = rearLeft, ["RR"] = rearRight },
            $"front: {f.GetProperty("provenance").GetString()}; rear: {r.GetProperty("provenance").GetString()}",
            f.GetProperty("coordinateFrame").GetString()!);
    }

    private static MacPhersonCorner3D Front(string id, JsonElement x, float side)
    {
        var wheel = Point(x, "wheelCenter", side); var lower = Point(x, "lowerBallJoint", side);
        var strut = Point(x, "strutUprightLower", side); var tie = Point(x, "tieRodOuter", side);
        var upright = new RigidUprightPoints3D(wheel, lower, strut, tie, strut, Point(x, "springLower", side), Point(x, "arbLinkLower", side));
        return new(id, upright, Point(x, "lowerArmFrontPivot", side), Point(x, "lowerArmRearPivot", side), Point(x, "strutTopMount", side), Point(x, "tieRodInner", side), Point(x, "springUpper", side));
    }

    private static DoubleWishboneCorner3D Rear(string id, JsonElement x, float side)
    {
        var wheel = Point(x, "wheelCenter", side); var lower = Point(x, "lowerBallJoint", side); var upper = Point(x, "upperBallJoint", side);
        var upright = new RigidUprightPoints3D(wheel, lower, upper, Point(x, "toeLinkOuter", side), lower, lower, lower);
        return new(id, upright, Point(x, "lowerArmFrontPivot", side), Point(x, "lowerArmRearPivot", side), Point(x, "upperArmFrontPivot", side), Point(x, "upperArmRearPivot", side), Point(x, "toeLinkInner", side), Point(x, "springUpper", side), Point(x, "springLowerOnLowerArm", side), Point(x, "arbLinkLowerOnLowerArm", side));
    }

    private static Vector3 Point(JsonElement owner, string name, float side)
    {
        var a = owner.GetProperty(name); return new((float)a[0].GetDouble(), side * (float)a[1].GetDouble(), (float)a[2].GetDouble());
    }
}
