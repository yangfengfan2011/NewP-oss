param(
    [Parameter(Mandatory = $true)]
    [string]$ModuleName,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputRoot = "artifacts/plugin-packages",
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

function Resolve-BackendProjectPath {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$ModuleName
    )

    $moduleDirectory = Join-Path $RepoRoot "server/modules/$ModuleName"
    if (-not (Test-Path -LiteralPath $moduleDirectory)) {
        throw "Backend module directory not found: $moduleDirectory"
    }

    $projectFile = Get-ChildItem -Path $moduleDirectory -Filter "*.csproj" | Select-Object -First 1
    if (-not $projectFile) {
        throw "No .csproj file found under '$moduleDirectory'."
    }

    return $projectFile.FullName
}

function Resolve-FrontendModulePath {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$ModuleName
    )

    $frontendModulesRoot = Join-Path $RepoRoot "frontend/modules"
    if (-not (Test-Path -LiteralPath $frontendModulesRoot)) {
        throw "Frontend modules directory not found: $frontendModulesRoot"
    }

    $kebabCaseName = [regex]::Replace($ModuleName, '([a-z0-9])([A-Z])', '$1-$2').ToLowerInvariant()
    $candidateNames = @(
        $ModuleName,
        $ModuleName.ToLowerInvariant(),
        $ModuleName.ToUpperInvariant(),
        $kebabCaseName
    ) | Select-Object -Unique

    foreach ($candidateName in $candidateNames) {
        $candidatePath = Join-Path $frontendModulesRoot $candidateName
        $candidateEntryPath = Join-Path $candidatePath "src/index.ts"
        if ((Test-Path -LiteralPath $candidatePath) -and (Test-Path -LiteralPath $candidateEntryPath)) {
            return $candidatePath
        }
    }

    $matchingDir = Get-ChildItem -LiteralPath $frontendModulesRoot -Directory |
        Where-Object {
            $entryPath = Join-Path $_.FullName "src/index.ts"
            $_.Name.Equals($ModuleName, [System.StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $entryPath)
        } |
        Select-Object -First 1

    if ($matchingDir) {
        return $matchingDir.FullName
    }

    throw "Frontend module not found for '$ModuleName'. Expected a directory under '$frontendModulesRoot' containing 'src/index.ts'."
}

function Resolve-ModuleMetadataPath {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$ModuleName
    )

    $modulesRoot = Join-Path $RepoRoot "modules"
    if (-not (Test-Path -LiteralPath $modulesRoot)) {
        return $null
    }

    $matchingDirectory = Get-ChildItem -LiteralPath $modulesRoot -Directory |
        Where-Object { $_.Name.Equals($ModuleName, [System.StringComparison]::OrdinalIgnoreCase) } |
        Select-Object -First 1

    if (-not $matchingDirectory) {
        return $null
    }

    $metadataPath = Join-Path $matchingDirectory.FullName "metadata"
    if (-not (Test-Path -LiteralPath $metadataPath)) {
        return $null
    }

    return $metadataPath
}

function Format-ProcessArgument {
    param([Parameter(Mandatory = $true)][string]$Value)

    if ($Value -notmatch '[\s"]') {
        return $Value
    }

    return '"' + $Value.Replace('"', '\"') + '"'
}

