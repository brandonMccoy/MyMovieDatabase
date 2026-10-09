<#
.SYNOPSIS
    CI-style check: restore, build everything with warnings as errors, run the tests.
    Exits non-zero on the first failing step.

.PARAMETER SkipApp
    Don't build the MAUI app (Android + Windows), the slowest part. Use while iterating;
    run without it before committing.

.PARAMETER Full
    Also run E2E tests (Category=E2E). The default is the fast suite.

.EXAMPLE
    ./scripts/test.ps1
    ./scripts/test.ps1 -SkipApp
#>
param(
    [switch]$SkipApp,
    [switch]$Full,
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$solution = Join-Path $root 'MyMovieDatabase.slnx'

function Invoke-Step([string]$Name, [scriptblock]$Command) {
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $Name" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Invoke-Step 'Restore' { dotnet restore $solution --nologo -v q -clp:NoSummary }

if ($SkipApp) {
    $projects = Get-ChildItem (Join-Path $root 'src'), (Join-Path $root 'tests') -Recurse -Filter *.csproj |
        Where-Object Name -ne 'MMDb.App.csproj'
    foreach ($project in $projects) {
        Invoke-Step "Build $($project.BaseName)" { dotnet build $project.FullName -c $Configuration --no-restore --nologo -v q -clp:NoSummary }
    }
}
else {
    Invoke-Step 'Build solution' { dotnet build $solution -c $Configuration --no-restore --nologo -v q -clp:NoSummary }
}

$filter = if ($Full) { @() } else { @('--filter-not-trait', 'Category=E2E') }
Invoke-Step 'Test' { dotnet test --solution $solution -c $Configuration --no-build @filter }

Write-Host 'All green.' -ForegroundColor Green
