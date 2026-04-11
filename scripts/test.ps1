$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

Write-Host "=== Running tests ===" -ForegroundColor Green

$backendTests = @(
    "tests\Kernel.Tests\Kernel.Tests.csproj",
    "tests\Files.Tests\Files.Tests.csproj",
    "tests\Audit.Tests\Audit.Tests.csproj",
    "tests\Authorization.Tests\Authorization.Tests.csproj",
    "tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj"
)

Write-Host "=== Backend tests ===" -ForegroundColor Green
foreach ($project in $backendTests) {
    if (Test-Path $project) {
        Write-Host "Running $project" -ForegroundColor Cyan
        dotnet test $project
    } else {
        Write-Host "Missing backend test project: $project" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "=== Frontend tests ===" -ForegroundColor Green
Set-Location (Join-Path $repoRoot "frontend")
$packageJson = Get-Content package.json -Raw -ErrorAction SilentlyContinue
if ($packageJson -and $packageJson -match '"test"') {
    pnpm test
} else {
    Write-Host "No frontend test command configured" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== AI-first design checks ===" -ForegroundColor Green
Set-Location $repoRoot
& (Join-Path $PSScriptRoot "check-ai-first-design-targets.ps1")

Write-Host ""
Write-Host "=== Tests complete ===" -ForegroundColor Green
