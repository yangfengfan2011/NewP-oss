[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$baselineRoot = Join-Path $PSScriptRoot "baseline\1.0.0"
$runId = [Guid]::NewGuid().ToString("N").Substring(0, 24)
$externalRoot = Join-Path ([IO.Path]::GetTempPath()) "newp-platform-api-$runId"
$evidenceRoot = Join-Path $repoRoot ".codex-temp\plugin-api\runs\$runId"
$feedRoot = Join-Path $externalRoot "feeds"
$nugetFeed = Join-Path $feedRoot "nuget"
$npmFeed = Join-Path $feedRoot "npm"
$consumerRoot = Join-Path $externalRoot "Partner.AssetCatalog"
$nugetCache = Join-Path $externalRoot "cache\nuget"
$pnpmStore = Join-Path $externalRoot "cache\pnpm-store"
$pnpmCache = Join-Path $externalRoot "cache\pnpm-cache"
$dotnetHome = Join-Path $externalRoot "dotnet-home"
$releaseManifest = Join-Path $feedRoot "platform-api-release.json"
$registryJob = $null
$completed = $false
$runStopwatch = [Diagnostics.Stopwatch]::StartNew()

function Invoke-Native {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $ArgumentList,
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [Parameter(Mandatory)] [string] $EvidencePath
    )

    Push-Location $WorkingDirectory
    try {
        $lines = @(& $FilePath @ArgumentList 2>&1)
        $exitCode = $LASTEXITCODE
        $parent = Split-Path -Parent $EvidencePath
        if (-not [string]::IsNullOrWhiteSpace($parent)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }
        $lines | Set-Content -LiteralPath $EvidencePath -Encoding utf8NoBOM
        $lines | ForEach-Object { Write-Host $_ }
        if ($exitCode -ne 0) {
            throw "Command failed with exit code ${exitCode}: $FilePath $($ArgumentList -join ' ')"
        }
    }
    finally {
        Pop-Location
    }
}

function Invoke-NativeExpectFailure {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $ArgumentList,
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [Parameter(Mandatory)] [string] $EvidencePath,
        [Parameter(Mandatory)] [string[]] $ExpectedDiagnostics
    )

    Push-Location $WorkingDirectory
    try {
        $lines = @(& $FilePath @ArgumentList 2>&1)
        $exitCode = $LASTEXITCODE
        $lines | Set-Content -LiteralPath $EvidencePath -Encoding utf8NoBOM
        if ($exitCode -eq 0) {
            throw "Command unexpectedly passed: $FilePath $($ArgumentList -join ' ')"
        }

        $text = $lines -join "`n"
        foreach ($diagnostic in $ExpectedDiagnostics) {
            if ($text -notmatch [regex]::Escape($diagnostic)) {
                throw "Expected diagnostic '$diagnostic' was not emitted by: $FilePath $($ArgumentList -join ' ')"
            }
        }
    }
    finally {
        Pop-Location
    }
}

function Get-RepositoryDiffFingerprint {
    Push-Location $repoRoot
    try {
        $fingerprint = git diff --no-ext-diff --binary HEAD -- . ":(exclude).codegraph/codegraph.db" | git hash-object --stdin
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($fingerprint)) {
            throw "Failed to fingerprint the NewP tracked diff."
        }
        return $fingerprint.Trim()
    }
    finally {
        Pop-Location
    }
}

