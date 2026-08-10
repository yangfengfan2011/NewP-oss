[CmdletBinding()]
param(
    [ValidateSet("IdentityFoundation", "Personnel", "Plugin", "All")]
    [string]$Gate = "All"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$outputRoot = Join-Path $repoRoot ".codex-temp\acceptance"
$runnerProject = Join-Path $repoRoot "tests\acceptance\Acceptance.Runner\Acceptance.Runner.csproj"

dotnet run `
    --project $runnerProject `
    --configuration Release `
    -- `
    --gate $Gate `
    --repository-root $repoRoot `
    --output-root $outputRoot

exit $LASTEXITCODE
