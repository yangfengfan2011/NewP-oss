$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

Write-Host "=== Running tests ===" -ForegroundColor Green

Write-Host "=== Backend tests ===" -ForegroundColor Green
dotnet test ICP-Platform.sln --configuration Release
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
Write-Host "=== AI-first design checks ===" -ForegroundColor Green
Set-Location $repoRoot
& (Join-Path $PSScriptRoot "check-ai-first-design-targets.ps1")

Write-Host ""
Write-Host "=== dic localization checks ===" -ForegroundColor Green
& (Join-Path $PSScriptRoot "validate-dic-localization.ps1") -Root $repoRoot

Write-Host ""
Write-Host "=== Tests complete ===" -ForegroundColor Green