function Get-FreeTcpPort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try {
        return ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Start-RunScopedNpmRegistry {
    param(
        [Parameter(Mandatory)] [string] $TarballPath,
        [Parameter(Mandatory)] [int] $Port
    )

    $sha1 = [Convert]::ToHexString([Security.Cryptography.SHA1]::HashData([IO.File]::ReadAllBytes($TarballPath))).ToLowerInvariant()
    $sha512 = [Convert]::ToBase64String([Security.Cryptography.SHA512]::HashData([IO.File]::ReadAllBytes($TarballPath)))
    $job = Start-Job -ArgumentList $TarballPath, $Port, $sha1, $sha512 -ScriptBlock {
        param($TarballPath, $Port, $Sha1, $Sha512)
        $ErrorActionPreference = "Stop"
        $prefix = "http://127.0.0.1:$Port/"
        $tarballUrl = "${prefix}@icp-platform/plugin-sdk/-/plugin-sdk-1.0.0.tgz"
        $metadata = @{
            name = "@icp-platform/plugin-sdk"
            "dist-tags" = @{ latest = "1.0.0" }
            versions = @{
                "1.0.0" = @{
                    name = "@icp-platform/plugin-sdk"
                    version = "1.0.0"
                    dist = @{
                        tarball = $tarballUrl
                        shasum = $Sha1
                        integrity = "sha512-$Sha512"
                    }
                }
            }
        } | ConvertTo-Json -Depth 10 -Compress
        $metadataBytes = [Text.Encoding]::UTF8.GetBytes($metadata)
        $tarballBytes = [IO.File]::ReadAllBytes($TarballPath)
        $listener = [Net.HttpListener]::new()
        $listener.Prefixes.Add($prefix)
        $listener.Start()
        try {
            while ($listener.IsListening) {
                $context = $listener.GetContext()
                $path = [Uri]::UnescapeDataString($context.Request.Url.AbsolutePath)
                if ($path -eq "/@icp-platform/plugin-sdk") {
                    $context.Response.StatusCode = 200
                    $context.Response.ContentType = "application/json"
                    $context.Response.ContentLength64 = $metadataBytes.Length
                    $context.Response.OutputStream.Write($metadataBytes)
                }
                elseif ($path -eq "/@icp-platform/plugin-sdk/-/plugin-sdk-1.0.0.tgz") {
                    $context.Response.StatusCode = 200
                    $context.Response.ContentType = "application/octet-stream"
                    $context.Response.ContentLength64 = $tarballBytes.Length
                    $context.Response.OutputStream.Write($tarballBytes)
                }
                elseif ($path -eq "/-/ping") {
                    $ping = [Text.Encoding]::UTF8.GetBytes('{"ok":true}')
                    $context.Response.StatusCode = 200
                    $context.Response.ContentType = "application/json"
                    $context.Response.ContentLength64 = $ping.Length
                    $context.Response.OutputStream.Write($ping)
                }
                else {
                    $context.Response.StatusCode = 404
                }
                $context.Response.OutputStream.Close()
            }
        }
        finally {
            $listener.Stop()
            $listener.Close()
        }
    }

    $uri = "http://127.0.0.1:$Port/-/ping"
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        try {
            $null = Invoke-RestMethod -Uri $uri -TimeoutSec 1
            return $job
        }
        catch {
            Start-Sleep -Milliseconds 100
        }
    }

    Stop-Job $job -ErrorAction SilentlyContinue
    Remove-Job $job -Force -ErrorAction SilentlyContinue
    throw "Run-scoped npm registry did not become ready."
}

