$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$testProject = Join-Path $repoRoot "tests\Persistence.Tests\Persistence.Tests.csproj"

$env:NEWP_RUN_SQLSERVER_SMOKE = "1"

Push-Location $repoRoot
try {
    dotnet test $testProject `
        --filter "Category=SqlServerSmoke" `
        --no-restore `
        --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "SQL Server persistence smoke failed."
    }
}
finally {
    Pop-Location
    Remove-Item Env:NEWP_RUN_SQLSERVER_SMOKE -ErrorAction SilentlyContinue
}
