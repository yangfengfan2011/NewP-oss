$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

Write-Host "=== Running tests ===" -ForegroundColor Green

Write-Host "=== AI-first design checks ===" -ForegroundColor Green
& (Join-Path $PSScriptRoot "check-ai-first-design-targets.ps1")

Write-Host ""
Write-Host "=== dic localization checks ===" -ForegroundColor Green
& (Join-Path $PSScriptRoot "validate-dic-localization.ps1") -Root $repoRoot

Write-Host ""
Write-Host "=== Backend tests ===" -ForegroundColor Green
# Windows 下多个测试项目并行启动 PowerShell 子进程会争用同名 shim；保持同一 solution 入口但串行调度项目。
dotnet test ICP-Platform.sln --configuration Release --maxcpucount:1
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
