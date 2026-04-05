$ErrorActionPreference = "Stop"

Write-Host "=== 初始化 ICP 开发环境 ===" -ForegroundColor Green

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "错误: 未找到 .NET SDK，请先安装 .NET SDK。" -ForegroundColor Red
    exit 1
}

Write-Host ".NET SDK 已安装: $(dotnet --version)" -ForegroundColor Green

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    Write-Host "错误: 未找到 Node.js，请先安装 Node.js 18 或更高版本。" -ForegroundColor Red
    exit 1
}

Write-Host "Node.js 已安装: $(node --version)" -ForegroundColor Green

if (-not (Get-Command pnpm -ErrorAction SilentlyContinue)) {
    Write-Host "未找到 pnpm，开始安装..." -ForegroundColor Yellow
    npm install -g pnpm
}

Write-Host "pnpm 已安装: $(pnpm --version)" -ForegroundColor Green

Write-Host ""
Write-Host "=== 恢复后端依赖 ===" -ForegroundColor Green
dotnet restore

Write-Host ""
Write-Host "=== 安装前端依赖 ===" -ForegroundColor Green
Set-Location frontend
pnpm install
Set-Location ..

Write-Host ""
Write-Host "=== 初始化完成 ===" -ForegroundColor Green
Write-Host ""
Write-Host "后续步骤:"
Write-Host "  1. 一键启动前后端: .\\scripts\\dev.ps1"
Write-Host "  2. 仅启动后端: cd apps/host-api; dotnet run"
Write-Host "  3. 仅启动前端: cd frontend; pnpm dev"
Write-Host ""
