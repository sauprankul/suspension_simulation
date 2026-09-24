using System.Text.Json;

namespace SuspensionSimulation.Reference;

public sealed record StockReferenceConstants(
    double SpringRateNPerMPerKgfPerMm,
    double BarRateKgfPerMmPerNPerM);

public sealed record StockVehicleReference(
    double TotalMassKg,
    double FrontMassFraction,
    double FrontUnsprungMassPerCornerKg,
    double RearUnsprungMassPerCornerKg,
    double FrontTrackM,
    double RearTrackM,
    double WheelbaseM,
    string SourceCells);

public sealed record StockArbReferenceInput(
    double ShearModulusPa,
    double YoungModulusPa,
    double CantileverLengthM,
    double TorsionLengthM,
    double OutsideDiameterM,
    double InsideDiameterM,
    double MotionRatio,
    double EnteredRateKgfPerMm,
    string SourceCell);

public sealed record StockSetupExpected(
    double FrontSpringWheelRateKgfPerMm,
    double RearSpringWheelRateKgfPerMm,
    double FrontRideFrequencyHz,
    double RearRideFrequencyHz,
    double RearToFrontFrequencyRatio,
    string RideCharacter,
    double FrontArbRawRateKgfPerMm,
    double RearArbRawRateKgfPerMm,
    double FrontArbWheelRateKgfPerMm,
    double RearArbWheelRateKgfPerMm,
    double FrontRollStiffnessNmPerDeg,
    double RearRollStiffnessNmPerDeg,
    double TotalRollStiffnessNmPerDeg,
    double FrontRollStiffnessDistribution,
    double ArbFractionOfTotalRollStiffness);

public sealed record StockSetupReference(
    string Id,
    string Name,
    int SourceRow,
    double FrontSpringRateKgfPerMm,
    double RearSpringRateKgfPerMm,
    double FrontSpringMotionRatio,
    double RearSpringMotionRatio,
    StockArbReferenceInput FrontArb,
    StockArbReferenceInput RearArb,
    StockSetupExpected Expected);

public sealed record StockReferenceDocument(
    string Schema,
    StockReferenceConstants Constants,
    StockVehicleReference Vehicle,
    IReadOnlyList<StockSetupReference> Setups);

public sealed record StockArbDerivation(
    double SecondMomentM4,
    double PolarSecondMomentM4,
    double CantileverStiffnessNPerM,
    double TorsionEquivalentRateNPerM,
    double RawRateNPerM,
    double RawRateKgfPerMm,
    double RateUsedKgfPerMm,
    double RateUsedMinusDerivedKgfPerMm,
    double WheelRateKgfPerMm);

public sealed record StockSetupDerivation(
    string Id,
    string Name,
    double FrontSpringWheelRateKgfPerMm,
    double RearSpringWheelRateKgfPerMm,
    double FrontRideFrequencyHz,
    double RearRideFrequencyHz,
    double RearToFrontFrequencyRatio,
    string RideCharacter,
    StockArbDerivation FrontArb,
    StockArbDerivation RearArb,
    double FrontSpringRollStiffnessNmPerDeg,
    double RearSpringRollStiffnessNmPerDeg,
    double FrontArbRollStiffnessNmPerDeg,
    double RearArbRollStiffnessNmPerDeg,
    double FrontRollStiffnessNmPerDeg,
    double RearRollStiffnessNmPerDeg,
    double TotalRollStiffnessNmPerDeg,
    double FrontRollStiffnessDistribution,
    double RollCoupleBiasPercentagePoints,
    double ArbFractionOfTotalRollStiffness);

public static class Stock86BrzReferenceLoader
{
    public static StockReferenceDocument Load(string path) =>
        JsonSerializer.Deserialize<StockReferenceDocument>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException($"Could not deserialize stock reference '{path}'.");
}

