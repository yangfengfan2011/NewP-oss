param(
    [Parameter(Mandatory = $true)]
    [string]$ModuleName,

    [string]$DisplayName,

    [switch]$SkipIntegration,

    [switch]$ExternalPlugin,

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

if ($ExternalPlugin) {
    throw 'RETIRED_ENTRY: -ExternalPlugin 已退役。外部插件只支持 standard-data.v1，请使用 docs/developer/external-plugin-development.md 与 PluginTool。'
}

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    throw '-OutputPath 仅属于已退役的外部代码插件脚手架。内建模块输出目录由 ModuleName 确定。'
}

function Set-Utf8Content {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Convert-ToKebabCase {
    param([Parameter(Mandatory = $true)][string]$Value)

    ([regex]::Replace($Value, '([a-z0-9])([A-Z])', '$1-$2')).ToLowerInvariant()
}

function Convert-ToCamelCase {
    param([Parameter(Mandatory = $true)][string]$Value)

    $parts = $Value.Split('-', [System.StringSplitOptions]::RemoveEmptyEntries)
    if ($parts.Count -eq 0) {
        throw 'Invalid module slug.'
    }

    $result = $parts[0]
    foreach ($part in ($parts | Select-Object -Skip 1)) {
        if ($part.Length -gt 0) {
            $result += $part.Substring(0, 1).ToUpperInvariant() + $part.Substring(1)
        }
    }

    $result
}

function Copy-TemplateTree {
    param(
        [Parameter(Mandatory = $true)][string]$RootPath,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    $sourceRoot = (Resolve-Path -LiteralPath $RootPath).Path
    $sourceItem = Get-Item -LiteralPath $sourceRoot -Force
    if (-not $sourceItem.PSIsContainer -or (($sourceItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
        throw "Unsafe template root: $sourceRoot"
    }

    if (Test-Path -LiteralPath $DestinationPath) {
        throw "Template destination already exists: $DestinationPath"
    }

    New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
    $excludedDirectories = @('bin', 'obj', 'dist', 'node_modules')
    $pendingDirectories = [System.Collections.Generic.Stack[string]]::new()
    $pendingDirectories.Push($sourceRoot)

    while ($pendingDirectories.Count -gt 0) {
        $currentSource = $pendingDirectories.Pop()
        $relativeDirectory = [IO.Path]::GetRelativePath($sourceRoot, $currentSource)
        $currentDestination = if ($relativeDirectory -eq '.') {
            $DestinationPath
        } else {
            Join-Path $DestinationPath $relativeDirectory
        }
        New-Item -ItemType Directory -Path $currentDestination -Force | Out-Null

        foreach ($item in Get-ChildItem -LiteralPath $currentSource -Force) {
            if ($item.PSIsContainer) {
                if ($excludedDirectories -contains $item.Name) {
                    continue
                }
                if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Template contains an unsupported reparse point: $($item.FullName)"
                }

                $pendingDirectories.Push($item.FullName)
                continue
            }

            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Template contains an unsupported reparse point: $($item.FullName)"
            }

            Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $currentDestination $item.Name)
        }
    }
}

function Rename-IfExists {
    param(
        [Parameter(Mandatory = $true)][string]$FromPath,
        [Parameter(Mandatory = $true)][string]$ToPath
    )

    if (Test-Path -LiteralPath $FromPath) {
        Move-Item -LiteralPath $FromPath -Destination $ToPath
    }
}

function Update-FileContent {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][scriptblock]$Transform
    )

    $content = Get-Content -LiteralPath $Path -Raw -Encoding utf8
    $updated = & $Transform $content
    Set-Utf8Content -Path $Path -Content $updated
}