function Invoke-GitCapture {
    param(
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = "git"
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Arguments = ($Arguments | ForEach-Object { Format-ProcessArgument -Value $_ }) -join " "

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo

    [void]$process.Start()
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    return @{
        ExitCode = $process.ExitCode
        StdOut = $stdout
        StdErr = $stderr
    }
}

function Read-FrontendModuleMetadata {
    param([Parameter(Mandatory = $true)][string]$FrontendModulePath)

    $entryPath = Join-Path $FrontendModulePath "src/index.ts"
    if (-not (Test-Path -LiteralPath $entryPath)) {
        throw "Frontend entry file not found: $entryPath"
    }

    $content = Get-Content -LiteralPath $entryPath -Raw -Encoding utf8
    $moduleIdMatch = [regex]::Match($content, '\bmoduleId\s*:\s*["''](?<value>[^"'']+)["'']')
    $displayNameMatch = [regex]::Match($content, '\bdisplayName\s*:\s*["''](?<value>[^"'']+)["'']')
    $versionMatch = [regex]::Match($content, '\bversion\s*:\s*["''](?<value>[^"'']+)["'']')
    $descriptionMatch = [regex]::Match($content, '\bdescription\s*:\s*["''](?<value>[^"'']+)["'']')

    if (-not $moduleIdMatch.Success -or -not $displayNameMatch.Success) {
        # Some local environments materialize tracked TS sources into a non-parseable binary form.
        # For metadata extraction only, fall back to Git-tracked text so packaging can still continue.
        $parentDirectory = Split-Path -Parent $entryPath
        if (-not [string]::IsNullOrWhiteSpace($parentDirectory)) {
            $repoRootResult = Invoke-GitCapture -WorkingDirectory $parentDirectory -Arguments @("rev-parse", "--show-toplevel")
            if ($repoRootResult.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($repoRootResult.StdOut)) {
                $resolvedRepoRoot = [System.IO.Path]::GetFullPath($repoRootResult.StdOut.Trim()).TrimEnd([char[]]"\/")
                $resolvedFilePath = [System.IO.Path]::GetFullPath($entryPath)
                $repoRootPrefix = $resolvedRepoRoot + "\"

                if ($resolvedFilePath.StartsWith($repoRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                    $gitRelativePath = $resolvedFilePath.Substring($repoRootPrefix.Length).Replace('\', '/')
                    $resolvedTrackedPath = $gitRelativePath
                    $showResult = Invoke-GitCapture -WorkingDirectory $resolvedRepoRoot -Arguments @("show", "HEAD:$resolvedTrackedPath")

                    if ($showResult.ExitCode -ne 0) {
                        $trackedFilesResult = Invoke-GitCapture -WorkingDirectory $resolvedRepoRoot -Arguments @("ls-files")
                        if ($trackedFilesResult.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($trackedFilesResult.StdOut)) {
                            $resolvedTrackedPath = ($trackedFilesResult.StdOut -split "`r?`n") |
                                Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_.Equals($gitRelativePath, [System.StringComparison]::OrdinalIgnoreCase) } |
                                Select-Object -First 1

                            if (-not [string]::IsNullOrWhiteSpace($resolvedTrackedPath)) {
                                $showResult = Invoke-GitCapture -WorkingDirectory $resolvedRepoRoot -Arguments @("show", "HEAD:$resolvedTrackedPath")
                            }
                        }
                    }

                    if ($showResult.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($showResult.StdOut)) {
                        $content = $showResult.StdOut
                        $moduleIdMatch = [regex]::Match($content, '\bmoduleId\s*:\s*["''](?<value>[^"'']+)["'']')
                        $displayNameMatch = [regex]::Match($content, '\bdisplayName\s*:\s*["''](?<value>[^"'']+)["'']')
                        $versionMatch = [regex]::Match($content, '\bversion\s*:\s*["''](?<value>[^"'']+)["'']')
                        $descriptionMatch = [regex]::Match($content, '\bdescription\s*:\s*["''](?<value>[^"'']+)["'']')
                    }
                }
            }
        }
    }

    if (-not $moduleIdMatch.Success -or -not $displayNameMatch.Success) {
        throw "Failed to extract frontend module metadata from '$entryPath'."
    }

    return @{
        ModuleId = $moduleIdMatch.Groups['value'].Value
        DisplayName = $displayNameMatch.Groups['value'].Value
        Version = if ($versionMatch.Success) { $versionMatch.Groups['value'].Value } else { "" }
        Description = if ($descriptionMatch.Success) { $descriptionMatch.Groups['value'].Value } else { "" }
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
    $versionLine = $lines | Where-Object { $_ -match 'public\s+override\s+string\s+Version' } | Select-Object -First 1
    $descriptionLine = $lines | Where-Object { $_ -match 'public\s+override\s+string\s+Description' } | Select-Object -First 1

    $moduleId = ([regex]::Match($moduleIdLine, '=>\s*"([^"]+)"')).Groups[1].Value
    $displayName = ([regex]::Match($displayNameLine, '=>\s*"([^"]+)"')).Groups[1].Value
    $version = if ($versionLine) { ([regex]::Match($versionLine, '=>\s*"([^"]+)"')).Groups[1].Value } else { "" }
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

    if ([string]::IsNullOrWhiteSpace($version)) {
        $version = "1.0.0"
    }

    return @{
        ModuleId = $moduleId
        DisplayName = $displayName
        Version = $version
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

function Compute-Sha256 {
    param([Parameter(Mandatory = $true)][string]$FilePath)

    $stream = [System.IO.File]::OpenRead($FilePath)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            $hashBytes = $sha256.ComputeHash($stream)
            return ([System.BitConverter]::ToString($hashBytes)).Replace("-", "").ToLowerInvariant()
        } finally {
            $sha256.Dispose()
        }
    } finally {
        $stream.Dispose()
    }
}

function Build-BackendModule {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$Configuration
    )

    Write-Host "Building backend module..." -ForegroundColor Cyan
    dotnet build $ProjectPath -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Backend build failed."
    }
}

function Build-FrontendModule {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$FrontendModulePath
    )

    $moduleName = Split-Path -Leaf $FrontendModulePath
    $buildScript = Join-Path $RepoRoot "frontend/scripts/build-frontend-plugin.mjs"

    if (-not (Test-Path -LiteralPath $buildScript)) {
        throw "Frontend build script not found: $buildScript"
    }

    Write-Host "Building frontend module..." -ForegroundColor Cyan
    Push-Location (Join-Path $RepoRoot "frontend")
    try {
        node $buildScript $moduleName
        if ($LASTEXITCODE -ne 0) {
            throw "Frontend build failed."
        }
    } finally {
        Pop-Location
    }
}

