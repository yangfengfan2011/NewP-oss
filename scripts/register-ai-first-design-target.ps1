param(
    [Parameter(Mandatory = $true)]
    [string]$DesignPath,
    [string]$TargetsFile = ".\docs\architecture\ai-first-design-check-targets.txt"
)

$ErrorActionPreference = "Stop"

function Get-RelativePathCompat {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FromPath,
        [Parameter(Mandatory = $true)]
        [string]$ToPath
    )

    $fromUri = New-Object System.Uri((Resolve-Path $FromPath).Path.TrimEnd('\') + '\')
    $toUri = New-Object System.Uri((Resolve-Path $ToPath).Path)
    return [System.Uri]::UnescapeDataString($fromUri.MakeRelativeUri($toUri).ToString()).Replace('\', '/')
}

$resolvedDesignPath = (Resolve-Path -LiteralPath $DesignPath).Path
$resolvedTargetsFile = (Resolve-Path -LiteralPath $TargetsFile).Path
$targetsDirectory = Split-Path -Parent $resolvedTargetsFile

$relativePath = Get-RelativePathCompat -FromPath $targetsDirectory -ToPath $resolvedDesignPath
$existingLines = Get-Content -LiteralPath $resolvedTargetsFile -Encoding UTF8

if ($existingLines -contains $relativePath) {
    Write-Host "AI-first design target already registered: $relativePath" -ForegroundColor Yellow
    exit 0
}

Add-Content -LiteralPath $resolvedTargetsFile -Value $relativePath -Encoding UTF8
Write-Host "AI-first design target registered: $relativePath" -ForegroundColor Green
