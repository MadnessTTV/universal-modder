# Compile UmMegabonkStudio against this profile's BepInEx interop. Does not launch the game
# and does not copy the DLL unless -Install is passed.
param(
    [string]$BepInEx = (Join-Path $env:APPDATA "Thunderstore Mod Manager\DataFolder\Megabonk\profiles\Default\BepInEx"),
    [switch]$Install
)
$ErrorActionPreference = "Stop"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet is not on PATH. Install the .NET 6 SDK (the game log showed runtime 6.0.7; the SDK is separate). Then run this script again."
    exit 1
}
$need = @(
    "core\BepInEx.Core.dll",
    "core\BepInEx.Unity.IL2CPP.dll",
    "core\0Harmony.dll",
    "core\Il2CppInterop.Runtime.dll",
    "interop\Assembly-CSharp.dll",
    "interop\UnityEngine.dll",
    "interop\UnityEngine.CoreModule.dll",
    "interop\UnityEngine.IMGUIModule.dll",
    "interop\UnityEngine.InputLegacyModule.dll"
)
foreach ($rel in $need) {
    $p = Join-Path $BepInEx $rel
    if (-not (Test-Path -LiteralPath $p)) {
        Write-Error "missing $p"
        exit 1
    }
    $item = Get-Item -LiteralPath $p
    Write-Output ("{0} {1}" -f $rel, $item.Length)
}
$proj = Join-Path $PSScriptRoot "UmMegabonkStudio.csproj"
& dotnet build $proj -c Release "-p:MegabonkBepInEx=$BepInEx"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed. Paste the first error line (the missing type or method). Do not send any DLL from interop or core."
    exit $LASTEXITCODE
}
$dll = Join-Path $PSScriptRoot "bin\Release\UmMegabonkStudio.dll"
Write-Output "built $dll"
if (-not $Install) {
    Write-Output "Not installed. Re-run with -Install to copy into the profile, or copy the DLL yourself."
    exit 0
}
$destDir = Join-Path $BepInEx "plugins\UmMegabonkStudio"
New-Item -ItemType Directory -Force -Path $destDir | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $destDir "UmMegabonkStudio.dll") -Force
$json = Join-Path $destDir "characters.json"
$starter = Join-Path (Split-Path $PSScriptRoot -Parent) "studio\characters.json"
if (-not (Test-Path -LiteralPath $json) -and (Test-Path -LiteralPath $starter)) {
    Copy-Item -LiteralPath $starter -Destination $json
    Write-Output "copied starter characters.json (the profile did not already have one)"
}
Write-Output "installed into $destDir"
Write-Output "Restart the game. In BepInEx\LogOutput.log look for 'Registered' and 'Studio UI OnGUI ran'."