function Copy-BackendArtifacts {
    param(
        [Parameter(Mandatory = $true)][string]$OutputDirectory,
        [Parameter(Mandatory = $true)][string]$AssemblyName,
        [Parameter(Mandatory = $true)][string]$TargetDirectory
    )

    $backendDir = Join-Path $TargetDirectory "backend"
    New-Item -ItemType Directory -Path $backendDir -Force | Out-Null

    $assemblyPath = Join-Path $OutputDirectory "$AssemblyName.dll"
    if (-not (Test-Path -LiteralPath $assemblyPath)) {
        throw "Built assembly not found: $assemblyPath"
    }

    Copy-Item -LiteralPath $assemblyPath -Destination (Join-Path $backendDir "$AssemblyName.dll") -Force
}

function Copy-FrontendArtifacts {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$ModuleName,
        [Parameter(Mandatory = $true)][string]$TargetDirectory
    )

    $frontendArtifactsDir = Join-Path $RepoRoot "artifacts/frontend-plugins/$ModuleName"
    if (-not (Test-Path -LiteralPath $frontendArtifactsDir)) {
        throw "Frontend artifacts not found: $frontendArtifactsDir"
    }

    $entryJsPath = Join-Path $frontendArtifactsDir "entry.js"
    if (-not (Test-Path -LiteralPath $entryJsPath)) {
        throw "Frontend entry.js not found: $entryJsPath"
    }

    $frontendDir = Join-Path $TargetDirectory "frontend"
    $assetsDir = Join-Path $frontendDir "assets"
    New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null

    Copy-Item -LiteralPath $entryJsPath -Destination (Join-Path $assetsDir "entry.js") -Force
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDirectory,
        [Parameter(Mandatory = $true)][string]$DestinationDirectory
    )

    if (-not (Test-Path -LiteralPath $SourceDirectory)) {
        return
    }

    New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath $SourceDirectory -Force) {
        Copy-Item -LiteralPath $entry.FullName -Destination (Join-Path $DestinationDirectory $entry.Name) -Recurse -Force
    }
}

function Copy-ModuleMetadata {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$ModuleName,
        [Parameter(Mandatory = $true)][string]$TargetDirectory
    )

    $metadataPath = Resolve-ModuleMetadataPath -RepoRoot $RepoRoot -ModuleName $ModuleName
    if (-not $metadataPath) {
        return
    }

    $targetMetadataDirectory = Join-Path $TargetDirectory "modules/$ModuleName/metadata"
    Copy-DirectoryContents -SourceDirectory $metadataPath -DestinationDirectory $targetMetadataDirectory
}

function Copy-PlatformResources {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$TargetDirectory
    )

    $resourcesPath = Join-Path $RepoRoot "resources"
    if (-not (Test-Path -LiteralPath $resourcesPath)) {
        return
    }

    $targetResourcesDirectory = Join-Path $TargetDirectory "resources"
    Copy-DirectoryContents -SourceDirectory $resourcesPath -DestinationDirectory $targetResourcesDirectory
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

