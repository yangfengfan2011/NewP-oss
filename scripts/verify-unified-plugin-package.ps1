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

function Assert-StringProperty {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$PropertyPath
    )

    $value = $Object
    foreach ($segment in $PropertyPath.Split(".")) {
        Assert-Condition ($null -ne $value) "Missing required property '$PropertyPath'."
        $value = $value.$segment
    }

    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$value)) "Property '$PropertyPath' cannot be empty."
}

function Assert-ArrayProperty {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$PropertyPath
    )

    $value = $Object
    foreach ($segment in $PropertyPath.Split(".")) {
        Assert-Condition ($null -ne $value) "Missing required property '$PropertyPath'."
        $value = $value.$segment
    }

    Assert-Condition ($value -is [System.Array]) "Property '$PropertyPath' must be an array."
}

function Read-JsonFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )

    return Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json
}

function Get-ChecksumManifestEntries {
    param(
        [Parameter(Mandatory = $true)]$ChecksumManifest
    )

    Assert-Condition ($null -ne $ChecksumManifest.files) "Checksum manifest is missing required property 'files'."

    return $ChecksumManifest.files
}

function Compute-Sha256 {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath
    )

    return (Get-FileHash -LiteralPath $FilePath -Algorithm SHA256).Hash.ToLowerInvariant()
}

$resolvedPackagePath = (Resolve-Path -LiteralPath $PackagePath).Path

$rootManifestPath = Join-Path $resolvedPackagePath "plugin.json"
$backendManifestPath = Join-Path $resolvedPackagePath "backend/plugin.backend.json"
$frontendPluginManifestPath = Join-Path $resolvedPackagePath "frontend/plugin.frontend.json"
$frontendRuntimeManifestPath = Join-Path $resolvedPackagePath "frontend/manifest.json"
$frontendEntryPath = Join-Path $resolvedPackagePath "frontend/assets/entry.js"
$checksumManifestPath = Join-Path $resolvedPackagePath "checksums/sha256sums.json"

foreach ($requiredPath in @(
    $rootManifestPath,
    $backendManifestPath,
    $frontendPluginManifestPath,
    $frontendRuntimeManifestPath,
    $frontendEntryPath,
    $checksumManifestPath
)) {
    Assert-Condition (Test-Path -LiteralPath $requiredPath) "Missing required file: $requiredPath"
}

$rootManifest = Read-JsonFile -Path $rootManifestPath
$backendManifest = Read-JsonFile -Path $backendManifestPath
$frontendPluginManifest = Read-JsonFile -Path $frontendPluginManifestPath
$frontendRuntimeManifest = Read-JsonFile -Path $frontendRuntimeManifestPath
$checksumManifest = Read-JsonFile -Path $checksumManifestPath

Assert-StringProperty -Object $rootManifest -PropertyPath "pluginId"
Assert-StringProperty -Object $rootManifest -PropertyPath "moduleId"
Assert-StringProperty -Object $rootManifest -PropertyPath "displayName"
Assert-StringProperty -Object $rootManifest -PropertyPath "version"
Assert-StringProperty -Object $rootManifest -PropertyPath "description"
Assert-StringProperty -Object $rootManifest -PropertyPath "distribution.channel"
Assert-StringProperty -Object $rootManifest -PropertyPath "distribution.publisher"
Assert-StringProperty -Object $rootManifest -PropertyPath "hostVersion.min"
Assert-StringProperty -Object $rootManifest -PropertyPath "hostVersion.max"
Assert-StringProperty -Object $rootManifest -PropertyPath "artifacts.backend"
Assert-StringProperty -Object $rootManifest -PropertyPath "artifacts.frontend"
Assert-StringProperty -Object $rootManifest -PropertyPath "checksums.sha256Manifest"

Assert-StringProperty -Object $backendManifest -PropertyPath "moduleId"
Assert-StringProperty -Object $backendManifest -PropertyPath "displayName"
Assert-StringProperty -Object $backendManifest -PropertyPath "version"
Assert-StringProperty -Object $backendManifest -PropertyPath "description"
Assert-StringProperty -Object $backendManifest -PropertyPath "entryAssembly"
Assert-ArrayProperty -Object $backendManifest -PropertyPath "dependencies"
Assert-StringProperty -Object $backendManifest -PropertyPath "hostVersion.min"
Assert-StringProperty -Object $backendManifest -PropertyPath "hostVersion.max"
Assert-StringProperty -Object $backendManifest -PropertyPath "hostApiContractsVersion"
Assert-StringProperty -Object $backendManifest -PropertyPath "install.targetKind"

Assert-StringProperty -Object $frontendPluginManifest -PropertyPath "moduleId"
Assert-StringProperty -Object $frontendPluginManifest -PropertyPath "entryManifest"
Assert-StringProperty -Object $frontendPluginManifest -PropertyPath "assetRoot"
Assert-StringProperty -Object $frontendPluginManifest -PropertyPath "runtimeBaseStrategy"
Assert-StringProperty -Object $frontendPluginManifest -PropertyPath "publicBasePath"
Assert-ArrayProperty -Object $frontendPluginManifest -PropertyPath "sharedDependencies"
Assert-StringProperty -Object $frontendPluginManifest -PropertyPath "frontendContractsVersion"

