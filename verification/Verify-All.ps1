<#
.SYNOPSIS
    Clean-builds every lesson solution under Projects/ and reports warnings and errors.

.DESCRIPTION
    Private verification harness - never shown in a lesson. Deletes bin/, obj/ and .vs/ under
    Projects/ first so no incremental build hides a warning, then runs `dotnet build` on each
    Projects/*/*.slnx. Exits non-zero if any solution reports a warning or an error.

.PARAMETER Filter
    Optional wildcard to build a subset, e.g. 'Lesson0*' or 'Skills*'.

.PARAMETER KeepArtifacts
    Leave bin/ and obj/ in place after the run (they are deleted by default).
#>
[CmdletBinding()]
param(
    [string]$Filter = '*',
    [switch]$KeepArtifacts
)

$ErrorActionPreference = 'Stop'
$projectsRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'Projects'

function Remove-BuildArtifacts {
    Get-ChildItem $projectsRoot -Recurse -Directory -Force -Include bin, obj, .vs, TestResults |
        Sort-Object FullName -Descending |
        ForEach-Object { if (Test-Path $_.FullName) { Remove-Item $_.FullName -Recurse -Force } }
}

Remove-BuildArtifacts

$results = foreach ($solution in Get-ChildItem $projectsRoot -Filter *.slnx -Recurse -Depth 1 |
        Where-Object { $_.Directory.Name -like $Filter } | Sort-Object FullName) {
    Write-Host "Building $($solution.Directory.Name)/$($solution.Name)..." -ForegroundColor Cyan
    $output = & dotnet build $solution.FullName -consoleLoggerParameters:NoSummary 2>&1 | Out-String
    $exitCode = $LASTEXITCODE
    $warnings = @([regex]::Matches($output, '(?m)^.*: warning [A-Z]+\d+.*$') | ForEach-Object { ($_.Value -replace '\s+\[[^\]]+\]\s*$', '').Trim() } | Sort-Object -Unique)
    $errors = @([regex]::Matches($output, '(?m)^.*: error [A-Z]+\d+.*$') | ForEach-Object { ($_.Value -replace '\s+\[[^\]]+\]\s*$', '').Trim() } | Sort-Object -Unique)
    $warnings | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
    $errors | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    [pscustomobject]@{
        Lesson   = $solution.Directory.Name
        Warnings = $warnings.Count
        Errors   = $errors.Count
        ExitCode = $exitCode
    }
}

if (-not $KeepArtifacts) { Remove-BuildArtifacts }

$results | Format-Table -AutoSize | Out-String | Write-Host
$failed = @($results | Where-Object { $_.Warnings -gt 0 -or $_.Errors -gt 0 -or $_.ExitCode -ne 0 })
if ($failed.Count -gt 0) {
    Write-Host "$($failed.Count) of $(@($results).Count) solutions are not clean." -ForegroundColor Red
    exit 1
}
Write-Host "All $(@($results).Count) solutions built with 0 warnings and 0 errors." -ForegroundColor Green
