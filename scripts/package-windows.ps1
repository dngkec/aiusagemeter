param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",
    [string]$Version = "1.1.0",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root "dist/windows/$Runtime"

if (-not $NoBuild) {
    dotnet publish (Join-Path $root "src/AIUsageMeter.Windows/AIUsageMeter.Windows.csproj") `
        --configuration Release --runtime $Runtime --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None -p:DebugSymbols=false --output $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
}

if (-not (Test-Path (Join-Path $publish "AIUsageMeter.exe"))) {
    throw "Published executable not found at $publish"
}

# The installer. ISCC is not on PATH after either a winget or a choco install, so look where the
# installer itself puts it before giving up.
$iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) { throw "ISCC.exe not found. Install Inno Setup 6 (winget install JRSoftware.InnoSetup)." }

$setupBase = "AIUsageMeter-$Version-$Runtime-setup"
& $iscc /Qp `
    "/DAppVersion=$Version" `
    "/DSourceExe=$(Join-Path $publish 'AIUsageMeter.exe')" `
    "/DArch=$($Runtime -replace '^win-', '')" `
    "/DOutputDir=$(Join-Path $root 'dist')" `
    "/DOutputBase=$setupBase" `
    (Join-Path $PSScriptRoot "windows-installer.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

$setup = Join-Path $root "dist/$setupBase.exe"
if (-not (Test-Path $setup)) { throw "Installer not found at $setup" }

$checksum = "$((Get-FileHash -Algorithm SHA256 $setup).Hash.ToLowerInvariant())  $(Split-Path -Leaf $setup)"
Set-Content -Path (Join-Path $root "dist/SHA256SUMS-windows-$Runtime.txt") -Value $checksum -Encoding ascii
Write-Host "Created $setup"
Write-Host $checksum
