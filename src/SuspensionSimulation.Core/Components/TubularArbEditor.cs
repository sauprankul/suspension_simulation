namespace SuspensionSimulation.Components;

/// <summary>
/// Editable physical inputs for one symmetric installed tubular bar. The wheel-to-end-link
/// derivative is deliberately explicit: link length/angle alone cannot determine it without
/// suspension hard-point travel geometry.
/// </summary>
public sealed record SymmetricTubularArbInput(
    double OutsideDiameterM,
    double InsideDiameterM,
    double ShearModulusPa,
    double TorsionLengthM,
    double LeverArmM,
    double EndLinkLengthM,
    double EndLinkAngleFromVerticalRad,
    double WheelToEndLinkVerticalMotionRatio);

public static class TubularArbEditor
{
    public static AntiRollBarConfig ApplySymmetricInput(AntiRollBarConfig source, SymmetricTubularArbInput input)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(input);
        var current = source.Tubular ?? throw new InvalidOperationException($"ARB '{source.Id}' has no tubular geometry to edit.");
        var side = new ArbSideGeometry(input.LeverArmM, input.EndLinkLengthM, input.EndLinkAngleFromVerticalRad,
            input.WheelToEndLinkVerticalMotionRatio);
        var edited = source with
        {
            ModelKind = AntiRollBarModelKind.TubularGeometry,
            Tubular = current with
            {
                OutsideDiameterM = input.OutsideDiameterM,
                InsideDiameterM = input.InsideDiameterM,
                ShearModulusPa = input.ShearModulusPa,
                TorsionLengthM = input.TorsionLengthM,
                Left = side,
                Right = side
            }
        };
        // Validate all inputs and the derived virtual-work model before exposing it to a package.
        _ = AntiRollBar.CreateTubular(edited);
        return edited;
    }
}