function Get-RelativePathCompat {
    param(
        [Parameter(Mandatory = $true)][string]$FromPath,
        [Parameter(Mandatory = $true)][string]$ToPath
    )

    $fromUri = New-Object System.Uri((Resolve-Path $FromPath).Path.TrimEnd('\') + '\')
    $toUri = New-Object System.Uri((Resolve-Path $ToPath).Path)
    [System.Uri]::UnescapeDataString($fromUri.MakeRelativeUri($toUri).ToString()).Replace('/', '\')
}

if ($ModuleName -notmatch '^[A-Z][A-Za-z0-9]*$') {
    throw 'ModuleName must be PascalCase.'
}

if ([string]::IsNullOrWhiteSpace($DisplayName)) {
    $DisplayName = "$ModuleName Module"
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$frontendRoot = Join-Path $repoRoot 'frontend'
$backendTemplateRoot = Join-Path $repoRoot 'server\templates\backend-module-template'
$externalTemplateRoot = Join-Path $repoRoot 'server\templates\external-plugin-template'
$frontendTemplateRoot = Join-Path $frontendRoot 'templates\frontend-module-template'
$moduleSlug = Convert-ToKebabCase -Value $ModuleName
$frontendBindingsVariable = "$(Convert-ToCamelCase -Value $moduleSlug)Bindings"
$frontendPageName = "$($ModuleName)Page"
$backendBehaviorKey = "$moduleSlug.backend"
$frontendBehaviorKey = "$moduleSlug.frontend"
$frontendPageKey = "$moduleSlug.page"

if ($ExternalPlugin) {
    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        $backendTargetRoot = Join-Path $repoRoot "external-plugins\$ModuleName"
    } else {
        $backendTargetRoot = if ([System.IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $repoRoot $OutputPath }
    }
    $frontendTargetRoot = $null
    $capsuleTargetRoot = $null
} else {
    $backendTargetRoot = Join-Path $repoRoot "server\modules\$ModuleName"
    $frontendTargetRoot = Join-Path $frontendRoot "modules\$moduleSlug"
    $capsuleTargetRoot = $frontendTargetRoot
}

if (-not (Test-Path -LiteralPath $backendTemplateRoot) -and -not $ExternalPlugin) {
    throw "Backend template not found: $backendTemplateRoot"
}

if (-not (Test-Path -LiteralPath $externalTemplateRoot) -and $ExternalPlugin) {
    throw "External plugin template not found: $externalTemplateRoot"
}

if (-not (Test-Path -LiteralPath $frontendTemplateRoot) -and -not $ExternalPlugin) {
    throw "Frontend template not found: $frontendTemplateRoot"
}

if (Test-Path -LiteralPath $backendTargetRoot) {
    throw "Backend target already exists: $backendTargetRoot"
}

if (-not $ExternalPlugin -and (Test-Path -LiteralPath $frontendTargetRoot)) {
    throw "Frontend target already exists: $frontendTargetRoot"
}

if (-not $ExternalPlugin -and (Test-Path -LiteralPath $capsuleTargetRoot)) {
    throw "Capsule target already exists: $capsuleTargetRoot"
}

Write-Host '=== Generate module scaffold ===' -ForegroundColor Green
Write-Host "ModuleName: $ModuleName" -ForegroundColor Cyan
Write-Host "DisplayName: $DisplayName" -ForegroundColor Cyan
Write-Host "BackendTarget: $backendTargetRoot" -ForegroundColor Cyan
if (-not $ExternalPlugin) {
    Write-Host "FrontendTarget: frontend/modules/$moduleSlug" -ForegroundColor Cyan
    Write-Host "CapsuleTarget: frontend/modules/$moduleSlug" -ForegroundColor Cyan
}

if ($ExternalPlugin) {
    Copy-TemplateTree -RootPath $externalTemplateRoot -DestinationPath $backendTargetRoot
} else {
    Copy-TemplateTree -RootPath $backendTemplateRoot -DestinationPath $backendTargetRoot
    Copy-TemplateTree -RootPath $frontendTemplateRoot -DestinationPath $frontendTargetRoot

    $capsuleTemplateRoot = Join-Path $backendTargetRoot 'capsule'
    if (-not (Test-Path -LiteralPath $capsuleTemplateRoot)) {
        throw "Backend template capsule not found: $capsuleTemplateRoot"
    }

    # 目录合并仅移动仓库内的模板资产；冲突不得覆盖已有前端内容。
    $resolvedRepoRoot = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd('\', '/')
    foreach ($asset in Get-ChildItem -LiteralPath $capsuleTemplateRoot) {
        $destination = [System.IO.Path]::GetFullPath((Join-Path $capsuleTargetRoot $asset.Name))
        if (-not $asset.FullName.StartsWith($resolvedRepoRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
            -not $destination.StartsWith($resolvedRepoRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Capsule move must stay inside the repository.'
        }
        if (Test-Path -LiteralPath $destination) { throw "Capsule asset already exists: $destination" }
        Move-Item -LiteralPath $asset.FullName -Destination $destination
    }
    Remove-Item -LiteralPath $capsuleTemplateRoot
}

$backendProjectPath = Join-Path $backendTargetRoot "$ModuleName.csproj"
$backendModulePath = Join-Path $backendTargetRoot "$($ModuleName)Module.cs"
$backendControllerPath = if ($ExternalPlugin) {
    Join-Path $backendTargetRoot "Controllers\$($ModuleName)StatusController.cs"
} else {
    Join-Path $backendTargetRoot "Controllers\$($ModuleName)Controller.cs"
}
$backendDtoPath = if ($ExternalPlugin) {
    Join-Path $backendTargetRoot "DTOs\$($ModuleName)StatusResponse.cs"
} else {
    Join-Path $backendTargetRoot "DTOs\$($ModuleName)Response.cs"
}
$backendInterfacePath = if ($ExternalPlugin) {
    Join-Path $backendTargetRoot "Services\I$($ModuleName)StatusService.cs"
} else {
    Join-Path $backendTargetRoot "Services\I$($ModuleName)Service.cs"
}
$backendServicePath = if ($ExternalPlugin) {
    Join-Path $backendTargetRoot "Services\$($ModuleName)StatusService.cs"
} else {
    Join-Path $backendTargetRoot "Services\$($ModuleName)Service.cs"
}

if ($ExternalPlugin) {
    Rename-IfExists -FromPath (Join-Path $backendTargetRoot 'ExternalPlugin.csproj') -ToPath $backendProjectPath
    Rename-IfExists -FromPath (Join-Path $backendTargetRoot 'ExternalPluginModule.cs') -ToPath $backendModulePath
    Rename-IfExists -FromPath (Join-Path $backendTargetRoot 'Controllers\ExternalPluginStatusController.cs') -ToPath $backendControllerPath
    Rename-IfExists -FromPath (Join-Path $backendTargetRoot 'DTOs\ExternalPluginStatusResponse.cs') -ToPath $backendDtoPath
    Rename-IfExists -FromPath (Join-Path $backendTargetRoot 'Services\IExternalPluginStatusService.cs') -ToPath $backendInterfacePath
    Rename-IfExists -FromPath (Join-Path $backendTargetRoot 'Services\ExternalPluginStatusService.cs') -ToPath $backendServicePath
} else {
    Rename-IfExists -FromPath (Join-Path $backendTargetRoot 'TemplateModule.csproj') -ToPath $backendProjectPath
    Rename-IfExists -FromPath (Join-Path $backendTargetRoot 'TemplateModule.cs') -ToPath $backendModulePath
    Rename-IfExists -FromPath (Join-Path $backendTargetRoot 'Controllers\TemplateController.cs') -ToPath $backendControllerPath
    Rename-IfExists -FromPath (Join-Path $backendTargetRoot 'DTOs\TemplateResponse.cs') -ToPath $backendDtoPath
    Rename-IfExists -FromPath (Join-Path $backendTargetRoot 'Services\ITemplateService.cs') -ToPath $backendInterfacePath
    Rename-IfExists -FromPath (Join-Path $backendTargetRoot 'Services\TemplateService.cs') -ToPath $backendServicePath
}

Update-FileContent -Path $backendProjectPath -Transform {
    param($content)

    if ($ExternalPlugin) {
        $sdkProjectPath = Resolve-Path (Join-Path $repoRoot 'server\sdk\Sdk.csproj')
        $relativeSdkPath = Get-RelativePathCompat -FromPath $backendTargetRoot -ToPath $sdkProjectPath
        $content = $content.Replace(
            '<RootNamespace>ICP.Platform.ExternalPluginTemplate</RootNamespace>',
            "<RootNamespace>ICP.Platform.ExternalPlugins.$ModuleName</RootNamespace>"
        )
        $content = $content.Replace(
            '<AssemblyName>ExternalPlugin</AssemblyName>',
            "<AssemblyName>$($ModuleName)Plugin</AssemblyName>"
        )
        $content = $content.Replace('__SDK_PROJECT_PATH__', $relativeSdkPath)
    } else {
        $content = $content.Replace(
            '<RootNamespace>ICP.Platform.Templates.BackendModuleTemplate</RootNamespace>',
            "<RootNamespace>ICP.Platform.Modules.$ModuleName</RootNamespace>"
        )
        $content = $content.Replace(
            '<AssemblyName>TemplateModule</AssemblyName>',
            "<AssemblyName>$($ModuleName)Module</AssemblyName>"
        )
        $content = $content.Replace(
            'capsule\newp.plugin.json',
            "..\..\..\frontend\modules\$moduleSlug\newp.plugin.json"
        )
        $content = $content.Replace(
            'ICP.Platform.Templates.BackendModuleTemplate.newp.plugin.json',
            "ICP.Platform.Modules.$ModuleName.newp.plugin.json"
        )
    }

    $content
}

Update-FileContent -Path $backendModulePath -Transform {
    param($content)

    if ($ExternalPlugin) {
        $content = $content.Replace('ICP.Platform.ExternalPluginTemplate', "ICP.Platform.ExternalPlugins.$ModuleName")
        $content = $content.Replace('IExternalPluginStatusService', "I$($ModuleName)StatusService")
        $content = $content.Replace('ExternalPluginStatusService', "$($ModuleName)StatusService")
        $content = $content.Replace('class ExternalPluginModule', "class $($ModuleName)Module")
        $content = [regex]::Replace($content, 'ModuleId\s*=>\s*"[^"]+";', ('ModuleId => "' + $ModuleName + '";'))
        $content = [regex]::Replace($content, 'DisplayName\s*=>\s*"[^"]+";', ('DisplayName => "' + $DisplayName + '";'))
        $content = [regex]::Replace($content, 'Description\s*=>\s*"[^"]+";', ('Description => "' + $DisplayName + ' external plugin";'))
        $content = $content.Replace('ExternalPlugin.View', ($ModuleName + '.View'))
        $content = [regex]::Replace($content, 'DisplayName\s*=\s*"[^"]+"', ('DisplayName = "View ' + $DisplayName + '"'))
        $content = [regex]::Replace($content, 'Group\s*=\s*"[^"]+"', ('Group = "' + $ModuleName + '"'))
    } else {
        $content = $content.Replace('ICP.Platform.Templates.BackendModuleTemplate', "ICP.Platform.Modules.$ModuleName")
        $content = $content.Replace('ITemplateService', "I$($ModuleName)Service")
        $content = $content.Replace('TemplateService', "$($ModuleName)Service")
        $content = $content.Replace('class TemplateModule', "class $($ModuleName)Module")
        $content = $content.Replace('template-module.backend', $backendBehaviorKey)
    }

    $content
}

Update-FileContent -Path $backendControllerPath -Transform {
    param($content)

    if ($ExternalPlugin) {
        $content = $content.Replace('ICP.Platform.ExternalPluginTemplate', "ICP.Platform.ExternalPlugins.$ModuleName")
        $content = $content.Replace('ExternalPluginStatusResponse', "$($ModuleName)StatusResponse")
        $content = $content.Replace('IExternalPluginStatusService', "I$($ModuleName)StatusService")
        $content = $content.Replace('ExternalPluginStatusController', "$($ModuleName)StatusController")
        $content = $content.Replace('api/external-plugin/status', "api/$moduleSlug/status")
    } else {
        $content = $content.Replace('ICP.Platform.Templates.BackendModuleTemplate', "ICP.Platform.Modules.$ModuleName")
        $content = $content.Replace('TemplateResponse', "$($ModuleName)Response")
        $content = $content.Replace('ITemplateService', "I$($ModuleName)Service")
        $content = $content.Replace('TemplateController', "$($ModuleName)Controller")
        $content = $content.Replace('api/template-module', "api/$moduleSlug")
        $content = $content.Replace('template-module.read', ($moduleSlug + '.read'))
        $content = $content.Replace('[EndpointModule("TemplateModule")]', ('[EndpointModule("' + $ModuleName + '")]'))
    }

    $content
}

Update-FileContent -Path $backendDtoPath -Transform {
    param($content)

    if ($ExternalPlugin) {
        $content = $content.Replace('ICP.Platform.ExternalPluginTemplate', "ICP.Platform.ExternalPlugins.$ModuleName")
        $content = $content.Replace('ExternalPluginStatusResponse', "$($ModuleName)StatusResponse")
    } else {
        $content = $content.Replace('ICP.Platform.Templates.BackendModuleTemplate', "ICP.Platform.Modules.$ModuleName")
        $content = $content.Replace('TemplateResponse', "$($ModuleName)Response")
    }

    $content
}

Update-FileContent -Path $backendInterfacePath -Transform {
    param($content)

    if ($ExternalPlugin) {
        $content = $content.Replace('ICP.Platform.ExternalPluginTemplate', "ICP.Platform.ExternalPlugins.$ModuleName")
        $content = $content.Replace('IExternalPluginStatusService', "I$($ModuleName)StatusService")
        $content = $content.Replace('ExternalPluginStatusResponse', "$($ModuleName)StatusResponse")
    } else {
        $content = $content.Replace('ICP.Platform.Templates.BackendModuleTemplate', "ICP.Platform.Modules.$ModuleName")
        $content = $content.Replace('ITemplateService', "I$($ModuleName)Service")
    }

    $content
}

Update-FileContent -Path $backendServicePath -Transform {
    param($content)

    if ($ExternalPlugin) {
        $content = $content.Replace('ICP.Platform.ExternalPluginTemplate', "ICP.Platform.ExternalPlugins.$ModuleName")
        $content = $content.Replace('ExternalPluginStatusService', "$($ModuleName)StatusService")
        $content = $content.Replace('IExternalPluginStatusService', "I$($ModuleName)StatusService")
        $content = $content.Replace('ExternalPluginStatusResponse', "$($ModuleName)StatusResponse")
        $content = $content.Replace('ModuleId = "ExternalPlugin"', ('ModuleId = "' + $ModuleName + '"'))
        $content = $content.Replace('External plugin template is running.', "$DisplayName is running.")
    } else {
        $content = $content.Replace('ICP.Platform.Templates.BackendModuleTemplate', "ICP.Platform.Modules.$ModuleName")
        $content = $content.Replace('TemplateService', "$($ModuleName)Service")
        $content = $content.Replace('ITemplateService', "I$($ModuleName)Service")
        $content = $content.Replace('Template module is ready.', "$DisplayName is ready.")
    }

    $content
}

if ($ExternalPlugin) {
    $externalReadmePath = Join-Path $backendTargetRoot 'README.md'
    Update-FileContent -Path $externalReadmePath -Transform {
        param($content)

        $content = $content.Replace('ExternalPlugin', $ModuleName)
        $content = [regex]::Replace($content, '^#\s+.+$', ('# ' + $DisplayName), [System.Text.RegularExpressions.RegexOptions]::Multiline)
        $content = $content.Replace('外部插件模板', $DisplayName)
        $content = $content.Replace('.\ExternalPlugin.csproj', ".\$ModuleName.csproj")
        $content
    }
} else {
    $capsuleManifestPath = Join-Path $capsuleTargetRoot 'newp.plugin.json'
    $capsuleNavigationPath = Join-Path $capsuleTargetRoot "metadata\navigation\$ModuleName.nav.json"
    $capsuleDictionaryPath = Join-Path $capsuleTargetRoot 'metadata\dictionaries\Navigation.dic.json'
    Rename-IfExists -FromPath (Join-Path $capsuleTargetRoot 'metadata\navigation\TemplateModule.nav.json') -ToPath $capsuleNavigationPath

    Update-FileContent -Path $capsuleManifestPath -Transform {
        param($content)

        $content = $content.Replace('TemplateModule', $ModuleName)
        $content = $content.Replace("../../$ModuleName.dll", "../../$($ModuleName)Module.dll")
        $content = $content.Replace('template-module', $moduleSlug)
        $content = $content.Replace('模板模块', $DisplayName)
        $content = $content.Replace('后端模块模板骨架', "$DisplayName 模块")
        $content
    }

    Update-FileContent -Path $capsuleNavigationPath -Transform {
        param($content)

        $content = $content.Replace('TemplateModule', $ModuleName)
        $content = $content.Replace('template-module', $moduleSlug)
        $content
    }

    Update-FileContent -Path $capsuleDictionaryPath -Transform {
        param($content)

        $content = $content.Replace('TemplateModule', $ModuleName)
        $content = $content.Replace('Template Module', $DisplayName)
        $content = $content.Replace('模板模块', $DisplayName)
        $content
    }

    $frontendPackagePath = Join-Path $frontendTargetRoot 'package.json'
    $frontendIndexPath = Join-Path $frontendTargetRoot 'src\index.ts'
    $frontendPagePath = Join-Path $frontendTargetRoot "src\pages\$frontendPageName.tsx"
    $frontendPrimaryEntryPath = Join-Path $frontendTargetRoot "src\$frontendPageName.primary.ts"
    $frontendRecoveryEntryPath = Join-Path $frontendTargetRoot "src\$frontendPageName.recovery.ts"
    Rename-IfExists -FromPath (Join-Path $frontendTargetRoot 'src\pages\TemplatePage.tsx') -ToPath $frontendPagePath
    Rename-IfExists -FromPath (Join-Path $frontendTargetRoot 'src\TemplatePage.primary.ts') -ToPath $frontendPrimaryEntryPath
    Rename-IfExists -FromPath (Join-Path $frontendTargetRoot 'src\TemplatePage.recovery.ts') -ToPath $frontendRecoveryEntryPath

    Update-FileContent -Path $frontendPackagePath -Transform {
        param($content)

        $content.Replace('@icp-templates/frontend-module-template', "@icp-modules/$moduleSlug")
    }

    Update-FileContent -Path $frontendIndexPath -Transform {
        param($content)

        $content = $content.Replace('TemplatePage', $frontendPageName)
        $content = $content.Replace('templateModuleBindings', $frontendBindingsVariable)
        $content = $content.Replace('template-module.frontend', $frontendBehaviorKey)
        $content = $content.Replace('template-module.page', $frontendPageKey)
        $content
    }

    Update-FileContent -Path $frontendPagePath -Transform {
        param($content)

        $content = $content.Replace('TemplatePage', $frontendPageName)
        $content = $content.Replace('模板模块', $DisplayName)
        $content
    }

    foreach ($frontendCapabilityEntryPath in @($frontendPrimaryEntryPath, $frontendRecoveryEntryPath)) {
        Update-FileContent -Path $frontendCapabilityEntryPath -Transform {
            param($content)

            $content.Replace('TemplatePage', $frontendPageName)
        }
    }

    foreach ($readmePath in @(
        (Join-Path $backendTargetRoot 'README.md'),
        (Join-Path $frontendTargetRoot 'README.md')
    )) {
        Update-FileContent -Path $readmePath -Transform {
            param($content)

            $content = $content.Replace('TemplateModule', $ModuleName)
            $content = $content.Replace('template-module', $moduleSlug)
            $content = $content.Replace('模板模块', $DisplayName)
            $content
        }
    }
}

if ((-not $SkipIntegration) -and (-not $ExternalPlugin)) {
    $hostApiPath = Join-Path $repoRoot 'apps\host-api\HostApi.csproj'
    $projectReferenceInclude = "..\..\server\modules\$ModuleName\$ModuleName.csproj"
    $capsuleContentInclude = "..\..\frontend\modules\$moduleSlug\newp.plugin.json"
    $projectReferenceElement = '<ProjectReference Include="{0}" />' -f $projectReferenceInclude
    $capsuleContentElement = '<Content Include="{0}">' -f $capsuleContentInclude

    Update-FileContent -Path $hostApiPath -Transform {
        param($content)

        $newline = if ($content.Contains([Environment]::NewLine)) {
            [Environment]::NewLine
        } elseif ($content.Contains([char]10)) {
            [char]10
        } else {
            [Environment]::NewLine
        }
        $insertions = [System.Collections.Generic.List[string]]::new()

        if (-not $content.Contains($projectReferenceElement)) {
            $insertions.Add(
                '  <ItemGroup>' + $newline +
                '    ' + $projectReferenceElement + $newline +
                '  </ItemGroup>'
            )
        }

        if (-not $content.Contains($capsuleContentElement)) {
            $insertions.Add(
                '  <ItemGroup>' + $newline +
                '    ' + $capsuleContentElement + $newline +
                "      <Link>modules\$ModuleName\plugin.json</Link>" + $newline +
                '      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>' + $newline +
                '    </Content>' + $newline +
                '  </ItemGroup>'
            )
        }

        if ($insertions.Count -eq 0) {
            return $content
        }

        $closingProjectIndex = $content.LastIndexOf('</Project>', [StringComparison]::Ordinal)
        if ($closingProjectIndex -lt 0) {
            throw 'HostApi.csproj closing Project element not found.'
        }

        $prefix = if ($closingProjectIndex -gt 0 -and $content[$closingProjectIndex - 1] -eq [char]10) {
            ''
        } else {
            $newline
        }
        $insertionText = $prefix + ($insertions -join ($newline + $newline)) + $newline + $newline
        $content.Insert($closingProjectIndex, $insertionText)
    }
}

Write-Host ''
Write-Host '=== Scaffold generated ===' -ForegroundColor Green
Write-Host "Backend module: $backendTargetRoot" -ForegroundColor Green
if ($ExternalPlugin) {
    Write-Host 'Mode: external plugin template (backend only)' -ForegroundColor Cyan
    Write-Host 'No Host or frontend integration was performed.' -ForegroundColor Yellow
} else {
    Write-Host "Frontend module: frontend/modules/$moduleSlug" -ForegroundColor Green
    Write-Host "Authoring capsule: frontend/modules/$moduleSlug/newp.plugin.json" -ForegroundColor Green
    Write-Host 'Allowed integration target: apps/host-api/HostApi.csproj' -ForegroundColor Cyan
    Write-Host 'Do not modify other Host/Shell internals for normal module integration unless a separate design approves it.' -ForegroundColor Yellow
}

if ($SkipIntegration -and -not $ExternalPlugin) {
    Write-Host 'HostApi integration skipped; the canonical capsule and both behavior bindings were still generated.' -ForegroundColor Yellow
}
