$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $repository 'src\SuspensionSimulation.App\bin\Debug\net10.0-windows\SuspensionSimulation.App.exe'
$proofDirectory = Join-Path $repository 'artifacts\shortcut-proof'
$shortcutPath = Join-Path $proofDirectory 'Suspension Simulation.lnk'
$sessionPath = Join-Path $repository 'logs\current-session.json'
if (Get-Process -Name 'SuspensionSimulation.App' -ErrorAction SilentlyContinue) {
    throw 'Close the existing app before this self-contained launch test; it will not close a pre-existing user session.'
}
New-Item -ItemType Directory -Path $proofDirectory -Force | Out-Null
& (Join-Path $PSScriptRoot 'Install-DesktopShortcut.ps1') -DestinationDirectory $proofDirectory | Out-Null
$startedAt = [datetime]::UtcNow
$owned = [Collections.Generic.List[int]]::new()
function Wait-LiveSession([string]$PreviousRun = '') {
    $deadline = [datetime]::UtcNow.AddSeconds(20)
    while ([datetime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $sessionPath) {
            $session = Get-Content -LiteralPath $sessionPath -Raw | ConvertFrom-Json
            if ($session.status -eq 'running' -and $session.runId -ne $PreviousRun -and ([datetime]$session.processStartedAt).ToUniversalTime() -ge $startedAt.AddSeconds(-1)) {
                $state = Get-Content -LiteralPath $session.statePath -Raw | ConvertFrom-Json
                if ($state.runId -eq $session.runId -and $state.state.ui.acceptedRevision -ge 1) { return @{ Session = $session; State = $state } }
            }
        }
        Start-Sleep -Milliseconds 100
    }
    throw 'App did not publish a usable live state within 20 seconds.'
}
try {
    Start-Process -FilePath $shortcutPath -WorkingDirectory $repository -WindowStyle Hidden
    $first = Wait-LiveSession
    $owned.Add([int]$first.Session.processId)
    $owner = Get-Process -Id $first.Session.processId
    if ($owner.Path -ne $executable) { throw 'Unexpected executable owns the diagnostic session.' }
    $originalInputs = $first.State.state.ui.inputs.controls | ConvertTo-Json -Depth 10 -Compress
    $originalSnapshot = $first.State.state.ui.acceptedSnapshot
    $duplicates = @()
    for ($i=0; $i -lt 4; $i++) {
        $duplicates += Start-Process -FilePath $executable -WorkingDirectory $repository -WindowStyle Hidden -PassThru
    }
    foreach ($duplicate in $duplicates) {
        if (-not $duplicate.WaitForExit(10000)) { throw 'A duplicate application process failed to exit.' }
        if ($duplicate.ExitCode -ne 0) { throw "Duplicate exited with $($duplicate.ExitCode)." }
    }
    Start-Process -FilePath $shortcutPath -WorkingDirectory $repository -WindowStyle Hidden
    $deadline = [datetime]::UtcNow.AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 100
        $again = Wait-LiveSession
    } while ($again.State.state.activationCount -lt 5 -and [datetime]::UtcNow -lt $deadline)
    if ($again.Session.runId -ne $first.Session.runId -or $again.Session.processId -ne $owner.Id) { throw 'Re-launch replaced the owner.' }
    if ($again.State.state.activationCount -lt 5) { throw 'Existing owner did not receive all launch activation requests.' }
    $windows = @(Get-Process -Name 'SuspensionSimulation.App' | Where-Object MainWindowHandle -ne 0)
    if ($windows.Count -ne 1) { throw "Expected one application window; found $($windows.Count)." }
    if (($again.State.state.ui.inputs.controls | ConvertTo-Json -Depth 10 -Compress) -ne $originalInputs) { throw 'Re-launch changed the input fields.' }
    if ($again.State.state.ui.acceptedSnapshot.B.FrontRsd -ne $originalSnapshot.B.FrontRsd) { throw 'Re-launch changed the accepted result.' }
    if (-not (Test-Path -LiteralPath $again.Session.logPath)) { throw 'Run log unavailable.' }
    if (-not (Select-String -LiteralPath $again.Session.logPath -Pattern 'existing-instance-activated' -Quiet)) { throw 'Activation was not logged.' }
    $heartbeat = $again.Session.updatedAt
    Start-Sleep -Milliseconds 1200
    $fresh = Wait-LiveSession
    if ($fresh.Session.updatedAt -eq $heartbeat) { throw 'Heartbeat stopped updating.' }
    if (-not $owner.CloseMainWindow() -or -not $owner.WaitForExit(10000)) { throw 'Owner failed to close normally.' }
    $closed = Get-Content -LiteralPath $sessionPath -Raw | ConvertFrom-Json
    if ($closed.status -ne 'closed') { throw 'Closed lifecycle status was not published.' }
    $closedState = Get-Content -LiteralPath $closed.statePath -Raw | ConvertFrom-Json
    if ($closedState.state.ui.acceptedSnapshot.B.FrontRsd -ne $originalSnapshot.B.FrontRsd) { throw 'Closing discarded the last accepted state.' }
    Start-Process -FilePath $shortcutPath -WorkingDirectory $repository -WindowStyle Hidden
    $reopened = Wait-LiveSession -PreviousRun $first.Session.runId
    $owned.Add([int]$reopened.Session.processId)
    if ($reopened.Session.logPath -eq $first.Session.logPath) { throw 'Reopen reused the old run log.' }
    $summary = & (Join-Path $PSScriptRoot 'Get-LiveState.ps1') -Summary | ConvertFrom-Json
    if (-not $summary.Running) { throw 'Live-state reader could not verify the process identity.' }
    [pscustomobject]@{
        Success = $true; InitialPid = $first.Session.processId; ReopenedPid = $reopened.Session.processId
        ActivationRequests = $again.State.state.activationCount; SingleWindow = $true; InputsAndResultPreserved = $true
        LiveHeartbeat = $true; ClosedAndReopened = $true; StateReadable = $true; LogReadable = $true
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $proofDirectory 'launch-proof.json')
    Get-Content -LiteralPath (Join-Path $proofDirectory 'launch-proof.json')
} finally {
    foreach ($ownedId in $owned) {
        $process = Get-Process -Id $ownedId -ErrorAction SilentlyContinue
        if ($null -ne $process -and $process.Path -eq $executable -and $process.StartTime.ToUniversalTime() -ge $startedAt.AddSeconds(-1)) {
            [void]$process.CloseMainWindow()
            [void]$process.WaitForExit(10000)
        }
    }
}
