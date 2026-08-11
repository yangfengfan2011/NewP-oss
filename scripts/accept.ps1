[CmdletBinding()]
param(
    [ValidateSet("IdentityFoundation", "Personnel", "Plugin", "All")]
    [string]$Gate,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$outputRoot = Join-Path $repoRoot ".codex-temp\acceptance"
$runnerProject = Join-Path $repoRoot "tests\acceptance\Acceptance.Runner\Acceptance.Runner.csproj"

if ([string]::IsNullOrWhiteSpace($Gate)) {
    [Console]::Error.WriteLine("ACCEPT_GATE_REQUIRED: specify -Gate IdentityFoundation, Personnel, Plugin, or All.")
    exit 2
}

$arguments = @(
    "run",
    "--project", $runnerProject,
    "--configuration", "Release"
)
if ($NoBuild.IsPresent) {
    $arguments += @("--no-build", "--no-restore")
}
$arguments += @(
    "--",
    "--gate", $Gate,
    "--repository-root", $repoRoot,
    "--output-root", $outputRoot
)

dotnet @arguments

exit $LASTEXITCODE
