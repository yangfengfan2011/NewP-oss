param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("install", "select", "desired", "enable", "disable", "uninstall", "prune", "rollback")]
    [string]$Action,
    [string]$PackagePath,
    [string]$ModuleId,
    [string]$Version,
    [ValidateSet("true", "false")][string]$Enabled,
    [string]$CandidatePath,
    [string]$InstallRoot = "artifacts/installed-plugins",
    [string]$HostPath = (Join-Path $PSScriptRoot "../apps/host-api/bin/Release/net10.0/HostApi.dll")
)

$ErrorActionPreference = "Stop"
# 发行 Host 的离线入口复用同一可信来源与开关配置；不编译、不迁移、不发布 runtime。
# Host 必须停止；成功只代表库存或 desired 已保存，重启仍须验证完整候选。
$resolvedHost = (Resolve-Path -LiteralPath $HostPath).Path
$resolvedInstall = [IO.Path]::GetFullPath($InstallRoot)
$commandArgs = @($resolvedHost, "plugin-deploy", $Action, "--Platform:PluginInstallRoot", $resolvedInstall)
switch ($Action) {
    "install" {
        if ([string]::IsNullOrWhiteSpace($PackagePath)) { throw "-PackagePath is required." }
        $commandArgs += @("--package", (Resolve-Path -LiteralPath $PackagePath).Path)
    }
    "desired" {
        if ([string]::IsNullOrWhiteSpace($CandidatePath)) { throw "-CandidatePath is required." }
        $commandArgs += @("--file", (Resolve-Path -LiteralPath $CandidatePath).Path)
    }
    default {
        if ([string]::IsNullOrWhiteSpace($ModuleId)) { throw "-ModuleId is required." }
        $commandArgs += @("--module-id", $ModuleId)
        if ($Action -in @("select", "enable", "prune")) {
            if ([string]::IsNullOrWhiteSpace($Version)) { throw "-Version is required." }
            $commandArgs += @("--version", $Version)
        }
        if ($PSBoundParameters.ContainsKey("Enabled")) { $commandArgs += @("--enabled", $Enabled) }
    }
}
Push-Location (Split-Path -Parent $resolvedHost)
try { & dotnet @commandArgs; $result = $LASTEXITCODE }
finally { Pop-Location }
exit $result
