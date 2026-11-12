param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$Run
)

$ErrorActionPreference = "Stop"

$msbuildCandidates = @(
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\amd64\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\amd64\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
)

$msbuild = $msbuildCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $msbuild) {
    $cmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($cmd) { $msbuild = $cmd.Source }
}

if (-not $msbuild) {
    throw "MSBuild.exe was not found on this machine."
}

$exe = Join-Path $PSScriptRoot "bin\$Configuration\Bins.exe"
$running = Get-Process -Name "Bins" -ErrorAction SilentlyContinue
if ($running) {
    if (Test-Path $exe) {
        try { & $exe --quit | Out-Null; Start-Sleep -Milliseconds 300 } catch { }
    }
    Get-Process -Name "Bins" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 200
}

$proj = Join-Path $PSScriptRoot "Bins.csproj"
& $msbuild $proj /p:Configuration=$Configuration /p:Platform=x64 /v:minimal
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Build succeeded -> $exe" -ForegroundColor Green

if ($Run) {
    Start-Process -FilePath $exe
}