function Assert-NoForbiddenConsumerReferences {
    param([Parameter(Mandatory)] [string] $Root)

    $auditFiles = Get-ChildItem -LiteralPath $Root -Recurse -File -Force | Where-Object {
        $relativePath = [IO.Path]::GetRelativePath($Root, $_.FullName)
        $isGenerated = $relativePath -match '(^|[\\/])(?:\.git|node_modules|bin|obj|\.artifacts)([\\/]|$)'
        -not $isGenerated -and (
            $_.Name -in @("package.json", "pnpm-lock.yaml", "packages.lock.json", "dotnet-tools.json") -or
            $_.Extension -eq ".csproj"
        )
    }
    $violations = foreach ($file in $auditFiles) {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        if ($content.Contains($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
            "$($file.FullName):NEWP_SOURCE_PATH_FORBIDDEN"
        }
        if ($file.Extension -eq ".csproj" -and $content -match '(?i)<ProjectReference\b') {
            "$($file.FullName):PROJECT_REFERENCE_FORBIDDEN"
        }
        if ($file.Name -eq "package.json" -and $content -match '(?i):\s*["''](?:workspace:|file:|link:)') {
            "$($file.FullName):LOCAL_PACKAGE_DEPENDENCY_FORBIDDEN"
        }
        if ($file.Name -eq "pnpm-lock.yaml" -and $content -match '(?im)^\s*(?:specifier|version|resolution):\s*["'']?(?:workspace:|file:|link:)') {
            "$($file.FullName):LOCAL_LOCK_DEPENDENCY_FORBIDDEN"
        }
    }
    if (@($violations).Count -gt 0) {
        throw "Forbidden consumer dependency detected: $($violations -join '; ')"
    }
}

function Assert-ResolvedGraphs {
    param([Parameter(Mandatory)] [string] $Root)

    $assetsPath = Join-Path $Root "src\Partner.AssetCatalog.Authoring\obj\project.assets.json"
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -Depth 100
    $packageFolders = @($assets.packageFolders.PSObject.Properties.Name)
    if ($packageFolders.Count -ne 1) {
        throw "Expected one isolated NuGet package folder, found $($packageFolders.Count)."
    }
    $resolvedFolder = [IO.Path]::GetFullPath($packageFolders[0]).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $expectedFolder = [IO.Path]::GetFullPath($nugetCache).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    if (-not [string]::Equals($resolvedFolder, $expectedFolder, [StringComparison]::OrdinalIgnoreCase)) {
        throw "NuGet resolved outside the isolated cache: $resolvedFolder"
    }

    $libraries = @($assets.libraries.PSObject.Properties)
    $nonPackageLibraries = @($libraries | Where-Object { $_.Value.type -ne "package" })
    if ($libraries.Count -ne 2 -or $nonPackageLibraries.Count -gt 0) {
        throw "External consumer resolved a non-package .NET dependency."
    }
    foreach ($expected in @("ICP.Platform.Plugin.Contracts/1.0.0", "ICP.Platform.Plugin.Sdk/1.0.0")) {
        if ($expected -notin $libraries.Name) {
            throw "Resolved graph is missing exact package '$expected'."
        }
    }

    $reparsePoints = @(Get-ChildItem -LiteralPath $Root -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue)
    $resolvedRootPrefix = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $externalLinks = foreach ($reparsePoint in $reparsePoints) {
        try {
            $target = $reparsePoint.ResolveLinkTarget($true)
            $resolvedTarget = [IO.Path]::GetFullPath($target.FullName)
            if (-not $resolvedTarget.StartsWith($resolvedRootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                "$($reparsePoint.FullName) -> $resolvedTarget"
            }
        }
        catch {
            "$($reparsePoint.FullName) -> UNRESOLVED"
        }
    }
    if (@($externalLinks).Count -gt 0) {
        throw "External consumer contains links outside its owned root: $($externalLinks -join '; ')"
    }
}

function Assert-PackageBoundary {
    param([Parameter(Mandatory)] [string] $PackagePath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $entries = @($archive.Entries | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Name) } | Select-Object -ExpandProperty FullName)
        $forbidden = @($entries | Where-Object {
            $_ -match '^(backend|frontend)/' -or
            $_ -match '(?i)ICP\.Platform\.Plugin\.(Contracts|Sdk|Tool)\.dll$' -or
            $_ -match '(?i)\.(js|jsx|mjs|cjs|tsx)$'
        })
        if ($forbidden.Count -gt 0) {
            throw "Package contains forbidden runtime artifacts: $($forbidden -join '; ')"
        }
        foreach ($required in @(
            "plugin.json",
            "authoring/newp.plugin.json",
            "attestation/platform-api.json",
            "projections/standard-data.json",
            "provenance/explain.json",
            "checksums/sha256sums.json",
            "metadata/views/AssetDetail.view.json",
            "migrations/sqlserver/Partner.AssetCatalog.Migrations.dll")) {
            if ($required -notin $entries) {
                throw "Package is missing required entry '$required'."
            }
        }
        return $entries
    }
    finally {
        $archive.Dispose()
    }
}

