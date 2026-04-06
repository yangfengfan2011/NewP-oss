param(
    [Parameter(Mandatory = $true)]
    [string]$ModuleName,

    [string]$DisplayName,

    [switch]$SkipIntegration,

    [switch]$ExternalPlugin,

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

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

function Remove-GeneratedArtifacts {
    param([Parameter(Mandatory = $true)][string]$RootPath)

    foreach ($directoryName in @('bin', 'obj', 'dist', 'node_modules')) {
        Get-ChildItem -LiteralPath $RootPath -Directory -Recurse -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -eq $directoryName } |
            ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
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
$frontendModuleVariable = "$(Convert-ToCamelCase -Value $moduleSlug)Module"
$frontendPageName = "${ModuleName}Page"
$frontendServiceName = "${ModuleName}StatusService"
$frontendServiceFileBase = "$(Convert-ToCamelCase -Value $moduleSlug)Service"

if ($ExternalPlugin) {
    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        $backendTargetRoot = Join-Path $repoRoot "external-plugins\$ModuleName"
    } else {
        $backendTargetRoot = if ([System.IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $repoRoot $OutputPath }
    }
    $frontendTargetRoot = $null
} else {
    $backendTargetRoot = Join-Path $repoRoot "server\modules\$ModuleName"
    $frontendTargetRoot = Join-Path $frontendRoot "modules\$moduleSlug"
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

Write-Host '=== Generate module scaffold ===' -ForegroundColor Green
Write-Host "ModuleName: $ModuleName" -ForegroundColor Cyan
Write-Host "DisplayName: $DisplayName" -ForegroundColor Cyan
Write-Host "BackendTarget: $backendTargetRoot" -ForegroundColor Cyan
if (-not $ExternalPlugin) {
    Write-Host "FrontendTarget: frontend/modules/$moduleSlug" -ForegroundColor Cyan
}

if ($ExternalPlugin) {
    Copy-Item -LiteralPath $externalTemplateRoot -Destination $backendTargetRoot -Recurse
} else {
    Copy-Item -LiteralPath $backendTemplateRoot -Destination $backendTargetRoot -Recurse
    Copy-Item -LiteralPath $frontendTemplateRoot -Destination $frontendTargetRoot -Recurse
}

Remove-GeneratedArtifacts -RootPath $backendTargetRoot
if (-not $ExternalPlugin) {
    Remove-GeneratedArtifacts -RootPath $frontendTargetRoot
}

$backendProjectPath = Join-Path $backendTargetRoot "$ModuleName.csproj"
$backendModulePath = Join-Path $backendTargetRoot "${ModuleName}Module.cs"
$backendControllerPath = if ($ExternalPlugin) { Join-Path $backendTargetRoot "Controllers\${ModuleName}StatusController.cs" } else { Join-Path $backendTargetRoot "Controllers\${ModuleName}Controller.cs" }
$backendDtoPath = if ($ExternalPlugin) { Join-Path $backendTargetRoot "DTOs\${ModuleName}StatusResponse.cs" } else { Join-Path $backendTargetRoot "DTOs\${ModuleName}Response.cs" }
$backendInterfacePath = if ($ExternalPlugin) { Join-Path $backendTargetRoot "Services\I${ModuleName}StatusService.cs" } else { Join-Path $backendTargetRoot "Services\I${ModuleName}Service.cs" }
$backendServicePath = if ($ExternalPlugin) { Join-Path $backendTargetRoot "Services\${ModuleName}StatusService.cs" } else { Join-Path $backendTargetRoot "Services\${ModuleName}Service.cs" }

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

if (-not $ExternalPlugin) {
    $frontendPackagePath = Join-Path $frontendTargetRoot 'package.json'
    $frontendIndexPath = Join-Path $frontendTargetRoot 'src\index.ts'
    $frontendPagePath = Join-Path $frontendTargetRoot "src\pages\${frontendPageName}.tsx"
    $frontendServicePath = Join-Path $frontendTargetRoot "src\services\${frontendServiceFileBase}.ts"

    Rename-IfExists -FromPath (Join-Path $frontendTargetRoot 'src\pages\TemplatePage.tsx') -ToPath $frontendPagePath
    Rename-IfExists -FromPath (Join-Path $frontendTargetRoot 'src\services\templateService.ts') -ToPath $frontendServicePath
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
            "<AssemblyName>${ModuleName}Plugin</AssemblyName>"
        )
        $content = $content.Replace('__SDK_PROJECT_PATH__', $relativeSdkPath)
    } else {
        $content = $content.Replace(
            '<RootNamespace>ICP.Platform.Templates.BackendModuleTemplate</RootNamespace>',
            "<RootNamespace>ICP.Platform.Modules.$ModuleName</RootNamespace>"
        )
        $content = $content.Replace(
            '<AssemblyName>TemplateModule</AssemblyName>',
            "<AssemblyName>${ModuleName}Module</AssemblyName>"
        )
    }

    $content
}

Update-FileContent -Path $backendModulePath -Transform {
    param($content)

    if ($ExternalPlugin) {
        $content = $content.Replace('ICP.Platform.ExternalPluginTemplate', "ICP.Platform.ExternalPlugins.$ModuleName")
        $content = $content.Replace('IExternalPluginStatusService', "I${ModuleName}StatusService")
        $content = $content.Replace('ExternalPluginStatusService', "${ModuleName}StatusService")
        $content = $content.Replace('class ExternalPluginModule', "class ${ModuleName}Module")
        $content = [regex]::Replace($content, 'ModuleId\s*=>\s*"[^"]+";', ('ModuleId => "' + $ModuleName + '";'))
        $content = [regex]::Replace($content, 'DisplayName\s*=>\s*"[^"]+";', ('DisplayName => "' + $DisplayName + '";'))
        $content = [regex]::Replace($content, 'Description\s*=>\s*"[^"]+";', ('Description => "' + $DisplayName + ' external plugin";'))
        $content = $content.Replace('ExternalPlugin.View', ($ModuleName + '.View'))
        $content = [regex]::Replace($content, 'DisplayName\s*=\s*"[^"]+"', ('DisplayName = "View ' + $DisplayName + '"'))
        $content = [regex]::Replace($content, 'Group\s*=\s*"[^"]+"', ('Group = "' + $ModuleName + '"'))
    } else {
        $content = $content.Replace('ICP.Platform.Templates.BackendModuleTemplate', "ICP.Platform.Modules.$ModuleName")
        $content = $content.Replace('ITemplateService', "I${ModuleName}Service")
        $content = $content.Replace('TemplateService', "${ModuleName}Service")
        $content = $content.Replace('class TemplateModule', "class ${ModuleName}Module")
        $content = $content.Replace('=> "TemplateModule";', ('=> "' + $ModuleName + '";'))
        $content = $content.Replace('TemplateModule.View', ($ModuleName + '.View'))
        $content = $content.Replace('TemplateModule.Enabled', ($ModuleName + '.Enabled'))
    }

    $content
}

Update-FileContent -Path $backendControllerPath -Transform {
    param($content)

    if ($ExternalPlugin) {
        $content = $content.Replace('ICP.Platform.ExternalPluginTemplate', "ICP.Platform.ExternalPlugins.$ModuleName")
        $content = $content.Replace('ExternalPluginStatusResponse', "${ModuleName}StatusResponse")
        $content = $content.Replace('IExternalPluginStatusService', "I${ModuleName}StatusService")
        $content = $content.Replace('ExternalPluginStatusController', "${ModuleName}StatusController")
        $content = $content.Replace('api/external-plugin/status', "api/$moduleSlug/status")
    } else {
        $content = $content.Replace('ICP.Platform.Templates.BackendModuleTemplate', "ICP.Platform.Modules.$ModuleName")
        $content = $content.Replace('TemplateResponse', "${ModuleName}Response")
        $content = $content.Replace('ITemplateService', "I${ModuleName}Service")
        $content = $content.Replace('TemplateController', "${ModuleName}Controller")
        $content = $content.Replace('api/template-module', "api/$moduleSlug")
        $content = $content.Replace('ModuleId = "TemplateModule"', ('ModuleId = "' + $ModuleName + '"'))
    }

    $content
}

Update-FileContent -Path $backendDtoPath -Transform {
    param($content)

    if ($ExternalPlugin) {
        $content = $content.Replace('ICP.Platform.ExternalPluginTemplate', "ICP.Platform.ExternalPlugins.$ModuleName")
        $content = $content.Replace('ExternalPluginStatusResponse', "${ModuleName}StatusResponse")
    } else {
        $content = $content.Replace('ICP.Platform.Templates.BackendModuleTemplate', "ICP.Platform.Modules.$ModuleName")
        $content = $content.Replace('TemplateResponse', "${ModuleName}Response")
    }

    $content
}

Update-FileContent -Path $backendInterfacePath -Transform {
    param($content)

    if ($ExternalPlugin) {
        $content = $content.Replace('ICP.Platform.ExternalPluginTemplate', "ICP.Platform.ExternalPlugins.$ModuleName")
        $content = $content.Replace('IExternalPluginStatusService', "I${ModuleName}StatusService")
        $content = $content.Replace('ExternalPluginStatusResponse', "${ModuleName}StatusResponse")
    } else {
        $content = $content.Replace('ICP.Platform.Templates.BackendModuleTemplate', "ICP.Platform.Modules.$ModuleName")
        $content = $content.Replace('ITemplateService', "I${ModuleName}Service")
    }

    $content
}

Update-FileContent -Path $backendServicePath -Transform {
    param($content)

    if ($ExternalPlugin) {
        $content = $content.Replace('ICP.Platform.ExternalPluginTemplate', "ICP.Platform.ExternalPlugins.$ModuleName")
        $content = $content.Replace('ExternalPluginStatusService', "${ModuleName}StatusService")
        $content = $content.Replace('IExternalPluginStatusService', "I${ModuleName}StatusService")
        $content = $content.Replace('ExternalPluginStatusResponse', "${ModuleName}StatusResponse")
        $content = $content.Replace('ModuleId = "ExternalPlugin"', ('ModuleId = "' + $ModuleName + '"'))
        $content = $content.Replace('External plugin template is running.', "$DisplayName is running.")
    } else {
        $content = $content.Replace('ICP.Platform.Templates.BackendModuleTemplate', "ICP.Platform.Modules.$ModuleName")
        $content = $content.Replace('TemplateService', "${ModuleName}Service")
        $content = $content.Replace('ITemplateService', "I${ModuleName}Service")
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
    Update-FileContent -Path $frontendPackagePath -Transform {
        param($content)

        $content.Replace('@icp-templates/frontend-module-template', "@icp-modules/$moduleSlug")
    }

    Update-FileContent -Path $frontendIndexPath -Transform {
        param($content)

        $content = $content.Replace('TemplatePage', $frontendPageName)
        $content = $content.Replace('templateModule', $frontendModuleVariable)
        $content = $content.Replace("moduleId: 'TemplateModule'", "moduleId: '$moduleSlug'")
        $content = $content.Replace('TemplateModule.View', ($ModuleName + '.View'))
        $content = $content.Replace("path: '/template-module'", "path: '/$moduleSlug'")
        $content = $content.Replace("id: 'template-module'", "id: '$moduleSlug'")

        $content
    }

    Update-FileContent -Path $frontendServicePath -Transform {
        param($content)

        $content = $content.Replace('TemplateStatus', $frontendServiceName)
        $content = $content.Replace('fetchTemplateStatus', "fetch${ModuleName}Status")
        $content = $content.Replace('/api/template-module/status', "/api/$moduleSlug/status")

        $content
    }

    Update-FileContent -Path $frontendPagePath -Transform {
        param($content)

        $content = $content.Replace('TemplatePage', $frontendPageName)
        $content = $content.Replace('../services/templateService', "../services/$frontendServiceFileBase")
        $content = $content.Replace('TemplateStatus', $frontendServiceName)
        $content = $content.Replace('fetchTemplateStatus', "fetch${ModuleName}Status")
        $content = $content.Replace('/api/template-module/status', "/api/$moduleSlug/status")

        $content
    }
}

if (-not $ExternalPlugin) {
    $frontendPackageContent = @"
{
  "name": "@icp-modules/$moduleSlug",
  "version": "0.1.0",
  "type": "module",
  "main": "./dist/index.js",
  "types": "./dist/index.d.ts",
  "scripts": {
    "build": "tsc",
    "dev": "tsc --watch"
  },
  "dependencies": {
    "@icp-platform/sdk": "workspace:*"
  },
  "devDependencies": {
    "@types/react": "^18.3.1",
    "react": "^18.3.1",
    "typescript": "^5.4.2"
  }
}
"@

$frontendServiceContent = @"
export interface ModuleStatus {
  moduleId: string;
  message: string;
}

export async function fetchModuleStatus(): Promise<ModuleStatus> {
  const response = await fetch('/api/$moduleSlug/status');

  if (!response.ok) {
    throw new Error('Status request failed: ' + response.status);
  }

  return response.json();
}
"@

$frontendPageContent = @"
import { useEffect, useState } from 'react';
import { fetchModuleStatus, ModuleStatus } from '../services/$frontendServiceFileBase';

export default function $frontendPageName() {
  const [status, setStatus] = useState<ModuleStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void loadStatus();
  }, []);

  const loadStatus = async () => {
    try {
      setLoading(true);
      setError(null);
      const data = await fetchModuleStatus();
      setStatus(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : '发生未知错误');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="sh-page">
      <section className="sh-page-header">
        <div className="sh-page-header-main">
          <span className="sh-page-eyebrow">$DisplayName</span>
          <h1 className="sh-page-title">$DisplayName Page</h1>
          <p className="sh-page-description">
            This scaffold-generated page connects to `/api/$moduleSlug/status` by default.
          </p>
        </div>
      </section>

      <div className="sh-section-grid">
        <div className="sh-summary-card">
          <span className="sh-summary-card-label">Status</span>
          <span className="sh-summary-card-value">
            {loading ? 'Loading...' : status?.message ?? 'No data'}
          </span>
          <span className="sh-summary-card-meta">{status?.moduleId ?? '$ModuleName'}</span>
        </div>
        <div className="sh-summary-card">
          <span className="sh-summary-card-label">Endpoint</span>
          <span className="sh-summary-card-value">/api/$moduleSlug/status</span>
          <span className="sh-summary-card-meta">Minimal end-to-end status check</span>
        </div>
      </div>

      {error && (
        <div className="sh-helper-block" style={{ borderColor: '#ef4444' }}>
          <h3 className="sh-helper-block-title">Request Failed</h3>
          <p style={{ color: '#ef4444' }}>{error}</p>
        </div>
      )}

      <div className="sh-helper-block">
        <h3 className="sh-helper-block-title">Next Steps</h3>
        <p>Add business queries, forms, and actions on top of this scaffolded page.</p>
        <button
          onClick={() => {
            void loadStatus();
          }}
          style={{
            marginTop: '1rem',
            padding: '0.5rem 1rem',
            background: '#3b82f6',
            color: 'white',
            border: 'none',
            borderRadius: '4px',
            cursor: 'pointer'
          }}
        >
          Refresh Status
        </button>
      </div>
    </div>
  );
}
"@

$frontendIndexContent = @"
import { FrontendModule } from '@icp-platform/sdk';
import $frontendPageName from './pages/$frontendPageName';

export const ${frontendModuleVariable}: FrontendModule = {
  metadata: {
    moduleId: '$moduleSlug',
    displayName: '$DisplayName',
    version: '0.1.0',
    description: '$DisplayName 前端模块'
  },

  routes: [
    {
      path: '/$moduleSlug',
      component: $frontendPageName,
      permissions: ['${ModuleName}.View']
    }
  ],

  menus: [
    {
      id: '$moduleSlug',
      label: '$DisplayName',
      path: '/$moduleSlug',
      icon: '🧩',
      order: 999,
      permissions: ['${ModuleName}.View']
    }
  ],

  permissions: [
    {
      code: '${ModuleName}.View',
      name: 'View $DisplayName',
      description: 'Allows access to $DisplayName page',
      category: '$DisplayName'
    }
  ],

  i18n: [
    {
      locale: 'zh-CN',
      messages: {
        '${moduleSlug}.title': '$DisplayName'
      }
    },
    {
      locale: 'en-US',
      messages: {
        '${moduleSlug}.title': '$DisplayName'
      }
    }
  ]
};

export default ${frontendModuleVariable};
"@

    Set-Utf8Content -Path $frontendPackagePath -Content $frontendPackageContent
    Set-Utf8Content -Path $frontendServicePath -Content $frontendServiceContent
    Set-Utf8Content -Path $frontendPagePath -Content $frontendPageContent
    Set-Utf8Content -Path $frontendIndexPath -Content $frontendIndexContent
}

if ((-not $SkipIntegration) -and (-not $ExternalPlugin)) {
    $hostApiPath = Join-Path $repoRoot 'apps\host-api\HostApi.csproj'
    $builtinRuntimePluginsPath = Join-Path $frontendRoot 'packages\shell\src\builtin-runtime-plugins.ts'
    $projectReferenceLine = "    <ProjectReference Include=""..\..\server\modules\$ModuleName\$ModuleName.csproj"" />"
    $builtinModuleEntryLine = "  { moduleId: '$moduleSlug' },"

    Update-FileContent -Path $hostApiPath -Transform {
        param($content)

        if ($content.Contains($projectReferenceLine)) {
            return $content
        }

        $pattern = '<ItemGroup>\s*(?<body>(?:\s*<ProjectReference Include="[^"]+" />\s*)+)</ItemGroup>'
        $match = [regex]::Match($content, $pattern)
        if (-not $match.Success) {
            throw 'ProjectReference block not found.'
        }

        $newBlock = $match.Value.Replace('</ItemGroup>', "$projectReferenceLine`r`n  </ItemGroup>")
        $content.Replace($match.Value, $newBlock)
    }

    Update-FileContent -Path $builtinRuntimePluginsPath -Transform {
        param($content)

        if (-not $content.Contains($builtinModuleEntryLine)) {
            $content = [regex]::Replace(
                $content,
                '(?ms)(export const BUILTIN_RUNTIME_PLUGIN_ENTRIES:\s*BuiltinRuntimePluginEntry\[\]\s*=\s*\[[\s\S]*?)(\r?\n\];)',
                ('${1}' + "`r`n" + $builtinModuleEntryLine + '${2}'),
                1
            )
        }

        $content
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
    Write-Host 'Allowed integration targets: apps/host-api/HostApi.csproj and frontend/packages/shell/src/builtin-runtime-plugins.ts' -ForegroundColor Cyan
    Write-Host 'Do not modify other Host/Shell internals for normal module integration unless a separate design approves it.' -ForegroundColor Yellow
}

if ($SkipIntegration -and -not $ExternalPlugin) {
    Write-Host 'Integration skipped.' -ForegroundColor Yellow
}
