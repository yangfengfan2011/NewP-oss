param(
    [string]$ModuleName,
    [string]$ProjectPath,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputRoot = "artifacts/plugin-packages",
    [string]$HostVersionMin,
    [string]$HostVersionMax,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    Resolve-Path (Join-Path $PSScriptRoot "..")
}

function Get-ProjectProperty {
    param(
        [Parameter(Mandatory = $true)][string]$ResolvedProjectPath,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    $value = dotnet msbuild $ResolvedProjectPath "-getProperty:$PropertyName"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to resolve property '$PropertyName' for '$ResolvedProjectPath'."
    }

    return ($value | Select-Object -Last 1).Trim()
}

function Resolve-ProjectPath {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string]$ModuleName,
        [string]$ProjectPath
    )

    if (-not [string]::IsNullOrWhiteSpace($ProjectPath)) {
        return (Resolve-Path $ProjectPath).Path
    }

    if ([string]::IsNullOrWhiteSpace($ModuleName)) {
        throw "Either -ModuleName or -ProjectPath is required."
    }

    $moduleDirectory = Join-Path $RepoRoot "server/modules/$ModuleName"
    $projectFile = Get-ChildItem -Path $moduleDirectory -Filter "*.csproj" | Select-Object -First 1
    if (-not $projectFile) {
        throw "No .csproj file found under '$moduleDirectory'."
    }

    return $projectFile.FullName
}

function Resolve-HostVersionDefaults {
    param([Parameter(Mandatory = $true)][string]$RepoRoot)

    $appSettingsPath = Join-Path $RepoRoot "apps/host-api/appsettings.json"
    $defaultMin = "1.0.0"
    $defaultMax = "2.0.0"

    if (-not (Test-Path -LiteralPath $appSettingsPath)) {
        return @{
            Min = $defaultMin
            Max = $defaultMax
        }
    }

    $appSettings = Get-Content -LiteralPath $appSettingsPath -Raw -Encoding utf8 | ConvertFrom-Json
    $configuredHostVersion = $appSettings.Platform.HostVersion
    if ([string]::IsNullOrWhiteSpace($configuredHostVersion)) {
        return @{
            Min = $defaultMin
            Max = $defaultMax
        }
    }

    $version = [Version]$configuredHostVersion
    return @{
        Min = $configuredHostVersion
        Max = "$($version.Major + 1).0.0"
    }
}

function Find-ModuleSourceFile {
    param([Parameter(Mandatory = $true)][string]$ProjectDirectory)

    $moduleFile = Get-ChildItem -Path $ProjectDirectory -Filter "*Module.cs" -Recurse |
        Where-Object { $_.FullName -notmatch "\\obj\\|\\bin\\" } |
        Select-Object -First 1

    if (-not $moduleFile) {
        throw "Unable to find a '*Module.cs' entry file under '$ProjectDirectory'."
    }

    return $moduleFile.FullName
}

