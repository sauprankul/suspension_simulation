param([string]$Repository=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$root=[System.IO.Path]::GetFullPath($Repository)
$executable=Join-Path $root 'src/SuspensionSimulation.App/bin/Release/net10.0-windows/SuspensionSimulation.App.exe'
if(!(Test-Path -LiteralPath $executable)){throw 'Build the optimized app first: dotnet build src/SuspensionSimulation.App -c Release'}
$desktop=[Environment]::GetFolderPath('Desktop')
if([string]::IsNullOrWhiteSpace($desktop)){throw 'Windows did not provide a desktop directory.'}
$link=Join-Path $desktop 'Suspension Simulation.lnk'
$shell=New-Object -ComObject WScript.Shell
$shortcut=$shell.CreateShortcut($link)
$shortcut.TargetPath=$executable
$shortcut.WorkingDirectory=$root
$shortcut.IconLocation=$executable+',0'
$shortcut.Description="Suspension Simulation: one instance. Live state and logs are in $root\logs"
$shortcut.Save()
Write-Output $link
