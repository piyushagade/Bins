param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$Run,
    [switch]$Package
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

if ($Package) {
    if ($Configuration -ne "Release") {
        throw "Packaging requires -Configuration Release."
    }

    $isccCandidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    $iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) {
        $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($cmd) { $iscc = $cmd.Source }
    }
    if (-not $iscc) {
        throw "Inno Setup 6 (ISCC.exe) was not found. Install it with: winget install JRSoftware.InnoSetup"
    }

    $asmInfo = Get-Content (Join-Path $PSScriptRoot "Properties\AssemblyInfo.cs") -Raw
    if ($asmInfo -match 'public const string Current = "([^"]+)"') {
        $version = $Matches[1]
    } else {
        throw "Could not determine AppVersion.Current from Properties\AssemblyInfo.cs."
    }

    $iss = Join-Path $PSScriptRoot "installer\Bins.iss"
    & $iscc "/DMyAppVersion=$version" $iss
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    $setupExe = Join-Path $PSScriptRoot "dist\Bins-Setup-$version-x64.exe"
    $portableZip = Join-Path $PSScriptRoot "dist\Bins-Portable-$version-x64.zip"
    if (Test-Path $portableZip) { Remove-Item -Force $portableZip }
    Compress-Archive -Path @(
        (Join-Path $PSScriptRoot "bin\Release\Bins.exe"),
        (Join-Path $PSScriptRoot "app.ico"),
        (Join-Path $PSScriptRoot "README.md")
    ) -DestinationPath $portableZip -CompressionLevel Optimal

    $hash = (Get-FileHash -Path $setupExe -Algorithm SHA256).Hash
    $zipHash = (Get-FileHash -Path $portableZip -Algorithm SHA256).Hash
    $shaFile = Join-Path $PSScriptRoot "dist\SHA256SUMS.txt"
    @(
        "$hash  Bins-Setup-$version-x64.exe"
        "$zipHash  Bins-Portable-$version-x64.zip"
    ) | Set-Content -Path $shaFile -Encoding ascii
    Write-Host "Installer built -> $setupExe" -ForegroundColor Green
    Write-Host "Portable zip   -> $portableZip" -ForegroundColor Green
    Write-Host "Setup SHA256:    $hash" -ForegroundColor Cyan
    Write-Host "Portable SHA256: $zipHash" -ForegroundColor Cyan
}

if ($Run) {
    Start-Process -FilePath $exe
}
