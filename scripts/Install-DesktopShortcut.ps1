param([string]$DestinationDirectory = [Environment]::GetFolderPath('DesktopDirectory'))
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $repository 'src\SuspensionSimulation.App\bin\Debug\net10.0-windows\SuspensionSimulation.App.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Build the desktop app before installing its shortcut.' }
if (-not (Test-Path -LiteralPath $DestinationDirectory -PathType Container)) { throw "Shortcut directory does not exist: $DestinationDirectory" }
$shortcutPath = Join-Path $DestinationDirectory 'Suspension Simulation.lnk'
$shell = New-Object -ComObject WScript.Shell
try {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    if ((Test-Path -LiteralPath $shortcutPath) -and $shortcut.TargetPath -ne $executable) {
        throw "A different shortcut already exists at $shortcutPath; it was left unchanged."
    }
    $shortcut.TargetPath = $executable
    $shortcut.WorkingDirectory = $repository
    $shortcut.Description = 'Start Suspension Simulation, or bring its existing window forward. State and logs remain in the project logs folder.'
    $shortcut.IconLocation = "$executable,0"
    $shortcut.WindowStyle = 1
    $shortcut.Save()
    [pscustomobject]@{ Shortcut = $shortcutPath; Target = $executable; WorkingDirectory = $repository }
} finally {
    if ($null -ne $shortcut) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
}