/// <summary>
/// Independent transcription of the supplied workbook's stock rows. Constants intentionally match
/// the workbook, including its 9,800 N/m per kgf/mm spring conversion and 0.000101972 bar conversion.
/// </summary>
public static class Stock86BrzReferenceCalculator
{
    public static StockSetupDerivation Calculate(StockReferenceDocument document, StockSetupReference setup)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(setup);
        var vehicle = document.Vehicle; var constants = document.Constants;
        var frontSpringWheel = setup.FrontSpringRateKgfPerMm * Square(setup.FrontSpringMotionRatio);
        var rearSpringWheel = setup.RearSpringRateKgfPerMm * Square(setup.RearSpringMotionRatio);
        var frontSprungCornerMass = vehicle.TotalMassKg * vehicle.FrontMassFraction * .5 - vehicle.FrontUnsprungMassPerCornerKg;
        var rearSprungCornerMass = vehicle.TotalMassKg * (1 - vehicle.FrontMassFraction) * .5 - vehicle.RearUnsprungMassPerCornerKg;
        var frontFrequency = Frequency(frontSpringWheel, frontSprungCornerMass, constants.SpringRateNPerMPerKgfPerMm);
        var rearFrequency = Frequency(rearSpringWheel, rearSprungCornerMass, constants.SpringRateNPerMPerKgfPerMm);
        var frontBar = Arb(setup.FrontArb, constants); var rearBar = Arb(setup.RearArb, constants);
        var frontSpringRoll = Roll(vehicle.FrontTrackM, frontSpringWheel, constants.SpringRateNPerMPerKgfPerMm);
        var rearSpringRoll = Roll(vehicle.RearTrackM, rearSpringWheel, constants.SpringRateNPerMPerKgfPerMm);
        var frontArbRoll = Roll(vehicle.FrontTrackM, frontBar.WheelRateKgfPerMm, constants.SpringRateNPerMPerKgfPerMm);
        var rearArbRoll = Roll(vehicle.RearTrackM, rearBar.WheelRateKgfPerMm, constants.SpringRateNPerMPerKgfPerMm);
        var frontRoll = frontSpringRoll + frontArbRoll; var rearRoll = rearSpringRoll + rearArbRoll; var total = frontRoll + rearRoll;
        var rsd = frontRoll / total;
        return new(setup.Id, setup.Name, frontSpringWheel, rearSpringWheel, frontFrequency, rearFrequency, rearFrequency / frontFrequency,
            rearFrequency > frontFrequency ? "FLATRIDE" : "PITCH", frontBar, rearBar,
            frontSpringRoll, rearSpringRoll, frontArbRoll, rearArbRoll, frontRoll, rearRoll, total, rsd,
            100 * (rsd - vehicle.FrontMassFraction), (frontArbRoll + rearArbRoll) / total);
    }

    private static StockArbDerivation Arb(StockArbReferenceInput input, StockReferenceConstants constants)
    {
        if (input.OutsideDiameterM <= 0 || input.InsideDiameterM < 0 || input.InsideDiameterM >= input.OutsideDiameterM)
            throw new ArgumentOutOfRangeException(nameof(input), "ARB requires OD > ID >= 0.");
        var i = Math.PI / 64 * (Math.Pow(input.OutsideDiameterM, 4) - Math.Pow(input.InsideDiameterM, 4));
        var j = 2 * i;
        var cantilever = 3 * input.YoungModulusPa * i / Math.Pow(input.CantileverLengthM, 3);
        var torsion = input.ShearModulusPa * j / (Square(input.CantileverLengthM) * input.TorsionLengthM);
        var raw = 1 / (1 / torsion + 2 / cantilever);
        var rawKgf = raw * constants.BarRateKgfPerMmPerNPerM;
        return new(i, j, cantilever, torsion, raw, rawKgf, input.EnteredRateKgfPerMm,
            input.EnteredRateKgfPerMm-rawKgf, input.EnteredRateKgfPerMm * Square(input.MotionRatio));
    }

    private static double Frequency(double wheelRateKgfPerMm, double sprungCornerMassKg, double conversion) =>
        Math.Sqrt(wheelRateKgfPerMm * conversion / sprungCornerMassKg) / (2 * Math.PI);

    private static double Roll(double trackM, double wheelRateKgfPerMm, double conversion) =>
        Square(trackM) * wheelRateKgfPerMm * conversion / (2 * 360 / (2 * Math.PI));

    private static double Square(double value) => value * value;
}