function Read-ModuleMetadataFromSource {
    param([Parameter(Mandatory = $true)][string]$ModuleSourcePath)

    $lines = Get-Content -LiteralPath $ModuleSourcePath -Encoding utf8
    $content = $lines -join [Environment]::NewLine

    $moduleIdLine = $lines | Where-Object { $_ -match 'public\s+override\s+string\s+ModuleId' } | Select-Object -First 1
    $displayNameLine = $lines | Where-Object { $_ -match 'public\s+override\s+string\s+DisplayName' } | Select-Object -First 1
    $descriptionLine = $lines | Where-Object { $_ -match 'public\s+override\s+string\s+Description' } | Select-Object -First 1

    $moduleId = ([regex]::Match($moduleIdLine, '=>\s*"([^"]+)"')).Groups[1].Value
    $displayName = ([regex]::Match($displayNameLine, '=>\s*"([^"]+)"')).Groups[1].Value
    $descriptionMatch = if ($descriptionLine) { [regex]::Match($descriptionLine, '=>\s*"([^"]+)"') } else { $null }
    $description = if ($descriptionMatch.Success) { $descriptionMatch.Groups[1].Value } else { "" }

    $dependencies = @()
    $dependenciesMatch = [regex]::Match(
        $content,
        'public\s+override\s+IReadOnlyList<string>\s+Dependencies\s*=>\s*new\[\]\s*\{(?<items>[^}]*)\}',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if ($dependenciesMatch.Success) {
        $items = $dependenciesMatch.Groups["items"].Value
        $dependencies = [regex]::Matches($items, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
    }

    if ([string]::IsNullOrWhiteSpace($moduleId) -or [string]::IsNullOrWhiteSpace($displayName)) {
        throw "Failed to extract ModuleId or DisplayName from '$ModuleSourcePath'."
    }

    return @{
        ModuleId = $moduleId
        DisplayName = $displayName
        Description = $description
        Dependencies = @($dependencies)
    }
}

function Write-Utf8NoBomFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

$repoRoot = (Get-RepoRoot).Path
Set-Location $repoRoot

$resolvedProjectPath = Resolve-ProjectPath -RepoRoot $repoRoot -ModuleName $ModuleName -ProjectPath $ProjectPath
$projectDirectory = Split-Path -Parent $resolvedProjectPath
$targetFramework = Get-ProjectProperty -ResolvedProjectPath $resolvedProjectPath -PropertyName "TargetFramework"
$assemblyName = Get-ProjectProperty -ResolvedProjectPath $resolvedProjectPath -PropertyName "AssemblyName"
$version = Get-ProjectProperty -ResolvedProjectPath $resolvedProjectPath -PropertyName "Version"
if ([string]::IsNullOrWhiteSpace($version)) {
    $version = "1.0.0"
}

$hostVersionDefaults = Resolve-HostVersionDefaults -RepoRoot $repoRoot
if ([string]::IsNullOrWhiteSpace($HostVersionMin)) {
    $HostVersionMin = $hostVersionDefaults.Min
}
if ([string]::IsNullOrWhiteSpace($HostVersionMax)) {
    $HostVersionMax = $hostVersionDefaults.Max
}

Write-Host "=== Packing plugin ===" -ForegroundColor Green
Write-Host "Project: $resolvedProjectPath" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "OutputRoot: $(Join-Path $repoRoot $OutputRoot)" -ForegroundColor Cyan

dotnet build $resolvedProjectPath -c $Configuration --nologo

$outputDirectory = Join-Path $projectDirectory "bin/$Configuration/$targetFramework"
$assemblyPath = Join-Path $outputDirectory "$assemblyName.dll"
if (-not (Test-Path -LiteralPath $assemblyPath)) {
    throw "Built assembly not found: $assemblyPath"
}

$moduleSourcePath = Find-ModuleSourceFile -ProjectDirectory $projectDirectory
$moduleMetadata = Read-ModuleMetadataFromSource -ModuleSourcePath $moduleSourcePath
$resolvedOutputRoot = Join-Path $repoRoot $OutputRoot
$packageDirectory = Join-Path $resolvedOutputRoot $moduleMetadata.ModuleId

if (Test-Path -LiteralPath $packageDirectory) {
    if (-not $Force) {
        throw "Package directory already exists: $packageDirectory. Re-run with -Force to overwrite."
    }

    Remove-Item -LiteralPath $packageDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

$manifest = @{
    moduleId = $moduleMetadata.ModuleId
    displayName = $moduleMetadata.DisplayName
    version = $version
    description = $moduleMetadata.Description
    entryAssembly = "$assemblyName.dll"
    dependencies = $moduleMetadata.Dependencies
    hostVersion = @{
        min = $HostVersionMin
        max = $HostVersionMax
    }
}

$manifestJson = $manifest | ConvertTo-Json -Depth 6
$manifestPath = Join-Path $packageDirectory "plugin.json"
Write-Utf8NoBomFile -Path $manifestPath -Content ($manifestJson + [Environment]::NewLine)

Copy-Item -LiteralPath $assemblyPath -Destination (Join-Path $packageDirectory "$assemblyName.dll") -Force

$pdbPath = Join-Path $outputDirectory "$assemblyName.pdb"
if (Test-Path -LiteralPath $pdbPath) {
    Copy-Item -LiteralPath $pdbPath -Destination (Join-Path $packageDirectory "$assemblyName.pdb") -Force
}

$depsPath = Join-Path $outputDirectory "$assemblyName.deps.json"
if (Test-Path -LiteralPath $depsPath) {
    Copy-Item -LiteralPath $depsPath -Destination (Join-Path $packageDirectory "$assemblyName.deps.json") -Force
}

$readmeSource = Join-Path $projectDirectory "README.md"
$readmeTarget = Join-Path $packageDirectory "README.md"
if (Test-Path -LiteralPath $readmeSource) {
    Copy-Item -LiteralPath $readmeSource -Destination $readmeTarget -Force
} else {
    $generatedReadme = @"
# $($moduleMetadata.ModuleId)

- DisplayName: $($moduleMetadata.DisplayName)
- Version: $version
- EntryAssembly: $assemblyName.dll
- HostVersion: $HostVersionMin - $HostVersionMax
"@
    Write-Utf8NoBomFile -Path $readmeTarget -Content ($generatedReadme + [Environment]::NewLine)
}

Write-Host ""
Write-Host "Package created: $packageDirectory" -ForegroundColor Green
Write-Host "Manifest: $manifestPath" -ForegroundColor Green
