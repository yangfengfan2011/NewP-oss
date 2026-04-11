param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("install", "enable", "disable", "uninstall", "rollback")]
    [string]$Action,
    [string]$PackagePath,
    [string]$ModuleId,
    [string]$Version,
    [string]$InstallRoot = "artifacts/installed-plugins"
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    Resolve-Path (Join-Path $PSScriptRoot "..")
}

$repoRoot = (Get-RepoRoot).Path
$pluginOpsProject = Join-Path $repoRoot "tools/PluginOps/PluginOps.csproj"
if (-not (Test-Path -LiteralPath $pluginOpsProject)) {
    throw "PluginOps project not found: $pluginOpsProject"
}

Set-Location $repoRoot

$dotnetArgs = @("run", "--project", $pluginOpsProject, "--")

switch ($Action) {
    "install" {
        if ([string]::IsNullOrWhiteSpace($PackagePath)) {
            throw "-PackagePath is required when -Action install."
        }

        $dotnetArgs += @("install", "--package", $PackagePath, "--install-root", $InstallRoot)
    }
    "enable" {
        if ([string]::IsNullOrWhiteSpace($ModuleId)) {
            throw "-ModuleId is required when -Action enable."
        }

        if ([string]::IsNullOrWhiteSpace($Version)) {
            throw "-Version is required when -Action enable."
        }

        $dotnetArgs += @("enable", "--module-id", $ModuleId, "--version", $Version, "--install-root", $InstallRoot)
    }
    "disable" {
        if ([string]::IsNullOrWhiteSpace($ModuleId)) {
            throw "-ModuleId is required when -Action disable."
        }

        $dotnetArgs += @("disable", "--module-id", $ModuleId, "--install-root", $InstallRoot)
    }
    "uninstall" {
        if ([string]::IsNullOrWhiteSpace($ModuleId)) {
            throw "-ModuleId is required when -Action uninstall."
        }

        $dotnetArgs += @("uninstall", "--module-id", $ModuleId, "--install-root", $InstallRoot)
    }
    "rollback" {
        if ([string]::IsNullOrWhiteSpace($ModuleId)) {
            throw "-ModuleId is required when -Action rollback."
        }

        $dotnetArgs += @("rollback", "--module-id", $ModuleId, "--install-root", $InstallRoot)
    }
}

& dotnet @dotnetArgs
exit $LASTEXITCODE
