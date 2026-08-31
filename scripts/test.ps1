$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

Write-Host "=== Running tests ===" -ForegroundColor Green

Write-Host "=== Current documentation checks ===" -ForegroundColor Green
& (Join-Path $PSScriptRoot "check-current-docs.ps1") -Root $repoRoot

Write-Host ""
Write-Host "=== dic localization checks ===" -ForegroundColor Green
& (Join-Path $PSScriptRoot "validate-dic-localization.ps1") -Root $repoRoot

Write-Host ""
Write-Host "=== Backend tests ===" -ForegroundColor Green
# spawn pwsh 的用例已全部收进 RepositoryScripts.Tests，并在该程序集内串行；
# 项目之间因此可以并行。
dotnet test ICP-Platform.sln --configuration Release
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "=== Model DTO synchronization check ===" -ForegroundColor Green
dotnet run `
    --project server/tools/model-dto-sync/ModelDtoSync.csproj `
    --configuration Release `
    --no-build `
    --no-restore `
    -- `
    --check $repoRoot
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "=== Frontend tests ===" -ForegroundColor Green
Set-Location $repoRoot
corepack pnpm --dir frontend test
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "=== Tests complete ===" -ForegroundColor Green
