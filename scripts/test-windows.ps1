param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Filter,
    [switch]$NoBuild
)

# Runs the Windows test suites by launching the test executables directly.
#
# Microsoft.Testing.Platform builds each test project as an ordinary executable, and running that
# executable is the platform's own entry point. `dotnet test` only wraps it: on the .NET 10 SDK it
# drives the same executable over a named pipe in server mode, and where that handshake does not
# come off the run reports "Zero tests ran" with exit code 5 and no failing test to point at. The
# suites themselves are fine — this skips the wrapper rather than the tests.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$suites = @(
    @{ Name = "AIUsageMeter.Core.Tests"; Framework = "net8.0" }
    @{ Name = "AIUsageMeter.Windows.Tests"; Framework = "net8.0-windows10.0.19041.0" }
)

if (-not $NoBuild) {
    dotnet build (Join-Path $root "AIUsageMeter.Windows.sln") --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { Write-Error "build failed"; exit 1 }
}

$failed = @()
foreach ($suite in $suites) {
    $exe = Join-Path $root "WindowsTests/$($suite.Name)/bin/$Configuration/$($suite.Framework)/$($suite.Name).exe"
    if (-not (Test-Path $exe)) { Write-Error "Test executable not found at $exe"; exit 1 }

    Write-Host ""
    Write-Host "== $($suite.Name) ==" -ForegroundColor Cyan
    if ($Filter) { & $exe --filter $Filter } else { & $exe }

    # 0 is a clean run; 5 means the platform found no test to run, which for these suites is a
    # broken build or a filter that matched nothing, never a reason to call the run green.
    if ($LASTEXITCODE -ne 0) { $failed += "$($suite.Name) (exit $LASTEXITCODE)" }
}

# An uncaught `throw` still leaves powershell.exe -File returning 0, which would hand CI a green
# tick over a red suite. Set the exit code by hand.
if ($failed.Count -gt 0) {
    Write-Host ""
    Write-Error "Failing suites: $($failed -join ', ')"
    exit 1
}

Write-Host ""
Write-Host "All Windows suites passed." -ForegroundColor Green
exit 0
