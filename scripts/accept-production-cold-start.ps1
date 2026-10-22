[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Baseline", "Candidate", "Qualification")]
    [string]$Mode,
    [string]$BaselineResult
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$outputRoot = Join-Path $repoRoot ".codex-temp\acceptance-production-cold-start"
$runnerProject = Join-Path $repoRoot "tests\acceptance\Acceptance.Runner\Acceptance.Runner.csproj"

if ($Mode -eq "Candidate" -and [string]::IsNullOrWhiteSpace($BaselineResult)) {
    [Console]::Error.WriteLine("ACC_PRODUCTION_COLD_START_BASELINE_REQUIRED: Candidate requires -BaselineResult.")
    exit 2
}

$arguments = @(
    "run",
    "--project", $runnerProject,
    "--configuration", "Release",
    "--",
    "--production-cold-start-mode", $Mode,
    "--repository-root", $repoRoot,
    "--output-root", $outputRoot
)
if (-not [string]::IsNullOrWhiteSpace($BaselineResult)) {
    $resolvedBaseline = (Resolve-Path -LiteralPath $BaselineResult).Path
    $arguments += @("--baseline-result", $resolvedBaseline)
}

dotnet @arguments
exit $LASTEXITCODE
