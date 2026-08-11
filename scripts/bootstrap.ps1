$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$frontendRoot = Join-Path $repoRoot "frontend"
$packageConfig = Get-Content -LiteralPath (Join-Path $frontendRoot "package.json") -Raw | ConvertFrom-Json
$packageManagerSpec = $packageConfig.packageManager
$runtimeSpec = $packageConfig.devEngines.runtime

if ($packageManagerSpec -notmatch '^pnpm@(?<Version>\d+\.\d+\.\d+)$') {
    Write-Host "错误: frontend/package.json 必须声明精确的 pnpm packageManager 版本。" -ForegroundColor Red
    exit 1
}

$requiredPnpmVersion = $Matches.Version

if ($runtimeSpec.name -ne "node" -or $runtimeSpec.version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Host "错误: frontend/package.json 必须声明精确的 Node.js devEngines.runtime 版本。" -ForegroundColor Red
    exit 1
}

$requiredNodeVersion = $runtimeSpec.version

Write-Host "=== 初始化 ICP 开发环境 ===" -ForegroundColor Green

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "错误: 未找到 .NET SDK，请先安装 .NET SDK。" -ForegroundColor Red
    exit 1
}

Write-Host ".NET SDK 已安装: $(dotnet --version)" -ForegroundColor Green

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    Write-Host "错误: 未找到 Node.js，需要精确版本 $requiredNodeVersion。" -ForegroundColor Red
    exit 1
}

$nodeVersion = (node --version).Trim().TrimStart("v")

if ($nodeVersion -ne $requiredNodeVersion) {
    Write-Host "错误: 当前 Node.js 版本为 $nodeVersion，需要精确版本 $requiredNodeVersion。" -ForegroundColor Red
    exit 1
}

Write-Host "Node.js 已安装: $nodeVersion" -ForegroundColor Green

if (-not (Get-Command corepack.cmd -ErrorAction SilentlyContinue)) {
    Write-Host "错误: 未找到 Corepack，无法按 packageManager 使用 pnpm $requiredPnpmVersion。" -ForegroundColor Red
    exit 1
}

$corepackPath = (Get-Command corepack.cmd).Source
Push-Location $frontendRoot
try {
    try {
        $pnpmVersionOutput = @(& $corepackPath pnpm --version)
        $pnpmVersionExitCode = $LASTEXITCODE
    }
    catch {
        $pnpmVersionOutput = @()
        $pnpmVersionExitCode = 1
    }
}
finally {
    Pop-Location
}

$currentPnpmVersion = if ($pnpmVersionOutput.Count -gt 0) { $pnpmVersionOutput[-1].ToString().Trim() } else { "" }
if ($pnpmVersionExitCode -ne 0 -or $currentPnpmVersion -ne $requiredPnpmVersion) {
    Write-Host "错误: Corepack 未能提供 packageManager 指定的 pnpm $requiredPnpmVersion。" -ForegroundColor Red
    exit 1
}

Write-Host "pnpm 已就绪（Corepack）: $currentPnpmVersion" -ForegroundColor Green

Write-Host ""
Write-Host "=== 恢复后端依赖 ===" -ForegroundColor Green
dotnet restore ICP-Platform.sln
if ($LASTEXITCODE -ne 0) {
    Write-Host "错误: 后端依赖恢复失败。" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "=== 安装前端依赖 ===" -ForegroundColor Green
Push-Location $frontendRoot
try {
    try {
        & $corepackPath pnpm install --frozen-lockfile
        $pnpmInstallExitCode = $LASTEXITCODE
    }
    catch {
        $pnpmInstallExitCode = 1
    }
}
finally {
    Pop-Location
}

if ($pnpmInstallExitCode -ne 0) {
    Write-Host "错误: 前端依赖安装失败。" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== 初始化完成 ===" -ForegroundColor Green
Write-Host ""
Write-Host "后续步骤:"
Write-Host "  1. 一键启动前后端: .\\scripts\\dev.ps1"
Write-Host "  2. 仅启动后端: cd apps/host-api; dotnet run"
Write-Host "  3. 仅启动前端: cd frontend; corepack pnpm --filter @icp-platform/shell dev"
Write-Host ""
