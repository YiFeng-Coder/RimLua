# Build the RimLua sample extension with csc (same approach as the main build script).
param(
    [string]$GameDir = "",
    [string]$SdkRoot = ""
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$modRoot = Split-Path -Parent (Split-Path -Parent $here)   # AIStory
if ([string]::IsNullOrEmpty($GameDir)) {
    if (-not [string]::IsNullOrEmpty($env:RIMWORLD_DIR)) { $GameDir = $env:RIMWORLD_DIR }
    else { $GameDir = Split-Path -Parent (Split-Path -Parent $modRoot) }
}
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
$systemDotnet = "C:\Program Files\dotnet\dotnet.exe"
$csc = Get-ChildItem "C:\Program Files\dotnet\sdk" -Directory -ErrorAction SilentlyContinue |
    Sort-Object { [version]$_.Name } -Descending |
    ForEach-Object { Join-Path $_.FullName "Roslyn\bincore\csc.dll" } |
    Where-Object { Test-Path $_ } | Select-Object -First 1
$dotnet = $systemDotnet
if (-not $csc) {
    $dotnet = Join-Path $SdkRoot "dotnet.exe"
    $csc = Get-ChildItem (Join-Path $SdkRoot "sdk") -Directory |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "Roslyn\bincore\csc.dll" } |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $csc) { throw "csc.dll not found" }

$refRoots = @((Join-Path $SdkRoot "packs\Microsoft.NETCore.App.Ref"), "C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref")
$refDir = $null
foreach ($root in $refRoots) {
    if (-not (Test-Path $root)) { continue }
    $candidate = Get-ChildItem $root -Directory | Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "ref\net10.0" } | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($candidate) { $refDir = $candidate; break }
}
if (-not $refDir) { throw "net10.0 reference assemblies not found" }

$rimlua = Join-Path $modRoot "1.7\Assemblies\RimLua.dll"
if (-not (Test-Path $rimlua)) { throw "Missing $rimlua (build RimLua first)" }

$outDir = Join-Path $here "Assemblies"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$outDll = Join-Path $outDir "RimLuaSample.dll"
if (Test-Path $outDll) { Remove-Item -LiteralPath $outDll -Force }

$sources = Get-ChildItem (Join-Path $here "Source\RimLuaSample") -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch "\\obj\\|\\bin\\" } | ForEach-Object { $_.FullName }
$lines = New-Object System.Collections.Generic.List[string]
foreach ($r in (Get-ChildItem $refDir -Filter *.dll)) { $lines.Add('/r:"' + $r.FullName + '"') }
$lines.Add('/r:"' + (Join-Path $GameDir "Assembly-CSharp.dll") + '"')
$lines.Add('/r:"' + (Join-Path $GameDir "UnityStubs.dll") + '"')
$lines.Add('/r:"' + (Join-Path $GameDir "Waylith.dll") + '"')
$lines.Add('/r:"' + $rimlua + '"')
foreach ($s in $sources) { $lines.Add('"' + $s + '"') }

$buildDir = Join-Path $here ".build"
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
$rsp = Join-Path $buildDir "sample.rsp"
Set-Content -LiteralPath $rsp -Value $lines -Encoding ASCII
$args = @("exec", $csc, "-noconfig", "-nostdlib+", "-target:library", "-langversion:latest", "-nullable:disable", "-deterministic", "-debug-", "-out:$outDll", "@$rsp")
& $dotnet @args
if ($LASTEXITCODE -ne 0) { throw "csc failed (exit $LASTEXITCODE)" }
Write-Host "OK: $outDll"