Assert-StringProperty -Object $frontendRuntimeManifest -PropertyPath "moduleId"
Assert-StringProperty -Object $frontendRuntimeManifest -PropertyPath "displayName"
Assert-StringProperty -Object $frontendRuntimeManifest -PropertyPath "version"
Assert-StringProperty -Object $frontendRuntimeManifest -PropertyPath "description"
Assert-StringProperty -Object $frontendRuntimeManifest -PropertyPath "entry"

Assert-Condition ($rootManifest.artifacts.backend -eq "backend/plugin.backend.json") "Root manifest artifact backend path must be 'backend/plugin.backend.json'."
Assert-Condition ($rootManifest.artifacts.frontend -eq "frontend/plugin.frontend.json") "Root manifest artifact frontend path must be 'frontend/plugin.frontend.json'."
Assert-Condition ($rootManifest.checksums.sha256Manifest -eq "checksums/sha256sums.json") "Root manifest checksum manifest path must be 'checksums/sha256sums.json'."
Assert-Condition ($backendManifest.entryAssembly -eq (Split-Path -Leaf $backendManifest.entryAssembly)) "Backend manifest entryAssembly must be a file name, not a path."
Assert-Condition ($frontendRuntimeManifest.entry -eq "assets/entry.js") "Frontend runtime manifest entry must be 'assets/entry.js'."
Assert-Condition ($frontendPluginManifest.entryManifest -eq "manifest.json") "Frontend plugin manifest entryManifest must be 'manifest.json'."
Assert-Condition ($frontendPluginManifest.assetRoot -eq "assets") "Frontend plugin manifest assetRoot must be 'assets'."

Assert-Condition ($rootManifest.moduleId -eq $backendManifest.moduleId) "Root and backend moduleId values do not match."
Assert-Condition ($rootManifest.moduleId -eq $frontendRuntimeManifest.moduleId) "Root and frontend runtime moduleId values do not match."
Assert-Condition ($rootManifest.moduleId -eq $frontendPluginManifest.moduleId) "Root and frontend plugin moduleId values do not match."
Assert-Condition ($rootManifest.version -eq $backendManifest.version) "Root and backend version values do not match."
Assert-Condition ($rootManifest.version -eq $frontendRuntimeManifest.version) "Root and frontend runtime version values do not match."
Assert-Condition ($rootManifest.displayName -eq $backendManifest.displayName) "Root and backend displayName values do not match."
Assert-Condition ($rootManifest.displayName -eq $frontendRuntimeManifest.displayName) "Root and frontend runtime displayName values do not match."

$backendAssemblyPath = Join-Path (Split-Path -Parent $backendManifestPath) $backendManifest.entryAssembly
Assert-Condition (Test-Path -LiteralPath $backendAssemblyPath) "Backend entry assembly not found: $backendAssemblyPath"

$checksumEntries = Get-ChecksumManifestEntries -ChecksumManifest $checksumManifest
Assert-Condition (@($checksumEntries.PSObject.Properties).Count -gt 0) "Checksum manifest property 'files' must contain at least one entry."
$packageRootPath = [System.IO.Path]::GetFullPath($resolvedPackagePath).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$packageRootPrefix = $packageRootPath + [System.IO.Path]::DirectorySeparatorChar

foreach ($entry in $checksumEntries.PSObject.Properties) {
    $relativePath = [string]$entry.Name
    $expectedHash = ([string]$entry.Value).ToLowerInvariant()
    $candidatePath = Join-Path $resolvedPackagePath ($relativePath -replace '/', '\')
    $actualFilePath = [System.IO.Path]::GetFullPath($candidatePath)

    Assert-Condition ($actualFilePath.StartsWith($packageRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) "Checksum manifest references a path outside the package root: $relativePath"
    Assert-Condition (Test-Path -LiteralPath $actualFilePath) "Checksum manifest references a missing file: $relativePath"

    $fileItem = Get-Item -LiteralPath $actualFilePath -Force
    Assert-Condition (-not ([bool]($fileItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint))) "Checksum manifest references a reparse point: $relativePath"

    $pathSegments = ($relativePath -replace '/', '\').Split('\')
    $currentPath = $resolvedPackagePath
    foreach ($segment in $pathSegments) {
        if ([string]::IsNullOrWhiteSpace($segment)) { continue }
        $currentPath = Join-Path $currentPath $segment
        if (Test-Path -LiteralPath $currentPath) {
            $segmentItem = Get-Item -LiteralPath $currentPath -Force
            Assert-Condition (-not ([bool]($segmentItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint))) "Checksum manifest references a path that traverses a reparse point: $relativePath"
        }
    }

    $actualHash = Compute-Sha256 -FilePath $actualFilePath
    Assert-Condition ($actualHash -eq $expectedHash) "Checksum mismatch for '$relativePath'. Expected $expectedHash but found $actualHash."
}

Write-Host "=== Unified plugin package verified ===" -ForegroundColor Green
Write-Host "Package: $resolvedPackagePath" -ForegroundColor Green
Write-Host "Root manifest: $rootManifestPath" -ForegroundColor Cyan
Write-Host "Backend manifest: $backendManifestPath" -ForegroundColor Cyan
Write-Host "Frontend manifest: $frontendPluginManifestPath" -ForegroundColor Cyan
Write-Host "Checksum manifest: $checksumManifestPath" -ForegroundColor Cyan
