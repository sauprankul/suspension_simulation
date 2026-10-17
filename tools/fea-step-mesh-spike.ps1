param([string]$Repository = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$culture=[System.Globalization.CultureInfo]::InvariantCulture
$mesher=Join-Path $Repository 'artifacts/fea-toolchain/gmsh-4.15.2/gmsh-4.15.2-Windows64/gmsh.exe'
if((Get-FileHash -LiteralPath $mesher -Algorithm SHA256).Hash -ne '317C43391E5B1FAB3A1DD80DC5245DAD6E2D087910F4B8EBC234BD6D4B8F41A1'){throw 'Gmsh does not match the reviewed 4.15.2 executable.'}
$folder=Join-Path $Repository 'artifacts/fea-step-mesh-spike'
New-Item -ItemType Directory -Force -Path $folder | Out-Null
function Invoke-Gmsh([string]$Name,[string[]]$Arguments){
    $start=[System.Diagnostics.ProcessStartInfo]::new($mesher)
    $start.WorkingDirectory=$folder;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach($argument in $Arguments){$start.ArgumentList.Add($argument)}
    $process=[System.Diagnostics.Process]::new();$process.StartInfo=$start;$watch=[System.Diagnostics.Stopwatch]::StartNew();$peak=0L
    try{
        $null=$process.Start();$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        while(-not $process.WaitForExit(100)){$process.Refresh();$peak=[Math]::Max($peak,$process.WorkingSet64);if($watch.Elapsed.TotalSeconds -gt 120 -or $peak -gt 2GB){$process.Kill($true);throw 'Mesher exceeded its 120-second/2-GB budget.'}}
        [System.IO.File]::WriteAllText((Join-Path $folder ($Name+'.stdout.log')),$stdout.Result);[System.IO.File]::WriteAllText((Join-Path $folder ($Name+'.stderr.log')),$stderr.Result)
        if($process.ExitCode -ne 0){throw "Gmsh failed: $Name"}
        [pscustomobject]@{seconds=$watch.Elapsed.TotalSeconds;peakWorkingSetBytes=if($peak -gt 0){$peak}else{$null};stdout=$stdout.Result}
    }finally{if($process.Id -and -not $process.HasExited){$process.Kill($true)};$process.Dispose()}
}
$cad=@'
SetFactory("OpenCASCADE");
// Millimetre CAD coordinates. Off-centre side groove prevents an axisymmetric reduction.
Cylinder(1)={0,0,0,0,0,40,20};
Cylinder(2)={0,0,-1,0,0,42,4};
BooleanDifference(3)={Volume{1};Delete;}{Volume{2};Delete;};
Box(4)={12,-25,16,15,50,8};
BooleanDifference(5)={Volume{3};Delete;}{Volume{4};Delete;};
Save "asymmetric-mm.step";
'@
[System.IO.File]::WriteAllText((Join-Path $folder 'create.geo'),$cad)
$creation=Invoke-Gmsh 'create' @('create.geo','-0','-nt','4','-v','4')
$reports=foreach($size in @(.006,.0045,.0035)){
    $key=$size.ToString('0.0000',$culture).Replace('.','_')
    $geo=@"
SetFactory("OpenCASCADE");
Geometry.OCCTargetUnit="M";
Geometry.OCCFixDegenerated=0;
Geometry.OCCFixSmallEdges=0;
Geometry.OCCFixSmallFaces=0;
Geometry.OCCSewFaces=0;
Geometry.OCCMakeSolids=0;
Merge "asymmetric-mm.step";
v()=Volume{:};
Printf("SOLID_COUNT=%g",#v());
Physical Volume("bumper",1)={v()};
Mesh.MeshSizeMin=$($size.ToString('R',$culture));
Mesh.MeshSizeMax=$($size.ToString('R',$culture));
Mesh.ElementOrder=2;
"@
    $file='mesh-'+$key+'.geo';$mesh='mesh-'+$key+'.msh';[System.IO.File]::WriteAllText((Join-Path $folder $file),$geo)
    $run=Invoke-Gmsh ('mesh-'+$key) @($file,'-3','-nt','4','-format','msh2','-o',$mesh,'-v','4')
    if($run.stdout -notmatch 'SOLID_COUNT=1(?:\s|$)'){throw 'STEP did not retain exactly one closed solid.'}
    $lines=[System.IO.File]::ReadAllLines((Join-Path $folder $mesh));$marker=[Array]::IndexOf($lines,'$Nodes');$count=[int]$lines[$marker+1]
    $zmin=[double]::PositiveInfinity;$zmax=[double]::NegativeInfinity
    for($i=0;$i -lt $count;$i++){$fields=$lines[$marker+2+$i] -split '\s+';$coordinate=[double]::Parse($fields[3],$culture);$zmin=[Math]::Min($zmin,$coordinate);$zmax=[Math]::Max($zmax,$coordinate)}
    if([Math]::Abs(($zmax-$zmin)-.04) -gt 1E-8){throw 'STEP units/bounding-box verification failed.'}
    $elementMarker=[Array]::IndexOf($lines,'$Elements');$elements=[int]$lines[$elementMarker+1];$quadraticTets=0
    for($i=0;$i -lt $elements;$i++){if(($lines[$elementMarker+2+$i] -split '\s+')[1] -eq '11'){$quadraticTets++}}
    if($quadraticTets -eq 0){throw 'No quadratic volume elements were generated.'}
    [pscustomobject]@{meshSizeM=$size;nodes=$count;quadraticTetrahedra=$quadraticTets;heightM=$zmax-$zmin;seconds=$run.seconds;peakWorkingSetBytes=$run.peakWorkingSetBytes;meshSha256=(Get-FileHash -LiteralPath (Join-Path $folder $mesh) -Algorithm SHA256).Hash.ToLowerInvariant()}
}
@{status='STEP import/units/three-mesh preparation only; no contact solve or force convergence claim';mesher='Gmsh 4.15.2 / OpenCASCADE';healing='Explicit healing operations disabled';stepSha256=(Get-FileHash -LiteralPath (Join-Path $folder 'asymmetric-mm.step') -Algorithm SHA256).Hash.ToLowerInvariant();meshes=@($reports)} |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $folder 'verification.json')
$reports | Format-Table meshSizeM,nodes,quadraticTetrahedra,heightM,seconds
