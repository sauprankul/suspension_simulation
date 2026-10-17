using System.Text.Json;

namespace SuspensionSimulation.App;

public partial class MainWindow
{
    internal object CaptureLiveDiagnostics()=>new
    {
        window=new {title=Title,visible=IsVisible,state=WindowState.ToString(),active=IsActive},
        inputs=JsonSerializer.Deserialize<JsonElement>(CaptureUiState(_renderAttempt)),
        acceptedRevision=_acceptedSnapshot?.Revision,rejectedRevision=_rejectedRevision,rejectedError=_rejectedError,
        acceptedSnapshot=_acceptedSnapshot,
        result=_comparison is null?(JsonElement?)null:JsonSerializer.Deserialize<JsonElement>(CaptureResult(_acceptedSnapshot?.Revision??0,_comparison)),
        view=new
        {
            mode=_inspectionEnabled?"3D":"Dashboard",selectedSetup=InspectionSetup.SelectedIndex==0?"A":"B",
            selectedComponent=_inspectionViewport?.SelectedId,camera=_inspectionViewport?.CaptureCamera(),
            adapter=_inspectionViewport?.Adapter,softwareRendering=_inspectionViewport?.Software
        }
    };
}
