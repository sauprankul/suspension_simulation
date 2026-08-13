using SuspensionSimulation.Components;
using SuspensionSimulation.Configuration;

namespace SuspensionSimulation.Core;

/// <summary>Stitches independently owned configuration files into the first quasi-static model.</summary>
public static class SyntheticFixtureLoader
{
    public static VehiclePackage LoadFourStrutPackage(string directory)
    {
        var chassis = JsonConfigurationLoader.Load<ChassisConfig>(Path.Combine(directory, "chassis.json"));
        var springs = JsonConfigurationLoader.Load<List<SpringConfig>>(Path.Combine(directory, "springs.json"));
        var bars = JsonConfigurationLoader.Load<List<AntiRollBarConfig>>(Path.Combine(directory, "anti-roll-bars.json"));
        var stops = JsonConfigurationLoader.Load<List<BumpStopConfig>>(Path.Combine(directory, "bump-stops.json"));
        _ = JsonConfigurationLoader.Load<List<DamperConfig>>(Path.Combine(directory, "dampers.json"));
        var assembly = JsonConfigurationLoader.Load<SuspensionAssemblyConfig>(Path.Combine(directory, "suspension-assembly.json"));
        var springById = springs.ToDictionary(x => x.Id); var stopById = stops.ToDictionary(x => x.Id);
        var corners = assembly.Corners.Select(c =>
        {
            var spring = springById[c.SpringId]; var stop = stopById[c.BumpStopId];
            var rate = spring.LinearRateNPerM * c.MotionRatio * c.MotionRatio;
            var firstSegment = stop.ForceCurve.Count < 2 ? 0 : (stop.ForceCurve[1].Y - stop.ForceCurve[0].Y) / (stop.ForceCurve[1].X - stop.ForceCurve[0].X);
            return new CornerSpring(c.Corner.ToString(), rate, stop.EngagementGapM / c.MotionRatio, firstSegment * c.MotionRatio * c.MotionRatio);
        }).ToList();
        var barById = bars.ToDictionary(x => x.Id);
        return new VehiclePackage(
            new ChassisModel(chassis.Id, chassis.TotalMassKg, chassis.WheelbaseM, chassis.FrontTrackM, chassis.RearTrackM, chassis.CgHeightM, chassis.StaticFrontWeightFraction, chassis.Provenance.ToString(), chassis.SourceNote),
            new SpringsModel(corners, "Synthetic", "Linear wheel rates are derived from synthetic component rates and declared motion ratios."),
            [AntiRollBarModel.FromConfig("front", barById[assembly.FrontAntiRollBarId], "Synthetic"), AntiRollBarModel.FromConfig("rear", barById[assembly.RearAntiRollBarId], "Synthetic")],
            new DampingModel("Synthetic", "Loaded but intentionally excluded from this static solver."),
            new ModelScope("first-milestone/0.2", ["flat road", "rigid body", "small angles", "quasi-static accelerations", "linear springs and ARBs", "incremental state about static reference", "zero-rate synthetic bump-stop placeholders"], ["tire forces and load sensitivity", "kinematic roll centers and jacking", "damper forces and transient dynamics", "chassis and bushing compliance", "full progressive bump-stop curve", "a calibrated static bump-stop operating point"]));
    }
}
