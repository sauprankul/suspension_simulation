param([switch]$Summary)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$sessionPath = Join-Path $repository 'logs\current-session.json'
if (-not (Test-Path -LiteralPath $sessionPath)) { throw 'No desktop session has published diagnostics yet.' }
$session = Get-Content -LiteralPath $sessionPath -Raw | ConvertFrom-Json
$owner = Get-Process -Id $session.processId -ErrorAction SilentlyContinue
$alive = $null -ne $owner -and [Math]::Abs(($owner.StartTime.ToUniversalTime() - ([datetime]$session.processStartedAt).ToUniversalTime()).TotalSeconds) -lt 1
if ($Summary) {
    [pscustomobject]@{
        Running = $alive; Status = $session.status; ProcessId = $session.processId; RunId = $session.runId
        Heartbeat = $session.updatedAt; Log = $session.logPath; State = $session.statePath
    } | ConvertTo-Json
} else {
    $state = Get-Content -LiteralPath $session.statePath -Raw | ConvertFrom-Json
    if ($state.runId -ne $session.runId) { throw 'The app restarted during this read; retry to get its new session.' }
    [pscustomobject]@{ Running = $alive; Session = $session; LiveState = $state } | ConvertTo-Json -Depth 100
}
