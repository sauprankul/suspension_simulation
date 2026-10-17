param([string]$Repository = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$culture = [System.Globalization.CultureInfo]::InvariantCulture
$solver = Join-Path $Repository 'artifacts/fea-toolchain/calculix-2.23/calculix_2.23_4win/ccx_static.exe'
if ((Get-FileHash -LiteralPath $solver -Algorithm SHA256).Hash -ne 'E18360EB32A5CD48F91A212C57069212CD8E27D2F798A599A78106BD0B3ECC1F') { throw 'CalculiX executable does not match the reviewed 2.23 binary.' }
$outputRoot = Join-Path $Repository 'artifacts/fea-coupon-spike'
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

function Invoke-Coupon([string]$Name, [bool]$Hyperelastic, [double]$Compression) {
    $folder = Join-Path $outputRoot $Name
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $material = if ($Hyperelastic) { "*HYPERELASTIC,NEO HOOKE`n500000.,2.E-8" } else { "*ELASTIC`n1000000.,0.3" }
    $step = if ($Hyperelastic) { "*STEP,NLGEOM,INC=200`n*STATIC,SOLVER=SPOOLES`n0.05,1.,1.E-8,0.05" } else { "*STEP`n*STATIC,SOLVER=SPOOLES`n1.,1." }
    $displacement = (-$Compression).ToString('R',$culture)
    $deck = @"
*HEADING
Synthetic homogeneous coupon; SI; numerical verification only, no manufacturer material claim
*NODE
1,0,0,0
2,.02,0,0
3,.02,.02,0
4,0,.02,0
5,0,0,.02
6,.02,0,.02
7,.02,.02,.02
8,0,.02,.02
*ELEMENT,TYPE=C3D8,ELSET=BODY
1,1,2,3,4,5,6,7,8
*NSET,NSET=TOP
5,6,7,8
*NSET,NSET=BOTTOM
1,2,3,4
*MATERIAL,NAME=EDUCATIONAL
$material
*SOLID SECTION,ELSET=BODY,MATERIAL=EDUCATIONAL
*BOUNDARY
BOTTOM,3,3,0.
1,1,2,0.
2,2,2,0.
$step
*BOUNDARY
TOP,3,3,$displacement
*NODE PRINT,NSET=TOP,TOTALS=YES
RF
*NODE PRINT,NSET=BOTTOM,TOTALS=YES
RF
*NODE FILE
U
*END STEP
"@
    [System.IO.File]::WriteAllText((Join-Path $folder 'coupon.inp'),$deck,[System.Text.UTF8Encoding]::new($false))
    $start = [System.Diagnostics.ProcessStartInfo]::new($solver)
    $start.WorkingDirectory = $folder
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.ArgumentList.Add('-i')
    $start.ArgumentList.Add('coupon')
    $start.Environment['OMP_NUM_THREADS'] = '4'
    $start.Environment['CCX_NPROC_RESULTS'] = '4'
    $start.Environment['CCX_NPROC_EQUATION_SOLVER'] = '4'
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $start
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $peak = 0L
    try {
        $null = $process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        while (-not $process.WaitForExit(100)) {
            $process.Refresh()
            $peak = [Math]::Max($peak,$process.WorkingSet64)
            if ($watch.Elapsed.TotalSeconds -gt 90 -or $peak -gt 2GB) { $process.Kill($true); throw 'Coupon exceeded its 90-second/2-GB budget.' }
        }
        [System.IO.File]::WriteAllText((Join-Path $folder 'stdout.log'),$stdout.Result)
        [System.IO.File]::WriteAllText((Join-Path $folder 'stderr.log'),$stderr.Result)
        if ($process.ExitCode -ne 0) { throw "CalculiX failed in $Name; inspect its archived log/deck." }
    } finally {
        if ($process.Id -and -not $process.HasExited) { $process.Kill($true) }
        $process.Dispose()
    }
    [pscustomobject]@{name=$Name;seconds=$watch.Elapsed.TotalSeconds;peakWorkingSetBytes=$peak;deck=(Join-Path $folder 'coupon.inp');data=(Join-Path $folder 'coupon.dat')}
}

$results = @(
    Invoke-Coupon 'linear-small-strain' $false 0.00002
    Invoke-Coupon 'neo-hookean-30-percent' $true 0.006
)
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'runs.json')
$results | Format-Table name,seconds,peakWorkingSetBytes

