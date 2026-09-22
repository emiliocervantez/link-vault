# Publishes LinkVault as a single-file exe for win-x64.
#   .\publish.ps1                    -> .\publish\LinkVault.exe, .NET bundled (runs anywhere)
#   .\publish.ps1 --bundle=false     -> small exe, needs the .NET 8 Desktop Runtime on the target machine
#   .\publish.ps1 -Output C:\tmp     -> C:\tmp\LinkVault.exe
#   .\publish.ps1 -StopRunning       stop a LinkVault started from the output folder first
param(
    [string]$Output = '',
    [switch]$StopRunning,
    [ValidateSet('true', 'false')][string]$Bundle = 'true',
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
# Not a parameter default: in Windows PowerShell 5.1, $PSScriptRoot is empty while defaults are evaluated
# once the script carries a [Parameter()] attribute.
if (-not $Output) { $Output = Join-Path $PSScriptRoot 'publish' }

# Accept the --bundle=true|false spelling as well as -Bundle true|false.
foreach ($arg in $Rest) {
    if ($arg -match '^--bundle=(true|false)$') { $Bundle = $Matches[1] }
    else { Write-Error "Unknown argument '$arg'. Use --bundle=true|false, -Output <dir>, -StopRunning." }
}
$selfContained = $Bundle -eq 'true'

$target = Join-Path $Output 'LinkVault.exe'
$running = Get-Process LinkVault -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $target }
if ($running) {
    if ($StopRunning) {
        $running | Stop-Process -Force
        Start-Sleep -Seconds 1
    } else {
        Write-Error "LinkVault is running from $target (pid $($running.Id -join ', ')). Exit it from the tray icon or rerun with -StopRunning."
    }
}

dotnet publish src\LinkVault\LinkVault.csproj -c Release -r win-x64 --self-contained $selfContained.ToString().ToLower() `
    -p:PublishSingleFile=true -o $Output --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Remove-Item (Join-Path $Output '*.pdb') -ErrorAction SilentlyContinue
$exe = Get-Item (Join-Path $Output 'LinkVault.exe')
$mode = if ($selfContained) { '.NET bundled' } else { 'needs .NET 8 Desktop Runtime on the target' }
"Published $($exe.FullName) ($([math]::Round($exe.Length / 1MB, 1)) MB, $mode)"
