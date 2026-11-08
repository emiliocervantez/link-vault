# Builds LinkVault.exe and publishes it as a GitHub Release. Needs the GitHub CLI (gh), logged in.
#   .\scripts\release.ps1 v1.1.0                  -> tests, publish (small exe), tag v1.1.0 on master, release with the exe
#   .\scripts\release.ps1 v1.1.0 -Bundle true     -> same with the .NET bundled exe
#   .\scripts\release.ps1 v1.0.0 -Replace         -> rebuild and replace the exe of an existing release
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^v\d+\.\d+\.\d+$')][string]$Version,
    [ValidateSet('true', 'false')][string]$Bundle = 'false',
    [switch]$Replace
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
Set-Location $root

$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh) { $gh = Join-Path $env:ProgramFiles 'GitHub CLI\gh.exe' }
if (-not (Test-Path $gh)) { Write-Error 'GitHub CLI not found. Install it: winget install --id GitHub.cli --source winget' }

# The release is tagged on origin/master, so it must hold exactly what gets built.
if (git status --porcelain) { Write-Error 'Uncommitted changes. Commit or stash them first.' }
git fetch -q origin
if ((git rev-parse HEAD) -ne (git rev-parse origin/master)) { Write-Error 'HEAD is not origin/master. Check out master and push first.' }

# Under 'Stop', Windows PowerShell turns a native command's redirected stderr into a terminating error.
$ErrorActionPreference = 'Continue'
& $gh release view $Version *> $null
$exists = $LASTEXITCODE -eq 0
$ErrorActionPreference = 'Stop'
if ($exists -and -not $Replace) { Write-Error "Release $Version exists. Pick a new version, or use -Replace to swap its exe." }
if ($Replace -and -not $exists) { Write-Error "Release $Version does not exist; drop -Replace to create it." }

& (Join-Path $root 'build.ps1') -Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& (Join-Path $root 'publish.ps1') -StopRunning -Bundle $Bundle
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $root 'publish\LinkVault.exe'
if ($Replace) {
    & $gh release upload $Version $exe --clobber
} else {
    $runtime = if ($Bundle -eq 'true') { '.NET bundled, nothing else to install.' }
               else { 'Requires the .NET 8 Desktop Runtime: https://dotnet.microsoft.com/download/dotnet/8.0' }
    & $gh release create $Version $exe --target master --title "LinkVault $Version" --generate-notes `
        --notes "Single-file LinkVault.exe for Windows 10/11 x64. $runtime"
}
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
