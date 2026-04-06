param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath
)

$ErrorActionPreference = "Stop"

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

$resolvedPackagePath = Resolve-Path $PackagePath
$manifestPath = Join-Path $resolvedPackagePath "plugin.json"
$readmePath = Join-Path $resolvedPackagePath "README.md"

Assert-Condition (Test-Path -LiteralPath $manifestPath) "Missing plugin.json in package '$resolvedPackagePath'."
Assert-Condition (Test-Path -LiteralPath $readmePath) "Missing README.md in package '$resolvedPackagePath'."

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json

foreach ($requiredProperty in @("moduleId", "displayName", "version", "entryAssembly", "dependencies", "hostVersion")) {
    Assert-Condition ($null -ne $manifest.$requiredProperty) "Manifest is missing required property '$requiredProperty'."
}

Assert-Condition (-not [string]::IsNullOrWhiteSpace($manifest.moduleId)) "Manifest 'moduleId' cannot be empty."
Assert-Condition (-not [string]::IsNullOrWhiteSpace($manifest.displayName)) "Manifest 'displayName' cannot be empty."
Assert-Condition (-not [string]::IsNullOrWhiteSpace($manifest.version)) "Manifest 'version' cannot be empty."
Assert-Condition (-not [string]::IsNullOrWhiteSpace($manifest.entryAssembly)) "Manifest 'entryAssembly' cannot be empty."
Assert-Condition ($null -ne $manifest.hostVersion.min) "Manifest 'hostVersion.min' is required."
Assert-Condition ($null -ne $manifest.hostVersion.max) "Manifest 'hostVersion.max' is required."

$entryAssemblyPath = Join-Path $resolvedPackagePath $manifest.entryAssembly
Assert-Condition (Test-Path -LiteralPath $entryAssemblyPath) "Entry assembly not found: $entryAssemblyPath"

Write-Host "=== Plugin package verified ===" -ForegroundColor Green
Write-Host "Package: $resolvedPackagePath" -ForegroundColor Green
Write-Host "ModuleId: $($manifest.moduleId)" -ForegroundColor Cyan
Write-Host "Version: $($manifest.version)" -ForegroundColor Cyan
Write-Host "EntryAssembly: $entryAssemblyPath" -ForegroundColor Cyan
