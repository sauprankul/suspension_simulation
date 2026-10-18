namespace SuspensionSimulation.App;

internal sealed partial class BumpStudioWindow
{
    private BumperDiagnosticWindow? _diagnosticViewer;
    private void OpenDiagnosticViewer()
    {
        if(_diagnosticViewer is not null) {_diagnosticViewer.Activate();return;}
        _diagnosticViewer=new BumperDiagnosticWindow {Owner=this};_diagnosticViewer.Closed+=(_,_)=>_diagnosticViewer=null;_diagnosticViewer.Show();
    }
}
