param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",
    [string]$Version = "1.0.0",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root "dist/windows/$Runtime"
$archive = Join-Path $root "dist/AIUsageMeter-$Version-$Runtime.zip"

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

if (Test-Path $archive) { Remove-Item $archive -Force }
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -Algorithm SHA256 $archive).Hash.ToLowerInvariant()
$checksum = "$hash  $(Split-Path -Leaf $archive)"
Set-Content -Path (Join-Path $root "dist/SHA256SUMS-windows-$Runtime.txt") -Value $checksum -Encoding ascii
Write-Host "Created $archive"
Write-Host $checksum
