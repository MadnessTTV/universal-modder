# Compile UmMegabonkStudio against this profile's BepInEx interop. Does not launch the game
# and does not copy the DLL unless -Install is passed.
param(
    [string]$BepInEx = (Join-Path $env:APPDATA "Thunderstore Mod Manager\DataFolder\Megabonk\profiles\Default\BepInEx"),
    [switch]$Install
)
$ErrorActionPreference = "Stop"
$dotnet = $null
$cmd = Get-Command dotnet -ErrorAction SilentlyContinue
if ($cmd) { $dotnet = $cmd.Source }
$fallback = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
if (-not $dotnet -and (Test-Path -LiteralPath $fallback)) { $dotnet = $fallback }
if (-not $dotnet) {
    Write-Output "dotnet.exe was not found."
    Write-Output "Install the .NET 10 SDK, then open a new terminal and rerun this script:"
    Write-Output "  winget install Microsoft.DotNet.SDK.10"
    Write-Output "https://learn.microsoft.com/en-us/dotnet/core/install/windows"
    exit 1
}
$sdkLines = @(& $dotnet --list-sdks 2>&1 | ForEach-Object { "$_" } | Where-Object { $_ -match '^\d+\.\d+\.\d+' })
if ($sdkLines.Count -eq 0) {
    Write-Output "Found $dotnet but dotnet --list-sdks is empty. That is the runtime host, not an SDK."
    Write-Output "The plugin stays TargetFramework net6.0 so BepInEx's .NET 6.0.7 host can load it."
    Write-Output "Compile it with the .NET 10 SDK (current LTS, supported until 15 November 2028)."
    Write-Output "The .NET 6 SDK left support on 13 November 2024. Do not install that SDK."
    Write-Output "Install, then open a new terminal so PATH sees the SDK:"
    Write-Output "  winget install Microsoft.DotNet.SDK.10"
    Write-Output "https://learn.microsoft.com/en-us/dotnet/core/install/windows"
    Write-Output "The first build restores Microsoft.NETCore.App.Ref for net6.0 from nuget.org. That pack is the compile-time reference surface. It is not copied into the plugin."
    exit 1
}
Write-Output "SDKs:"
$sdkLines | ForEach-Object { Write-Output ("  " + $_) }
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
& $dotnet build $proj -c Release "-p:MegabonkBepInEx=$BepInEx" "-p:CheckEolTargetFramework=false"
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
