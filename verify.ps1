# Runs every check on .NET 10 and on .NET 11 and prints the two behaviors side by side.
# Each run must show the behavior Microsoft documents for that runtime.
# Run: pwsh ./verify.ps1  (or powershell -ExecutionPolicy Bypass -File verify.ps1)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$failed = 0

& dotnet build -c Release --no-incremental 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

function Run([string] $tfm) {
    $output = & dotnet "bin/Release/$tfm/DotnetUpgradeChecks.dll" 2>&1 | Out-String
    $exit = $LASTEXITCODE
    Write-Host "----- $tfm -----"
    Write-Host $output.TrimEnd()
    Write-Host ''
    $observed = [regex]::Matches($output, 'observed: (.+)') | ForEach-Object { $_.Groups[1].Value.Trim() }
    $names = [regex]::Matches($output, '\[(?:PASS|FAIL)\] (.+)') | ForEach-Object { $_.Groups[1].Value.Trim() } | Select-Object -Skip 1
    return @{ Exit = $exit; Observed = @($observed); Names = @($names) }
}

function WorkerExitCode([string] $tfm) {
    # The failing worker writes its unhandled exception to stderr; that output is expected.
    $ErrorActionPreference = 'Continue'
    & dotnet "bin/Release/$tfm/DotnetUpgradeChecks.dll" worker 2>&1 | Out-Null
    return $LASTEXITCODE
}

Write-Host "SDK $(dotnet --version)"
Write-Host ''
$net10 = Run 'net10.0'
$net11 = Run 'net11.0'

Write-Host '----- .NET 10 vs .NET 11 -----'
for ($i = 0; $i -lt $net11.Names.Count; $i++) {
    Write-Host $net11.Names[$i]
    Write-Host "       .NET 10: $($net10.Observed[$i])"
    Write-Host "       .NET 11: $($net11.Observed[$i])"
}
Write-Host ''

function Check([string] $claim, [bool] $ok, [string] $detail) {
    $label = if ($ok) { 'PASS' } else { 'FAIL' }
    Write-Host "[$label] $claim"
    Write-Host "       $detail"
    if (-not $ok) { $script:failed++ }
}

Check 'Every check shows the documented .NET 10 behavior on .NET 10' ($net10.Exit -eq 0) "exit code $($net10.Exit)"
Check 'Every check shows the documented .NET 11 behavior on .NET 11' ($net11.Exit -eq 0) "exit code $($net11.Exit)"

$exit10 = WorkerExitCode 'net10.0'
$exit11 = WorkerExitCode 'net11.0'
Check 'A failing BackgroundService: process exit code' ($exit10 -eq 0 -and $exit11 -ne 0) ".NET 10: $exit10, .NET 11: $exit11"

Write-Host ''
if ($failed -eq 0) { Write-Host 'verify.ps1: all checks passed.'; exit 0 }
Write-Host "verify.ps1: $failed check(s) failed."; exit 1
