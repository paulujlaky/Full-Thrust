param(
    [ValidateSet('pad', 'liftoff', 'capsule', 'engines', 'night-pad', 'staging', 'earth', 'moon', 'reentry')]
    [string[]]$View = @('pad', 'liftoff', 'capsule'),
    [ValidatePattern('^[1-9][0-9]*x[1-9][0-9]*$')]
    [string]$Window = '1280x720',
    [string]$Output = 'captures/visual-review'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$project = Join-Path $root 'src/FullThrust.Game/FullThrust.Game.csproj'
$game = Join-Path $root 'src/FullThrust.Game/bin/Release/net10.0/win-x64/FullThrust.Game.exe'

$shots = @{

    pad = '--scenario Launchpad --rocket harrier --camera-distance 110 --camera-yaw 65 --camera-pitch 3 --frames 90'
    liftoff = '--scenario Launchpad --rocket harrier --camera-distance 135 --camera-yaw 60 --camera-pitch -8 --stage 1 --sas --throttle 1 --frames 280'
    capsule = '--scenario Launchpad --rocket harrier --camera-distance 7 --camera-yaw -20 --camera-pitch 0 --frames 90'
    engines = '--scenario Launchpad --rocket harrier --camera-part engine-harrier-9 --camera-distance 12 --camera-yaw 58 --camera-pitch -19 --stage 1 --throttle 1 --sas --frames 275'
    'night-pad' = '--scenario Launchpad --rocket harrier --camera-distance 112 --camera-yaw 65 --camera-pitch 3 --sun 70,-16 --frames 180'
    staging = '--scenario LowOrbit --rocket harrier --stage 2 --throttle 0.3 --sas --camera-distance 145 --camera-yaw 68 --camera-pitch 12 --frames 285'
    earth = '--scenario LunarTransfer --rocket harrier --focus 1 --camera-distance 14500000 --camera-yaw 155 --camera-pitch 18 --frames 120'
    moon = '--scenario LunarTransfer --rocket harrier --focus 2 --camera-distance 4100000 --camera-yaw 145 --camera-pitch 4 --frames 120'
    reentry = '--scenario Reentry --rocket concord-return --camera-distance 7 --camera-yaw 80 --camera-pitch -8 --frames 350 --time-scale 8'

}

dotnet build $project -c Release -m:1 --nologo -v:q

if ($LASTEXITCODE -ne 0) {

    exit $LASTEXITCODE

}

$failedViews = [Collections.Generic.List[string]]::new()

foreach ($name in $View) {

    $shot = $shots[$name].Split(' ')
    $destination = Join-Path $Output $name
    Write-Host "Rendering $name at $Window"
    & $game @shot --window $Window --capture-fps 60 --capture-last --no-overlay --no-vsync --capture-dir $destination

    if ($LASTEXITCODE -ne 0) {

        $failedViews.Add($name)
        Write-Warning "Capture failed for $name (exit $LASTEXITCODE)"

    }

}

if ($failedViews.Count -gt 0) {

    Write-Warning ("Failed views: " + ($failedViews -join ', '))
    exit 1

}
