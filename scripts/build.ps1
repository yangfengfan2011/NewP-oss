$ErrorActionPreference = "Stop"

Write-Host "=== 构建 ICP 新平台 ===" -ForegroundColor Green

# 构建后端
Write-Host "=== 构建后端 ===" -ForegroundColor Green
dotnet build ICP-Platform.sln

# 构建前端
Write-Host ""
Write-Host "=== 构建前端 ===" -ForegroundColor Green
Set-Location frontend
pnpm build
Set-Location ..

Write-Host ""
Write-Host "=== 构建完成 ===" -ForegroundColor Green