function Write-RootManifest {
    param(
        [Parameter(Mandatory = $true)][string]$TargetDirectory,
        [Parameter(Mandatory = $true)][hashtable]$Metadata,
        [Parameter(Mandatory = $true)][hashtable]$HostVersion
    )

    $pluginId = "icp.module." + $Metadata.ModuleId.ToLower()
    $manifest = [ordered]@{
        pluginId = $pluginId
        moduleId = $Metadata.ModuleId
        displayName = $Metadata.DisplayName
        version = $Metadata.Version
        description = $Metadata.Description
        distribution = [ordered]@{
            channel = "internal"
            publisher = "ICP Platform Team"
        }
        hostVersion = [ordered]@{
            min = $HostVersion.Min
            max = $HostVersion.Max
        }
        artifacts = [ordered]@{
            backend = "backend/plugin.backend.json"
            frontend = "frontend/plugin.frontend.json"
        }
        checksums = [ordered]@{
            sha256Manifest = "checksums/sha256sums.json"
        }
    }

    $manifestJson = $manifest | ConvertTo-Json -Depth 6
    $manifestPath = Join-Path $TargetDirectory "plugin.json"
    Write-Utf8NoBomFile -Path $manifestPath -Content ($manifestJson + [Environment]::NewLine)
}

function Write-BackendManifest {
    param(
        [Parameter(Mandatory = $true)][string]$TargetDirectory,
        [Parameter(Mandatory = $true)][string]$AssemblyName,
        [Parameter(Mandatory = $true)][hashtable]$Metadata,
        [Parameter(Mandatory = $true)][hashtable]$HostVersion
    )

    $backendDir = Join-Path $TargetDirectory "backend"
    $manifest = [ordered]@{
        moduleId = $Metadata.ModuleId
        displayName = $Metadata.DisplayName
        version = $Metadata.Version
        description = $Metadata.Description
        entryAssembly = "$AssemblyName.dll"
        dependencies = $Metadata.Dependencies
        hostVersion = [ordered]@{
            min = $HostVersion.Min
            max = $HostVersion.Max
        }
        hostApiContractsVersion = "1.0.0"
        install = [ordered]@{
            targetKind = "host-modules-directory"
        }
    }

    $manifestJson = $manifest | ConvertTo-Json -Depth 6
    $manifestPath = Join-Path $backendDir "plugin.backend.json"
    Write-Utf8NoBomFile -Path $manifestPath -Content ($manifestJson + [Environment]::NewLine)
}

function Write-FrontendRuntimeManifest {
    param(
        [Parameter(Mandatory = $true)][string]$TargetDirectory,
        [Parameter(Mandatory = $true)][hashtable]$Metadata
    )

    $frontendDir = Join-Path $TargetDirectory "frontend"
    $manifest = [ordered]@{
        moduleId = $Metadata.ModuleId
        displayName = $Metadata.DisplayName
        version = $Metadata.Version
        description = $Metadata.Description
        entry = "assets/entry.js"
    }

    $manifestJson = $manifest | ConvertTo-Json -Depth 6
    $manifestPath = Join-Path $frontendDir "manifest.json"
    Write-Utf8NoBomFile -Path $manifestPath -Content ($manifestJson + [Environment]::NewLine)
}

function Write-FrontendManifest {
    param(
        [Parameter(Mandatory = $true)][string]$TargetDirectory,
        [Parameter(Mandatory = $true)][hashtable]$Metadata
    )

    $frontendDir = Join-Path $TargetDirectory "frontend"
    $manifest = [ordered]@{
        moduleId = $Metadata.ModuleId
        entryManifest = "manifest.json"
        assetRoot = "assets"
        runtimeBaseStrategy = "host-local-static"
        publicBasePath = "/plugin-assets/$($Metadata.ModuleId)/$($Metadata.Version)/"
        sharedDependencies = @(
            "react",
            "react-dom",
            "@icp-platform/sdk",
            "@icp-platform/core"
        )
        frontendContractsVersion = "1.0.0"
    }

    $manifestJson = $manifest | ConvertTo-Json -Depth 6
    $manifestPath = Join-Path $frontendDir "plugin.frontend.json"
    Write-Utf8NoBomFile -Path $manifestPath -Content ($manifestJson + [Environment]::NewLine)
}

