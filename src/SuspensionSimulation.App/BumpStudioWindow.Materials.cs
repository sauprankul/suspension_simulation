using System.Globalization;
using System.IO;
using System.Text;
using SuspensionSimulation.Design;

namespace SuspensionSimulation.App;
internal sealed partial class BumpStudioWindow
{
    private void SetMaterialCoefficients(BumperMaterialCard card)
    {
        var keys=EstimatedBumperMaterials.Keys(card);if(keys.Length!=3||!_fields.ContainsKey("law0")) return;
        for(var i=0;i<3;i++) _fields["law"+i].Text=(card.Law!.ParametersSi[keys[i]]/(i<2||keys[i]=="k"?1e6:1)).ToString("R",CultureInfo.InvariantCulture);
        if(card.PrintRecipe is {} print) ApplyPrint(print);
    }
    private void PreservePresetEvidence(BumperMaterialCard card)
    {
        if(EstimatedBumperMaterials.Keys(card).Length!=3) return;
        var text=EstimatedBumperMaterials.Evidence(card.Id);var bytes=Encoding.UTF8.GetBytes(text);
        var hash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));if(!card.EvidenceSha256.Contains(hash)) return;
        Directory.CreateDirectory(_service.Root);var file=Path.Combine(_service.Root,"estimated-source-record.txt");File.WriteAllBytes(file,bytes);_service.PreserveMaterialEvidence(file);
    }
    internal void VerifyMaterialOverrideProof()
    {
        var before=ReadMaterial();var key=EstimatedBumperMaterials.Keys(before)[0];var target=before.Law!.ParametersSi[key]*1.1;
        _fields["law0"].Text=(target/1e6).ToString("R",CultureInfo.InvariantCulture);SaveRecipe();OpenRecipe(_recipeHash!);
        if(Math.Abs(ReadMaterial().Law!.ParametersSi[key]-target)>1e-7||ReadMaterial().Readiness!=MaterialReadiness.Estimated) throw new InvalidOperationException("Estimated material override did not survive recipe replay.");
        var saved=_fields["law0"].Text;_fields["law0"].Text="invalid coefficient";SaveDraft();RestoreDraft();if(_fields["law0"].Text!="invalid coefficient") throw new InvalidOperationException("Invalid coefficient draft was lost.");_fields["law0"].Text=saved;SaveDraft();
    }
}
