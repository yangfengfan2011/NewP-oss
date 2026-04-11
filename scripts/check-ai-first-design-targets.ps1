param(
    [string[]]$AdditionalPath = @(),
    [string]$IncludeChangedDesigns = "true",
    [string]$TargetsFile = ".\docs\architecture\ai-first-design-check-targets.txt"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

function Get-ChangedDesignPaths {
    $gitStatusOutput = git status --short --untracked-files=all -- docs/designs 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $gitStatusOutput) {
        return @()
    }

    $results = New-Object System.Collections.Generic.List[string]
    foreach ($line in $gitStatusOutput) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $pathPortion = $line.Substring(3).Trim()
        if ($pathPortion.Contains("->")) {
            $pathPortion = ($pathPortion.Split("->")[-1]).Trim()
        }

        if ($pathPortion -like "docs/designs/*-design.md") {
            $results.Add((Join-Path $repoRoot $pathPortion))
        }
    }

    return $results
}

function Get-ConfiguredTargetPaths {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath
    )

    $resolvedTargetsFile = Resolve-Path -LiteralPath $FilePath
    $baseDirectory = Split-Path -Parent $resolvedTargetsFile.Path
    $configuredPaths = New-Object System.Collections.Generic.List[string]

    foreach ($line in Get-Content -LiteralPath $resolvedTargetsFile.Path -Encoding UTF8) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) {
            continue
        }

        if ([System.IO.Path]::IsPathRooted($trimmed)) {
            $configuredPaths.Add($trimmed)
            continue
        }

        $configuredPaths.Add((Join-Path $baseDirectory $trimmed))
    }

    return $configuredPaths
}

function Test-IncludeChangedDesignsEnabled {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $normalized = $Value.Trim().ToLowerInvariant()
    return $normalized -notin @("0", "false", "no")
}

$targets = New-Object System.Collections.Generic.List[string]

$allCandidateTargets = New-Object System.Collections.Generic.List[string]
foreach ($configuredTarget in Get-ConfiguredTargetPaths -FilePath $TargetsFile) {
    $allCandidateTargets.Add($configuredTarget)
}

foreach ($path in $AdditionalPath) {
    $allCandidateTargets.Add($path)
}

if (Test-IncludeChangedDesignsEnabled -Value $IncludeChangedDesigns) {
    foreach ($changedPath in Get-ChangedDesignPaths) {
        $allCandidateTargets.Add($changedPath)
    }
}

foreach ($candidate in $allCandidateTargets) {
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        continue
    }

    $resolvedCandidate = (Resolve-Path -LiteralPath $candidate -ErrorAction SilentlyContinue)
    if ($null -eq $resolvedCandidate) {
        continue
    }

    $resolvedPath = $resolvedCandidate.Path
    if (-not $targets.Contains($resolvedPath)) {
        $targets.Add($resolvedPath)
    }
}

Write-Host "=== AI-first design target checks ===" -ForegroundColor Green
& (Join-Path $PSScriptRoot "validate-ai-first-design.ps1") -Path $targets