function Assert-PlatformApiPackageLayout {
    param(
        [Parameter(Mandatory)] [string] $ContractsPackage,
        [Parameter(Mandatory)] [string] $BackendSdkPackage,
        [Parameter(Mandatory)] [string] $ToolPackage
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $expectations = @(
        @{
            Path = $ContractsPackage
            Required = @(
                "lib/net10.0/ICP.Platform.Plugin.Contracts.dll",
                "contentFiles/any/any/platform-api-release.identity.json",
                "contentFiles/any/any/schemas/standard-plugin-blueprint.schema.json")
        },
        @{
            Path = $BackendSdkPackage
            Required = @(
                "lib/net10.0/ICP.Platform.Plugin.Sdk.dll",
                "buildTransitive/ICP.Platform.Plugin.Sdk.targets",
                "analyzers/dotnet/cs/ICP.Platform.Plugin.Sdk.Analyzers.dll",
                "contentFiles/any/any/platform-api-release.identity.json")
        },
        @{
            Path = $ToolPackage
            Required = @("contentFiles/any/any/platform-api-release.identity.json")
        }
    )

    foreach ($expectation in $expectations) {
        $archive = [IO.Compression.ZipFile]::OpenRead($expectation.Path)
        try {
            $entries = @($archive.Entries | Select-Object -ExpandProperty FullName)
            foreach ($required in $expectation.Required) {
                if ($required -notin $entries) {
                    throw "Platform API package '$($expectation.Path)' is missing '$required'."
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
}

$trackedFingerprintBefore = Get-RepositoryDiffFingerprint

try {
    if (-not (Test-Path $baselineRoot)) {
        throw "Platform API baseline is missing: $baselineRoot"
    }
    if ($externalRoot.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "External consumer root resolved inside NewP."
    }

    New-Item -ItemType Directory -Path $nugetFeed, $npmFeed, $consumerRoot, $evidenceRoot, $nugetCache, $pnpmStore, $pnpmCache, $dotnetHome -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $externalRoot ".newp-platform-api-run") -Value $runId -Encoding ascii
    Copy-Item -Path (Join-Path $baselineRoot "*") -Destination $consumerRoot -Recurse -Force

    $env:PNPM_HOME = Join-Path $externalRoot "pnpm-home"

    Invoke-Native "dotnet" @("pack", ".\server\plugin-contracts\Plugin.Contracts.csproj", "-c", "Release", "-o", $nugetFeed, "--nologo", "-p:ContinuousIntegrationBuild=true") $repoRoot (Join-Path $evidenceRoot "pack-contracts.log")
    Invoke-Native "dotnet" @("pack", ".\server\plugin-sdk\Plugin.Sdk.csproj", "-c", "Release", "-o", $nugetFeed, "--nologo", "-p:ContinuousIntegrationBuild=true") $repoRoot (Join-Path $evidenceRoot "pack-backend-sdk.log")
    Invoke-Native "dotnet" @("pack", ".\tools\PluginTool\PluginTool.csproj", "-c", "Release", "-o", $nugetFeed, "--nologo", "-p:ContinuousIntegrationBuild=true") $repoRoot (Join-Path $evidenceRoot "pack-tool.log")
    Invoke-Native "corepack" @("pnpm", "--dir", "frontend", "--filter", "@icp-platform/plugin-sdk", "build") $repoRoot (Join-Path $evidenceRoot "build-frontend-sdk.log")
    Invoke-Native "corepack" @("pnpm", "--dir", "frontend", "--filter", "@icp-platform/plugin-sdk", "pack", "--pack-destination", $npmFeed) $repoRoot (Join-Path $evidenceRoot "pack-frontend-sdk.log")

    $contractsPackage = (Get-ChildItem -LiteralPath $nugetFeed -Filter "ICP.Platform.Plugin.Contracts.1.0.0.nupkg" -File).FullName
    $backendSdkPackage = (Get-ChildItem -LiteralPath $nugetFeed -Filter "ICP.Platform.Plugin.Sdk.1.0.0.nupkg" -File).FullName
    $toolPackage = (Get-ChildItem -LiteralPath $nugetFeed -Filter "ICP.Platform.Plugin.Tool.1.0.0.nupkg" -File).FullName
    $frontendSdkPackage = (Get-ChildItem -LiteralPath $npmFeed -Filter "*.tgz" -File).FullName
    Assert-PlatformApiPackageLayout -ContractsPackage $contractsPackage -BackendSdkPackage $backendSdkPackage -ToolPackage $toolPackage
    Invoke-Native "dotnet" @(
        "run", "--project", ".\tools\PluginTool\PluginTool.csproj", "-c", "Release", "--no-build", "--",
        "platform-api", "release",
        "--contracts", $contractsPackage,
        "--backend-sdk", $backendSdkPackage,
        "--frontend-sdk", $frontendSdkPackage,
        "--tool", $toolPackage,
        "--source-commit", (git -C $repoRoot rev-parse HEAD),
        "--output", $releaseManifest) $repoRoot (Join-Path $evidenceRoot "release.log")

    $nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="newp-platform-api-$runId" value="$nugetFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="newp-platform-api-$runId"><package pattern="ICP.Platform.Plugin.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@
    $nugetConfigPath = Join-Path $consumerRoot "NuGet.config"
    Set-Content -LiteralPath $nugetConfigPath -Value $nugetConfig -Encoding utf8NoBOM

    Invoke-Native "git" @("init", "--quiet") $consumerRoot (Join-Path $evidenceRoot "consumer-git-init.log")
    Invoke-Native "git" @("config", "user.name", "NewP Acceptance") $consumerRoot (Join-Path $evidenceRoot "consumer-git-config-name.log")
    Invoke-Native "git" @("config", "user.email", "acceptance@newp.invalid") $consumerRoot (Join-Path $evidenceRoot "consumer-git-config-email.log")
    Invoke-Native "git" @("add", "migrations/sqlserver/migrations.json") $consumerRoot (Join-Path $evidenceRoot "consumer-git-seed-add.log")
    Invoke-Native "git" @("commit", "--quiet", "-m", "固定初始空迁移前缀") $consumerRoot (Join-Path $evidenceRoot "consumer-git-seed-commit.log")

    $authoringProject = Join-Path $consumerRoot "src\Partner.AssetCatalog.Authoring\Partner.AssetCatalog.Authoring.csproj"
    # 隔离缓存只属于外仓；发布本仓Tool时保留仓库assets的默认缓存路径，避免清理临时目录后污染后续构建。
    $env:NUGET_PACKAGES = $nugetCache
    $env:DOTNET_CLI_HOME = $dotnetHome
    $migrationProject = Join-Path $consumerRoot "src\Partner.AssetCatalog.Migrations\Partner.AssetCatalog.Migrations.csproj"
    Invoke-Native "dotnet" @("restore", $authoringProject, "--configfile", $nugetConfigPath, "--use-lock-file", "--force-evaluate") $consumerRoot (Join-Path $evidenceRoot "restore-authoring.log")
    Invoke-Native "dotnet" @("restore", $authoringProject, "--configfile", $nugetConfigPath, "--locked-mode") $consumerRoot (Join-Path $evidenceRoot "restore-authoring-locked.log")
    Invoke-Native "dotnet" @("build", $authoringProject, "-c", "Release", "--no-restore", "--nologo") $consumerRoot (Join-Path $evidenceRoot "build-authoring.log")
    Invoke-Native "dotnet" @("run", "--project", $authoringProject, "-c", "Release", "--no-build", "--", $consumerRoot) $consumerRoot (Join-Path $evidenceRoot "test-authoring.log")

    $attestationPath = Join-Path (Split-Path -Parent $authoringProject) "obj\Release\net10.0\newp\platform-api-attestation.json"
    $buildAttestation = Get-Content -LiteralPath $attestationPath -Raw | ConvertFrom-Json
    if ($buildAttestation.builtWith -ne "1.0.0" -or $buildAttestation.releaseDigest -ne "sha256:e6f17cb0ca9747b06c83d9803d7cc634cff32c3e1234306389bc106a6fc3e273") {
        throw "The SDK build target did not emit the expected lockstep attestation."
    }

    $authoringProjectText = Get-Content -LiteralPath $authoringProject -Raw
    try {
        $nonExactProjectText = $authoringProjectText.Replace('Version="[1.0.0]"', 'Version="1.0.0"')
        Set-Content -LiteralPath $authoringProject -Value $nonExactProjectText -Encoding utf8NoBOM -NoNewline
        Invoke-NativeExpectFailure "dotnet" @("build", $authoringProject, "-c", "Release", "--no-restore", "--nologo") $consumerRoot (Join-Path $evidenceRoot "negative-version-skew.log") @("PLATFORM_API_RELEASE_SKEW")
    }
    finally {
        Set-Content -LiteralPath $authoringProject -Value $authoringProjectText -Encoding utf8NoBOM -NoNewline
    }

    try {
        $projectReference = "  <ItemGroup>`n    <ProjectReference Include=`"..\Partner.AssetCatalog.Migrations\Partner.AssetCatalog.Migrations.csproj`" />`n  </ItemGroup>`n`n"
        Set-Content -LiteralPath $authoringProject -Value $authoringProjectText.Replace("</Project>", "$projectReference</Project>") -Encoding utf8NoBOM -NoNewline
        Invoke-NativeExpectFailure "dotnet" @("build", $authoringProject, "-c", "Release", "--no-restore", "--nologo") $consumerRoot (Join-Path $evidenceRoot "negative-project-reference-build.log") @("PROJECT_REFERENCE_FORBIDDEN")
    }
    finally {
        Set-Content -LiteralPath $authoringProject -Value $authoringProjectText -Encoding utf8NoBOM -NoNewline
    }

    $forbiddenControllerPath = Join-Path (Split-Path -Parent $authoringProject) "ForbiddenController.cs"
    try {
        Set-Content -LiteralPath $forbiddenControllerPath -Encoding utf8NoBOM -Value @'
namespace Microsoft.AspNetCore.Mvc
{
    public abstract class ControllerBase { }
}

namespace Partner.AssetCatalog
{
    public sealed class ForbiddenController : Microsoft.AspNetCore.Mvc.ControllerBase { }
}
'@
        Invoke-NativeExpectFailure "dotnet" @("build", $authoringProject, "-c", "Release", "--no-restore", "--nologo") $consumerRoot (Join-Path $evidenceRoot "negative-backend-analyzer.log") @("STANDARD_PROFILE_BACKEND_CODE_FORBIDDEN")
    }
    finally {
        Remove-Item -LiteralPath $forbiddenControllerPath -Force -ErrorAction SilentlyContinue
    }

    Invoke-Native "dotnet" @("restore", $migrationProject, "--configfile", $nugetConfigPath, "--use-lock-file") $consumerRoot (Join-Path $evidenceRoot "restore-migrations.log")
    Invoke-Native "dotnet" @("restore", $migrationProject, "--configfile", $nugetConfigPath, "--locked-mode") $consumerRoot (Join-Path $evidenceRoot "restore-migrations-locked.log")
    Invoke-Native "dotnet" @("build", $migrationProject, "-c", "Release", "--no-restore", "--nologo") $consumerRoot (Join-Path $evidenceRoot "build-migrations.log")
    Copy-Item -LiteralPath (Join-Path $consumerRoot "src\Partner.AssetCatalog.Migrations\bin\Release\net10.0\Partner.AssetCatalog.Migrations.dll") -Destination (Join-Path $consumerRoot "migrations\sqlserver\Partner.AssetCatalog.Migrations.dll") -Force

    Invoke-Native "dotnet" @("tool", "restore", "--configfile", $nugetConfigPath, "--tool-manifest", (Join-Path $consumerRoot ".config\dotnet-tools.json")) $consumerRoot (Join-Path $evidenceRoot "restore-tool.log")

    $port = Get-FreeTcpPort
    $registryJob = Start-RunScopedNpmRegistry -TarballPath $frontendSdkPackage -Port $port
    $registry = "http://127.0.0.1:$port"
    Invoke-Native "corepack" @("pnpm", "install", "--registry", $registry, "--store-dir", $pnpmStore, "--cache-dir", $pnpmCache) $consumerRoot (Join-Path $evidenceRoot "pnpm-install.log")
    Invoke-Native "corepack" @("pnpm", "install", "--frozen-lockfile", "--registry", $registry, "--store-dir", $pnpmStore, "--cache-dir", $pnpmCache) $consumerRoot (Join-Path $evidenceRoot "pnpm-install-frozen.log")
    Invoke-Native "corepack" @("pnpm", "run", "build") $consumerRoot (Join-Path $evidenceRoot "frontend-build.log")
    Invoke-Native "corepack" @("pnpm", "test") $consumerRoot (Join-Path $evidenceRoot "frontend-test.log")

    $consumerRelease = Join-Path $consumerRoot ".artifacts\platform-api-release.json"
    New-Item -ItemType Directory -Path (Split-Path -Parent $consumerRelease) -Force | Out-Null
    Copy-Item -LiteralPath $releaseManifest -Destination $consumerRelease
    $finalizeOptions = @("newp", "plugin", "finalize-migration", "--root", $consumerRoot, "--release-manifest", $consumerRelease,
        "--migration", "20261010000100_AssetInitial", "--source", "src/Partner.AssetCatalog.Migrations/Migrations/20261010000100_AssetInitial.cs", "--compatibility", "safe-expand")
    Invoke-Native "dotnet" $finalizeOptions $consumerRoot (Join-Path $evidenceRoot "plugin-finalize-first.log")
    $manifestPath = Join-Path $consumerRoot "migrations/sqlserver/migrations.json"
    $firstManifestSha = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    Invoke-Native "dotnet" $finalizeOptions $consumerRoot (Join-Path $evidenceRoot "plugin-finalize-replay.log")
    if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne $firstManifestSha) {
        throw "Repeated public finalize changed the canonical manifest."
    }
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $evidenceRoot "migrations.json")
    Invoke-Native "git" @("add", "migrations/sqlserver/migrations.json", "src/Partner.AssetCatalog.Migrations/Migrations/20261010000100_AssetInitial.cs") $consumerRoot (Join-Path $evidenceRoot "consumer-git-published-add.log")
    Invoke-Native "git" @("commit", "--quiet", "-m", "固定已发布迁移前缀") $consumerRoot (Join-Path $evidenceRoot "consumer-git-published-commit.log")
    $originalManifest = [IO.File]::ReadAllBytes($manifestPath)
    try {
        $changedPrefix = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -Depth 100
        $changedPrefix.migrations[0].compatibility = "contract"
        [IO.File]::WriteAllText($manifestPath, ($changedPrefix | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
        $invalidPrefixSha = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
        Invoke-NativeExpectFailure "dotnet" $finalizeOptions $consumerRoot (Join-Path $evidenceRoot "negative-migration-published-prefix.log") @("MIGRATION_MANIFEST_HISTORY_CHANGED")
        if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne $invalidPrefixSha) { throw "Rejected finalize modified the invalid manifest." }
    }
    finally { [IO.File]::WriteAllBytes($manifestPath, $originalManifest) }
    $missingTypeOptions = @("newp", "plugin", "finalize-migration", "--root", $consumerRoot, "--release-manifest", $consumerRelease,
        "--migration", "20261010000200_MissingAssetTail", "--source", "src/Partner.AssetCatalog.Migrations/Migrations/20261010000200_MissingAssetTail.cs", "--compatibility", "safe-expand")
    Invoke-NativeExpectFailure "dotnet" $missingTypeOptions $consumerRoot (Join-Path $evidenceRoot "negative-migration-type-set.log") @("PERSISTENCE_MIGRATION_MISSING")
    if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne $firstManifestSha) { throw "Rejected migration type set changed the canonical manifest." }
    $explainPath = Join-Path $consumerRoot ".artifacts\explain.json"
    $packagePath = Join-Path $consumerRoot ".artifacts\Partner.AssetCatalog@1.0.0.npp"
    Invoke-Native "dotnet" @("newp", "plugin", "validate", "--root", $consumerRoot, "--release-manifest", $consumerRelease) $consumerRoot (Join-Path $evidenceRoot "plugin-validate.log")
    Invoke-Native "dotnet" @("newp", "plugin", "explain", "--root", $consumerRoot, "--release-manifest", $consumerRelease, "--output", $explainPath) $consumerRoot (Join-Path $evidenceRoot "plugin-explain.log")
    Invoke-Native "dotnet" @("newp", "plugin", "pack", "--root", $consumerRoot, "--release-manifest", $consumerRelease, "--output", $packagePath) $consumerRoot (Join-Path $evidenceRoot "plugin-pack.log")
    Invoke-Native "dotnet" @("newp", "plugin", "verify", "--package", $packagePath, "--release-manifest", $consumerRelease) $consumerRoot (Join-Path $evidenceRoot "plugin-verify.log")
    $blueprintPath = Join-Path $consumerRoot "newp.plugin.json"
    $originalBlueprint = [IO.File]::ReadAllBytes($blueprintPath)
    try {
        foreach ($version in @("1.1.0", "1.2.0")) {
            $variant = [Text.Encoding]::UTF8.GetString($originalBlueprint) | ConvertFrom-Json -Depth 100
            $variant.version = $version
            if ($version -eq "1.1.0") { $variant.composition.members = @("Part") }
            else { $variant.composition.members = @() }
            [IO.File]::WriteAllText($blueprintPath, ($variant | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
            $variantPackage = Join-Path $consumerRoot ".artifacts/Partner.AssetCatalog@$version.npp"
            Invoke-Native "dotnet" @("newp", "plugin", "pack", "--root", $consumerRoot, "--release-manifest", $consumerRelease, "--output", $variantPackage) $consumerRoot (Join-Path $evidenceRoot "plugin-pack-$version.log")
            Invoke-Native "dotnet" @("newp", "plugin", "verify", "--package", $variantPackage, "--release-manifest", $consumerRelease) $consumerRoot (Join-Path $evidenceRoot "plugin-verify-$version.log")
            Copy-Item -LiteralPath $variantPackage -Destination (Join-Path $evidenceRoot "Partner.AssetCatalog@$version.npp")
        }
    }
    finally { [IO.File]::WriteAllBytes($blueprintPath, $originalBlueprint) }

    Assert-NoForbiddenConsumerReferences -Root $consumerRoot
    Assert-ResolvedGraphs -Root $consumerRoot
    $packageEntries = Assert-PackageBoundary -PackagePath $packagePath

    $badRoot = Join-Path $externalRoot "bad-consumer"
    New-Item -ItemType Directory -Path $badRoot | Out-Null
    Set-Content -LiteralPath (Join-Path $badRoot "Bad.csproj") -Value '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="..\NewP\server\sdk\Sdk.csproj" /></ItemGroup></Project>' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $badRoot "package.json") -Value '{"devDependencies":{"@icp-platform/plugin-sdk":"workspace:*","@icp-platform/core":"1.0.0"}}' -Encoding utf8NoBOM
    Copy-Item -LiteralPath (Join-Path $consumerRoot "newp.plugin.json") -Destination $badRoot
    $sourceLinkPath = Join-Path $badRoot "source-link"
    New-Item -ItemType Junction -Path $sourceLinkPath -Target $consumerRoot | Out-Null
    try {
        Push-Location $consumerRoot
        try {
            $badOutput = @(& dotnet newp plugin validate --root $badRoot --release-manifest $consumerRelease 2>&1)
            $badExitCode = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }
    }
    finally {
        Remove-Item -LiteralPath $sourceLinkPath -Force -ErrorAction SilentlyContinue
    }
    $badOutput | Set-Content -LiteralPath (Join-Path $evidenceRoot "negative-dependencies.log") -Encoding utf8NoBOM
    if ($badExitCode -eq 0 -or
        ($badOutput -join "`n") -notmatch "PROJECT_REFERENCE_FORBIDDEN" -or
        ($badOutput -join "`n") -notmatch "WORKSPACE_DEPENDENCY_FORBIDDEN" -or
        ($badOutput -join "`n") -notmatch "SOURCE_PATH_DEPENDENCY_FORBIDDEN" -or
        ($badOutput -join "`n") -notmatch "FORBIDDEN_PLATFORM_REFERENCE") {
        throw "Forbidden dependency negative fixture did not fail with stable diagnostics."
    }

    $tamperedRelease = Join-Path $consumerRoot ".artifacts\platform-api-release-tampered.json"
    $tampered = Get-Content -LiteralPath $consumerRelease -Raw | ConvertFrom-Json -Depth 100
    $tampered.releaseDigest = "sha256:$('f' * 64)"
    $tampered | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $tamperedRelease -Encoding utf8NoBOM
    Push-Location $consumerRoot
    try {
        $tamperedOutput = @(& dotnet newp plugin verify --package $packagePath --release-manifest $tamperedRelease 2>&1)
        $tamperedExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    $tamperedOutput | Set-Content -LiteralPath (Join-Path $evidenceRoot "negative-attestation.log") -Encoding utf8NoBOM
    if ($tamperedExitCode -eq 0 -or ($tamperedOutput -join "`n") -notmatch "PLATFORM_API_ATTESTATION_MISMATCH") {
        throw "Tampered release digest did not fail with PLATFORM_API_ATTESTATION_MISMATCH."
    }

    $trackedFingerprintAfter = Get-RepositoryDiffFingerprint
    if (-not [string]::Equals($trackedFingerprintBefore, $trackedFingerprintAfter, [StringComparison]::Ordinal)) {
        throw "External consumer run changed the NewP tracked diff."
    }

    Copy-Item -LiteralPath $releaseManifest -Destination (Join-Path $evidenceRoot "platform-api-release.json") -Force
    Copy-Item -LiteralPath $packagePath -Destination (Join-Path $evidenceRoot "Partner.AssetCatalog@1.0.0.npp") -Force
    Copy-Item -LiteralPath (Join-Path $consumerRoot "pnpm-lock.yaml") -Destination (Join-Path $evidenceRoot "pnpm-lock.yaml") -Force
    Copy-Item -LiteralPath (Join-Path $consumerRoot "src\Partner.AssetCatalog.Authoring\packages.lock.json") -Destination (Join-Path $evidenceRoot "packages.lock.json") -Force
    Copy-Item -LiteralPath $explainPath -Destination (Join-Path $evidenceRoot "explain.json") -Force

    $result = [ordered]@{
        schemaVersion = "1.0"
        runId = $runId
        verdict = "passed"
        releaseVersion = "1.0.0"
        releaseDigest = (Get-Content -LiteralPath $releaseManifest -Raw | ConvertFrom-Json).releaseDigest
        finalizedManifestSha256 = $firstManifestSha.ToLowerInvariant()
        authoringDurationSeconds = $runStopwatch.Elapsed.TotalSeconds
        externalConsumerFingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($consumerRoot))).ToLowerInvariant()
        trackedDiffFingerprintBefore = $trackedFingerprintBefore
        trackedDiffFingerprintAfter = $trackedFingerprintAfter
        packageEntries = $packageEntries
        checks = @(
            "nuget-locked-restore",
            "pnpm-frozen-lockfile",
            "dotnet-tool-exact-restore",
            "external-build-test",
            "validate-explain-pack-verify",
            "resolved-path-audit",
            "no-reparse-points",
            "package-runtime-boundary",
            "version-compatibility",
            "negative-dependency-diagnostics",
            "attestation-tamper-diagnostic",
            "tracked-tree-unchanged"
        )
    }
    $result | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $evidenceRoot "result.json") -Encoding utf8NoBOM
    $completed = $true
    Write-Host "Platform API release smoke passed. runId=$runId evidence=$evidenceRoot"
}
finally {
    if ($null -ne $registryJob) {
        Stop-Job $registryJob -ErrorAction SilentlyContinue
        Remove-Job $registryJob -Force -ErrorAction SilentlyContinue
    }
    Remove-Item Env:NUGET_PACKAGES -ErrorAction SilentlyContinue
    Remove-Item Env:DOTNET_CLI_HOME -ErrorAction SilentlyContinue
    Remove-Item Env:PNPM_HOME -ErrorAction SilentlyContinue

    if (Test-Path $externalRoot) {
        $resolvedExternalRoot = [IO.Path]::GetFullPath($externalRoot)
        $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        $marker = Join-Path $resolvedExternalRoot ".newp-platform-api-run"
        if (-not $resolvedExternalRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path $marker) -or
            (Get-Content -LiteralPath $marker -Raw).Trim() -ne $runId) {
            throw "Refusing to clean an unowned Platform API smoke directory: $resolvedExternalRoot"
        }
        Remove-Item -LiteralPath $resolvedExternalRoot -Recurse -Force
    }

    if (-not $completed) {
        Write-Host "Platform API release smoke failed. Partial evidence: $evidenceRoot"
    }
}
