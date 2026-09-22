# Builds the solution and runs the unit tests.
#   .\build.ps1            Debug build + tests
#   .\build.ps1 -Release   Release build + tests
#   .\build.ps1 -NoTest    skip tests
param(
    [switch]$Release,
    [switch]$NoTest
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$config = if ($Release) { 'Release' } else { 'Debug' }

dotnet build LinkVault.sln -c $config --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $NoTest) {
    dotnet test LinkVault.sln -c $config --no-build --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
