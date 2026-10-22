$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $repoRoot

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "命令失败（退出码 $LASTEXITCODE）：$FilePath $($Arguments -join ' ')"
    }
}

Write-Host "=== 构建 ICP 新平台 ===" -ForegroundColor Green

# 构建后端
Write-Host "=== 构建后端 ===" -ForegroundColor Green
Invoke-CheckedCommand -FilePath "dotnet" -Arguments @(
    "build", "ICP-Platform.sln", "--configuration", "Release")

# 构建前端
Write-Host ""
Write-Host "=== 构建前端 ===" -ForegroundColor Green
Invoke-CheckedCommand -FilePath $(if ($IsWindows) { "corepack.cmd" } else { "corepack" }) -Arguments @(
    "pnpm", "--dir", "frontend", "build")

Write-Host ""
Write-Host "=== 构建完成 ===" -ForegroundColor Green
