#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$hostProjectPath = Join-Path $repoRoot "apps\host-api\HostApi.csproj"
$hostProjectRoot = Split-Path -Parent $hostProjectPath
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    throw "IDENTITY_MAINTENANCE_DOTNET_MISSING: dotnet was not found on PATH."
}

$previousAspNetCoreEnvironment = [Environment]::GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Process")
$previousDotNetEnvironment = [Environment]::GetEnvironmentVariable("DOTNET_ENVIRONMENT", "Process")
$previousConnectionString = [Environment]::GetEnvironmentVariable("ConnectionStrings__SqlServer", "Process")
$exitCode = 1

try {
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:DOTNET_ENVIRONMENT = "Development"

    if (-not [string]::IsNullOrWhiteSpace($env:NEWP_DEV_SQLSERVER_CONNECTION_STRING)) {
        # 与 dev.ps1 使用同一连接覆盖入口；连接值不得写入脚本输出。
        $env:ConnectionStrings__SqlServer = $env:NEWP_DEV_SQLSERVER_CONNECTION_STRING
    }

    # 维护命令会丢弃 Web 参数，因此必须从 Host 项目目录启动，确保加载 appsettings.*.json。
    Push-Location $hostProjectRoot
    try {
        & $dotnetCommand.Source run --project $hostProjectPath -- identity bootstrap-local-admin
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}
finally {
    [Environment]::SetEnvironmentVariable(
        "ASPNETCORE_ENVIRONMENT",
        $previousAspNetCoreEnvironment,
        "Process")
    [Environment]::SetEnvironmentVariable(
        "DOTNET_ENVIRONMENT",
        $previousDotNetEnvironment,
        "Process")
    [Environment]::SetEnvironmentVariable(
        "ConnectionStrings__SqlServer",
        $previousConnectionString,
        "Process")
}

exit $exitCode
