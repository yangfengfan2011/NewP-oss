param(
    [Parameter(Mandatory = $true)]
    [string]$DesignName,
    [ValidateSet("", "platform", "modules", "tooling", "roadmap")]
    [string]$Category = "",
    [string]$DesignsRoot = ".\docs\superpowers\specs",
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

function Add-DatePrefix {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ($Value -match "^\d{4}-\d{2}-\d{2}-") {
        return $Value
    }

    return "{0}-{1}" -f (Get-Date -Format "yyyy-MM-dd"), $Value
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

$normalizedName = Add-DatePrefix -Value $normalizedName

if ($normalizedName.EndsWith("-design", [System.StringComparison]::OrdinalIgnoreCase)) {
    $fileName = "$normalizedName.md"
}
else {
    $fileName = "$normalizedName-design.md"
}

$targetDesignsRoot = $resolvedDesignsRoot
if (-not [string]::IsNullOrWhiteSpace($Category)) {
    $targetDesignsRoot = Join-Path $resolvedDesignsRoot $Category
    if (-not (Test-Path -LiteralPath $targetDesignsRoot)) {
        New-Item -ItemType Directory -Path $targetDesignsRoot | Out-Null
    }
}

$designPath = Join-Path $targetDesignsRoot $fileName
if (Test-Path -LiteralPath $designPath) {
    throw "Design file already exists: $designPath"
}

& (Join-Path $PSScriptRoot "validate-ai-first-design.ps1") -Path $resolvedTemplatePath

Copy-Item -LiteralPath $resolvedTemplatePath -Destination $designPath
Write-Host "Created design document: $designPath" -ForegroundColor Green

if (Test-Enabled -Value $RegisterAiFirst) {
    & (Join-Path $PSScriptRoot "register-ai-first-design-target.ps1") -DesignPath $designPath -TargetsFile $resolvedTargetsFile
}