function Write-Checksums {
    param([Parameter(Mandatory = $true)][string]$TargetDirectory)

    $checksumsDir = Join-Path $TargetDirectory "checksums"
    New-Item -ItemType Directory -Path $checksumsDir -Force | Out-Null

    $normalizedTargetDirectory = [System.IO.Path]::GetFullPath($TargetDirectory).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $targetDirectoryPrefix = $normalizedTargetDirectory + [System.IO.Path]::DirectorySeparatorChar

    $checksums = @{}
    $files = Get-ChildItem -LiteralPath $TargetDirectory -Recurse -File |
        Where-Object { $_.FullName -notmatch "\\checksums\\" }

    foreach ($file in $files) {
        $normalizedFilePath = [System.IO.Path]::GetFullPath($file.FullName)
        if (-not $normalizedFilePath.StartsWith($targetDirectoryPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "File path is outside the target directory: $normalizedFilePath"
        }
        $relativePath = $normalizedFilePath.Substring($targetDirectoryPrefix.Length).Replace('\', '/')
        $hash = Compute-Sha256 -FilePath $normalizedFilePath
        $checksums[$relativePath] = $hash
    }

    $checksumManifest = @{
        files = $checksums
    }

    $checksumsJson = $checksumManifest | ConvertTo-Json -Depth 6
    $checksumsPath = Join-Path $checksumsDir "sha256sums.json"
    Write-Utf8NoBomFile -Path $checksumsPath -Content ($checksumsJson + [Environment]::NewLine)
}

$repoRoot = (Get-RepoRoot).Path
Set-Location $repoRoot

Write-Host "=== Packing unified plugin ===" -ForegroundColor Green
Write-Host "Module: $ModuleName" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan

$backendProjectPath = Resolve-BackendProjectPath -RepoRoot $repoRoot -ModuleName $ModuleName
$frontendModulePath = Resolve-FrontendModulePath -RepoRoot $repoRoot -ModuleName $ModuleName

Write-Host "Backend project: $backendProjectPath" -ForegroundColor Cyan
Write-Host "Frontend module: $frontendModulePath" -ForegroundColor Cyan

$projectDirectory = Split-Path -Parent $backendProjectPath
$moduleSourcePath = Find-ModuleSourceFile -ProjectDirectory $projectDirectory
$moduleMetadata = Read-ModuleMetadataFromSource -ModuleSourcePath $moduleSourcePath

$frontendMetadata = $null
$frontendMetadataReadError = $null

try {
    $frontendMetadata = Read-FrontendModuleMetadata -FrontendModulePath $frontendModulePath
} catch {
    $frontendMetadataReadError = $_
}

if ($null -eq $frontendMetadata) {
    if ($null -eq $frontendMetadataReadError -or -not $frontendMetadataReadError.Exception.Message.StartsWith("Failed to extract frontend module metadata from", [System.StringComparison]::Ordinal)) {
        throw $frontendMetadataReadError
    }

    # Packaging should remain deterministic even when the local frontend authoring file is not directly parseable.
    # In that case fall back to backend module metadata instead of blocking the package build.
    $frontendMetadata = @{
        ModuleId = $moduleMetadata.ModuleId
        DisplayName = $moduleMetadata.DisplayName
        Version = $moduleMetadata.Version
        Description = $moduleMetadata.Description
    }
}

if ($frontendMetadata.ModuleId -cne $moduleMetadata.ModuleId) {
    throw "Frontend module '$frontendModulePath' has moduleId '$($frontendMetadata.ModuleId)', but backend moduleId is '$($moduleMetadata.ModuleId)'."
}

$targetFramework = Get-ProjectProperty -ResolvedProjectPath $backendProjectPath -PropertyName "TargetFramework"
$assemblyName = Get-ProjectProperty -ResolvedProjectPath $backendProjectPath -PropertyName "AssemblyName"

$hostVersionDefaults = Resolve-HostVersionDefaults -RepoRoot $repoRoot

$isRooted = [System.IO.Path]::IsPathRooted($OutputRoot)
$isFullyQualified = $false
if ($isRooted) {
    if ($OutputRoot -match '^[A-Za-z]:(?![/\\])') {
        throw "Invalid -OutputRoot path format: $OutputRoot. Drive-relative paths (e.g., 'C:temp') are not supported."
    }
    try {
        $fullPath = [System.IO.Path]::GetFullPath($OutputRoot)
        $isFullyQualified = ($fullPath -eq $OutputRoot) -or ($fullPath.Replace('/', '\') -eq $OutputRoot.Replace('/', '\'))
    } catch {
        throw "Invalid -OutputRoot path format: $OutputRoot"
    }
}

$resolvedOutputRoot = if ($isFullyQualified) {
    [System.IO.Path]::GetFullPath($OutputRoot)
} else {
    $candidatePath = Join-Path $repoRoot $OutputRoot
    $normalizedPath = [System.IO.Path]::GetFullPath($candidatePath)
    $normalizedRepoRoot = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $repoRootPrefix = $normalizedRepoRoot + [System.IO.Path]::DirectorySeparatorChar

    if (-not $normalizedPath.StartsWith($repoRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Relative -OutputRoot must resolve to a path within the repository root. Resolved path: $normalizedPath"
    }

    $defaultOutputSubtree = Join-Path $repoRoot "artifacts/plugin-packages"
    $normalizedDefaultSubtree = [System.IO.Path]::GetFullPath($defaultOutputSubtree).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $defaultSubtreePrefix = $normalizedDefaultSubtree + [System.IO.Path]::DirectorySeparatorChar

    if (-not ($normalizedPath.StartsWith($defaultSubtreePrefix, [System.StringComparison]::OrdinalIgnoreCase) -or $normalizedPath.Equals($normalizedDefaultSubtree, [System.StringComparison]::OrdinalIgnoreCase))) {
        throw "Relative -OutputRoot must resolve within 'artifacts/plugin-packages'. Resolved path: $normalizedPath"
    }

    $normalizedPath
}

$packageDirectory = Join-Path $resolvedOutputRoot $moduleMetadata.ModuleId

if (Test-Path -LiteralPath $packageDirectory) {
    if (-not $Force) {
        throw "Package directory already exists: $packageDirectory. Re-run with -Force to overwrite."
    }
    Remove-Item -LiteralPath $packageDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

Build-BackendModule -ProjectPath $backendProjectPath -Configuration $Configuration

$frontendModuleName = Split-Path -Leaf $frontendModulePath
Build-FrontendModule -RepoRoot $repoRoot -FrontendModulePath $frontendModulePath

$outputDirectory = Join-Path $projectDirectory "bin/$Configuration/$targetFramework"
Copy-BackendArtifacts -OutputDirectory $outputDirectory -AssemblyName $assemblyName -TargetDirectory $packageDirectory
Copy-FrontendArtifacts -RepoRoot $repoRoot -ModuleName $frontendModuleName -TargetDirectory $packageDirectory
Copy-ModuleMetadata -RepoRoot $repoRoot -ModuleName $moduleMetadata.ModuleId -TargetDirectory $packageDirectory
Copy-PlatformResources -RepoRoot $repoRoot -TargetDirectory $packageDirectory

Write-RootManifest -TargetDirectory $packageDirectory -Metadata $moduleMetadata -HostVersion $hostVersionDefaults
Write-BackendManifest -TargetDirectory $packageDirectory -AssemblyName $assemblyName -Metadata $moduleMetadata -HostVersion $hostVersionDefaults

$frontendManifestMetadata = @{
    ModuleId = $frontendMetadata.ModuleId
    DisplayName = $frontendMetadata.DisplayName
    Version = if ([string]::IsNullOrWhiteSpace($frontendMetadata.Version)) { $moduleMetadata.Version } else { $frontendMetadata.Version }
    Description = $frontendMetadata.Description
}
Write-FrontendRuntimeManifest -TargetDirectory $packageDirectory -Metadata $frontendManifestMetadata
Write-FrontendManifest -TargetDirectory $packageDirectory -Metadata $frontendManifestMetadata

Write-Checksums -TargetDirectory $packageDirectory

Write-Host ""
Write-Host "Unified plugin package created: $packageDirectory" -ForegroundColor Green
Write-Host "Root manifest: $(Join-Path $packageDirectory 'plugin.json')" -ForegroundColor Green
Write-Host "Backend manifest: $(Join-Path $packageDirectory 'backend/plugin.backend.json')" -ForegroundColor Green
Write-Host "Frontend manifest: $(Join-Path $packageDirectory 'frontend/plugin.frontend.json')" -ForegroundColor Green
Write-Host "Checksums: $(Join-Path $packageDirectory 'checksums/sha256sums.json')" -ForegroundColor Green
