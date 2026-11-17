# RimLua M0 build script (direct csc compile; avoids MSBuild temp-dir issues in some sandboxes)
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools/build.ps1
#   powershell -ExecutionPolicy Bypass -File tools/build.ps1 -Configuration Debug
param(
    [string]$Configuration = "Release",
    [string]$GameDir = "",
    [string]$SdkRoot = ""
)

$ErrorActionPreference = "Stop"

$modRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($GameDir)) {
    if (-not [string]::IsNullOrEmpty($env:RIMWORLD_DIR)) { $GameDir = $env:RIMWORLD_DIR }
    else { $GameDir = Split-Path -Parent (Split-Path -Parent $modRoot) }
}
$asmCSharp = Join-Path $GameDir "Assembly-CSharp.dll"
if (-not (Test-Path $asmCSharp)) { throw "Invalid GameDir: $asmCSharp not found (use -GameDir)" }

if ([string]::IsNullOrEmpty($SdkRoot)) {
    if (-not [string]::IsNullOrEmpty($env:RIMLUA_SDK_ROOT)) { $SdkRoot = $env:RIMLUA_SDK_ROOT }
    else {
        $candidates = @(
            (Join-Path $modRoot ".zcode\dotnet10"),
            (Join-Path (Split-Path -Parent $modRoot) "PioneerFrameworkLite\.zcode\dotnet10"),
            "C:\Program Files\dotnet"
        )
        foreach ($c in $candidates) { if (Test-Path (Join-Path $c "dotnet.exe")) { $SdkRoot = $c; break } }
    }
}
if ([string]::IsNullOrEmpty($SdkRoot)) { throw "No .NET SDK root found; set -SdkRoot or RIMLUA_SDK_ROOT" }

$systemDotnet = "C:\Program Files\dotnet\dotnet.exe"
$systemSdkDir = "C:\Program Files\dotnet\sdk"
$dotnet = $systemDotnet
$csc = $null
if (Test-Path $systemDotnet) {
    $csc = Get-ChildItem $systemSdkDir -Directory -ErrorAction SilentlyContinue |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "Roslyn\bincore\csc.dll" } |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $csc) {
    $dotnet = Join-Path $SdkRoot "dotnet.exe"
    $csc = Get-ChildItem (Join-Path $SdkRoot "sdk") -Directory -ErrorAction SilentlyContinue |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "Roslyn\bincore\csc.dll" } |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $csc) { throw "No csc.dll found (system SDK or portable SDK)" }

$refRoots = @(
    (Join-Path $SdkRoot "packs\Microsoft.NETCore.App.Ref"),
    "C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref"
)
$refDir = $null
foreach ($root in $refRoots) {
    if (-not (Test-Path $root)) { continue }
    $candidate = Get-ChildItem $root -Directory |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "ref\net10.0" } |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($candidate) { $refDir = $candidate; break }
}
if (-not $refDir) { throw "No net10.0 reference assemblies found (looked in $($refRoots -join ', '))" }

$moonDll = Join-Path $modRoot "ThirdParty\MoonSharp\MoonSharp.Interpreter.dll"
if (-not (Test-Path $moonDll)) { throw "Missing $moonDll" }

$outDir = Join-Path $modRoot "1.7\Assemblies"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$outDll = Join-Path $outDir "RimLua.dll"
if (Test-Path $outDll) { Remove-Item -LiteralPath $outDll -Force }

$srcDir = Join-Path $modRoot "Source\RimLua"
$sources = Get-ChildItem $srcDir -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch "\\obj\\|\\bin\\" } | ForEach-Object { $_.FullName }
if ($sources.Count -eq 0) { throw "No source files found" }

$lines = New-Object System.Collections.Generic.List[string]
foreach ($r in (Get-ChildItem $refDir -Filter *.dll)) { $lines.Add('/r:"' + $r.FullName + '"') }
$lines.Add('/r:"' + $asmCSharp + '"')
$lines.Add('/r:"' + (Join-Path $GameDir "UnityStubs.dll") + '"')
$lines.Add('/r:"' + (Join-Path $GameDir "Waylith.dll") + '"')
$lines.Add('/r:"' + $moonDll + '"')
foreach ($s in $sources) { $lines.Add('"' + $s + '"') }

$buildDir = Join-Path $modRoot ".build"
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
$rsp = Join-Path $buildDir "rimlua.rsp"
Set-Content -LiteralPath $rsp -Value $lines -Encoding ASCII

$cscArgs = @("exec", $csc, "-noconfig", "-nostdlib+", "-target:library", "-langversion:latest", "-nullable:disable", "-deterministic", "-debug-", "-out:$outDll", "@$rsp")
& $dotnet @cscArgs
if ($LASTEXITCODE -ne 0) { throw "csc failed (exit $LASTEXITCODE)" }

Copy-Item -LiteralPath $moonDll -Destination (Join-Path $outDir "MoonSharp.Interpreter.dll") -Force
Write-Host "OK: $outDll (refs: $refDir)"
Get-ChildItem $outDir -File | Select-Object Name, Length | Format-Table -AutoSize