function Read-Reaction([string]$Path,[string]$Set) {
    $text = [System.IO.File]::ReadAllText($Path)
    $number = '([-+0-9.Ee]+)'
    $pattern = 'total force \(fx,fy,fz\) for set '+$Set+' and time\s+'+$number+'\s+'+$number+'\s+'+$number+'\s+'+$number
    $reactionMatches = [regex]::Matches($text,$pattern)
    if ($reactionMatches.Count -eq 0) { throw "No reaction output for $Set in $Path" }
    $last = $reactionMatches[$reactionMatches.Count-1]
    $time = [double]::Parse($last.Groups[1].Value,$culture)
    if ([Math]::Abs($time-1) -gt 1E-6) { throw 'Last displacement increment did not converge to the requested stroke.' }
    [double]::Parse($last.Groups[4].Value,$culture)
}
function NeoHookean-Piola([double]$Lateral,[double]$Axial,[bool]$Radial) {
    $jacobian = $Lateral*$Lateral*$Axial
    $invariant = 2*$Lateral*$Lateral+$Axial*$Axial
    $stretch = if ($Radial) { $Lateral } else { $Axial }
    # Independent differentiation of W=C10*(J^(-2/3)*I1-3)+(J-1)^2/D1.
    1000000*[Math]::Pow($jacobian,-2.0/3)*($stretch-$invariant/(3*$stretch))+100000000*($jacobian-1)*$jacobian/$stretch
}
$low=.5; $high=2.0
for ($iteration=0; $iteration -lt 100; $iteration++) {
    $middle=($low+$high)/2
    if ((NeoHookean-Piola $middle .7 $true) -gt 0) { $high=$middle } else { $low=$middle }
}
$expected = @(0.4, ([Math]::Abs((NeoHookean-Piola (($low+$high)/2) .7 $false))*.0004))
$checks = for ($i=0; $i -lt $results.Count; $i++) {
    $top=Read-Reaction $results[$i].data 'TOP'
    $bottom=Read-Reaction $results[$i].data 'BOTTOM'
    $relativeDifference=[Math]::Abs([Math]::Abs($top)-$expected[$i])/$expected[$i]
    $imbalance=[Math]::Abs($top+$bottom)/$expected[$i]
    if ($relativeDifference -gt .001 -or $imbalance -gt .0001) { throw "Coupon analytic/reaction-balance gate failed: $($results[$i].name), expected $($expected[$i]), got $top" }
    [pscustomobject]@{name=$results[$i].name;expectedForceN=$expected[$i];reactionForceN=[Math]::Abs($top);relativeError=$relativeDifference;relativeSupportImbalance=$imbalance;seconds=$results[$i].seconds;peakWorkingSetBytes=if($results[$i].peakWorkingSetBytes -gt 0){$results[$i].peakWorkingSetBytes}else{$null}}
}
@{status='Partial feasibility spike: homogeneous coupons only';material='Synthetic educational coefficients; no manufacturer/correlated claim';solver='CalculiX 2.23 / SPOOLES';maximumThreads=4;memoryLimitBytes=2GB;cases=@($checks);pending=@('STEP contact compression','nearly incompressible locking/contact benchmark','fitted foam coupon','groove fold/self-contact','axisymmetric total-force convention','three-mesh convergence','independent physical correlation')} |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputRoot 'verification.json')
$checks | Format-Table name,expectedForceN,reactionForceN,relativeError,relativeSupportImbalance
