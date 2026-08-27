[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$ErrorActionPreference = 'Stop'

$repositoryPath = [System.IO.Path]::GetFullPath($RepositoryRoot)
$solutionPath = Join-Path $repositoryPath 'ICP-Platform.sln'
if (-not [System.IO.File]::Exists($solutionPath)) {
    throw "Repository root is invalid: $repositoryPath"
}

$revision = (& git -C $repositoryPath rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $revision -notmatch '^[0-9a-f]{40}$') {
    throw 'Unable to resolve the repository revision.'
}

$temporaryBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\')
$temporaryRoot = Join-Path $temporaryBase ("newp-migration-determinism-{0}" -f [Guid]::NewGuid().ToString('N'))
$copyRoot = Join-Path $temporaryRoot 'repository-copy'
$primaryRevisionArgument = "-p:SourceRevisionId=$revision"
$copyRevisionArgument = '-p:SourceRevisionId=0000000000000000000000000000000000000000'
$projects = @(
    @{
        Name = 'PlatformFoundation'
        Project = 'server\platform-persistence-efcore.migrations.sqlserver\PlatformPersistence.EfCore.SqlServerMigrations.csproj'
        Dll = 'server\platform-persistence-efcore.migrations.sqlserver\bin\Debug\net10.0\PlatformPersistence.EfCore.SqlServerMigrations.dll'
        Manifest = 'server\platform-persistence-efcore.migrations.sqlserver\PlatformFoundation.SqlServerMigrations.json'
    },
    @{
        Name = 'Organization'
        Project = 'server\modules\Organization\Migrations.SqlServer\Organization.SqlServerMigrations.csproj'
        Dll = 'server\modules\Organization\Migrations.SqlServer\bin\Debug\net10.0\Organization.SqlServerMigrations.dll'
        Manifest = 'modules\Organization\migrations\Organization.SqlServerMigrations.json'
    },
    @{
        Name = 'Identity'
        Project = 'server\modules\Identity\Migrations.SqlServer\Identity.SqlServerMigrations.csproj'
        Dll = 'server\modules\Identity\Migrations.SqlServer\bin\Debug\net10.0\Identity.SqlServerMigrations.dll'
        Manifest = 'modules\Identity\migrations\Identity.SqlServerMigrations.json'
    },
    @{
        Name = 'Authentication'
        Project = 'server\modules\Authentication\Migrations.SqlServer\Authentication.SqlServerMigrations.csproj'
        Dll = 'server\modules\Authentication\Migrations.SqlServer\bin\Debug\net10.0\Authentication.SqlServerMigrations.dll'
        Manifest = 'modules\Authentication\migrations\Authentication.SqlServerMigrations.json'
    },
    @{
        Name = 'Example'
        Project = 'server\modules\Example\Migrations.SqlServer\Example.SqlServerMigrations.csproj'
        Dll = 'server\modules\Example\Migrations.SqlServer\bin\Debug\net10.0\Example.SqlServerMigrations.dll'
        Manifest = 'modules\Example\migrations\Example.SqlServerMigrations.json'
    }
)

try {
    New-Item -ItemType Directory -Path $copyRoot | Out-Null
    $trackedFiles = @(& git -C $repositoryPath ls-files)
    if ($LASTEXITCODE -ne 0 -or $trackedFiles.Count -eq 0) {
        throw 'Unable to enumerate tracked repository files.'
    }

    foreach ($relativePath in $trackedFiles) {
        $sourcePath = Join-Path $repositoryPath $relativePath
        if (-not [System.IO.File]::Exists($sourcePath)) {
            continue
        }

        $targetPath = Join-Path $copyRoot $relativePath
        $targetDirectory = Split-Path -Parent $targetPath
        if (-not [System.IO.Directory]::Exists($targetDirectory)) {
            New-Item -ItemType Directory -Path $targetDirectory | Out-Null
        }

        Copy-Item -LiteralPath $sourcePath -Destination $targetPath
    }

    foreach ($build in @(
        @{ Root = $repositoryPath; RevisionArgument = $primaryRevisionArgument },
        @{ Root = $copyRoot; RevisionArgument = $copyRevisionArgument }
    )) {
        $root = $build.Root
        foreach ($bundle in $projects) {
            $projectPath = Join-Path $root $bundle.Project
            & dotnet restore $projectPath --nologo | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Restore failed for $($bundle.Name) under $root."
            }

            & dotnet build $projectPath --no-restore --nologo --no-incremental $build.RevisionArgument | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Build failed for $($bundle.Name) under $root."
            }
        }
    }

    foreach ($bundle in $projects) {
        $primaryHash = (Get-FileHash -Algorithm SHA256 (Join-Path $repositoryPath $bundle.Dll)).Hash.ToLowerInvariant()
        $copyHash = (Get-FileHash -Algorithm SHA256 (Join-Path $copyRoot $bundle.Dll)).Hash.ToLowerInvariant()
        if ($primaryHash -ne $copyHash) {
            throw "$($bundle.Name) bundle is path-dependent: $primaryHash != $copyHash"
        }

        $manifest = Get-Content -Raw (Join-Path $repositoryPath $bundle.Manifest) | ConvertFrom-Json
        if ($manifest.bundleChecksum -ne $primaryHash) {
            throw "$($bundle.Name) manifest checksum does not match the deterministic bundle: $primaryHash"
        }

        Write-Output "$($bundle.Name) $primaryHash"
    }
}
finally {
    $resolvedTemporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot)
    $expectedPrefix = "$temporaryBase\newp-migration-determinism-"
    if ([System.IO.Directory]::Exists($resolvedTemporaryRoot)) {
        if (-not $resolvedTemporaryRoot.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unexpected path: $resolvedTemporaryRoot"
        }

        $rootItem = Get-Item -LiteralPath $resolvedTemporaryRoot -Force
        $reparsePoints = @(Get-ChildItem -LiteralPath $resolvedTemporaryRoot -Recurse -Force -Attributes ReparsePoint)
        if (($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or $reparsePoints.Count -ne 0) {
            throw "Refusing to remove a verification tree containing reparse points: $resolvedTemporaryRoot"
        }

        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
