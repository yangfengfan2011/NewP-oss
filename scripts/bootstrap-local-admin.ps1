#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$hostProjectPath = Join-Path $repoRoot "apps\host-api\HostApi.csproj"
$hostProjectRoot = Split-Path -Parent $hostProjectPath
$developmentConfigurationPath = Join-Path $hostProjectRoot "appsettings.Development.json"
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    throw "IDENTITY_MAINTENANCE_DOTNET_MISSING: dotnet was not found on PATH."
}

function Resolve-DevelopmentSqlServerConnectionString {
    $environmentOverride = [Environment]::GetEnvironmentVariable(
        "NEWP_DEV_SQLSERVER_CONNECTION_STRING",
        "Process")
    if (-not [string]::IsNullOrWhiteSpace($environmentOverride)) {
        return $environmentOverride
    }

    if (-not [IO.File]::Exists($developmentConfigurationPath)) {
        throw "IDENTITY_MAINTENANCE_SQLSERVER_CONNECTION_MISSING: configure apps/host-api/appsettings.Development.json or NEWP_DEV_SQLSERVER_CONNECTION_STRING."
    }

    try {
        $developmentConfiguration = Get-Content -Raw -LiteralPath $developmentConfigurationPath |
            ConvertFrom-Json -ErrorAction Stop
        $configuredConnectionString = $developmentConfiguration.ConnectionStrings.SqlServer
    }
    catch {
        throw "IDENTITY_MAINTENANCE_DEVELOPMENT_CONFIGURATION_INVALID: apps/host-api/appsettings.Development.json is invalid."
    }

    if ([string]::IsNullOrWhiteSpace($configuredConnectionString)) {
        throw "IDENTITY_MAINTENANCE_SQLSERVER_CONNECTION_MISSING: configure apps/host-api/appsettings.Development.json or NEWP_DEV_SQLSERVER_CONNECTION_STRING."
    }

    return [string]$configuredConnectionString
}

$previousAspNetCoreEnvironment = [Environment]::GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Process")
$previousDotNetEnvironment = [Environment]::GetEnvironmentVariable("DOTNET_ENVIRONMENT", "Process")
$previousConnectionString = [Environment]::GetEnvironmentVariable("ConnectionStrings__SqlServer", "Process")
$exitCode = 1

try {
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:DOTNET_ENVIRONMENT = "Development"
    # 显式注入标准键，避免维护模式依赖 Host 对 content root 的隐式推断；连接值不得写入脚本输出。
    $env:ConnectionStrings__SqlServer = Resolve-DevelopmentSqlServerConnectionString

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
