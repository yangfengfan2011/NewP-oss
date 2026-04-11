param(
    [Parameter(Mandatory = $true)]
    [string]$DesignName,
    [string]$DesignsRoot = ".\docs\designs",
    [string]$TemplatePath = ".\docs\designs\detailed-design-template.md",
    [string]$TargetsFile = ".\docs\architecture\ai-first-design-check-targets.txt",
    [string]$RegisterAiFirst = "true"
)

$ErrorActionPreference = "Stop"

function Test-Enabled {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $normalized = $Value.Trim().ToLowerInvariant()
    return $normalized -notin @("0", "false", "no")
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

$resolvedDesignsRoot = (Resolve-Path -LiteralPath $DesignsRoot).Path
$resolvedTemplatePath = (Resolve-Path -LiteralPath $TemplatePath).Path
$resolvedTargetsFile = (Resolve-Path -LiteralPath $TargetsFile).Path

$normalizedName = $DesignName.Trim()
if ([string]::IsNullOrWhiteSpace($normalizedName)) {
    throw "DesignName must not be empty."
}

if ($normalizedName.EndsWith("-design", [System.StringComparison]::OrdinalIgnoreCase)) {
    $fileName = "$normalizedName.md"
}
else {
    $fileName = "$normalizedName-design.md"
}

$designPath = Join-Path $resolvedDesignsRoot $fileName
if (Test-Path -LiteralPath $designPath) {
    throw "Design file already exists: $designPath"
}

& (Join-Path $PSScriptRoot "validate-ai-first-design.ps1") -Path $resolvedTemplatePath

Copy-Item -LiteralPath $resolvedTemplatePath -Destination $designPath
Write-Host "Created design document: $designPath" -ForegroundColor Green

if (Test-Enabled -Value $RegisterAiFirst) {
    & (Join-Path $PSScriptRoot "register-ai-first-design-target.ps1") -DesignPath $designPath -TargetsFile $resolvedTargetsFile
}
