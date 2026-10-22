[CmdletBinding()]
param(
    # 历史升级场景才需要；只跑 CurrentRelease 场景时省略，登记里不写 baseline。
    [string]$BaselineHostRoot,
    # 忽略输入摘要，重建全部产物。
    [switch]$Force
)

# 把 componentized-development.md 的 Workspace 固定发行物准备收成一个入口：
# 按 bundle → Host → Shell → Acceptance 构建 → 登记的顺序执行，每段计时写入运行目录。
# 失败直接抛出原始错误，不产生失败码或裁决文件；timings.json 只是耗时记录。
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repoRoot

# Host 按自身所在目录向上找仓库根（同时含 apps、server、frontend/modules 的第一个祖先），
# 再用该工作树的 git 身份派生数据库所有权标记；baseline 与候选必须落在同一工作树。
if ($BaselineHostRoot) {
    $probe = (Resolve-Path -LiteralPath $BaselineHostRoot).Path
    while ($probe -and -not ((Test-Path -LiteralPath (Join-Path $probe 'apps')) -and (Test-Path -LiteralPath (Join-Path $probe 'server')) `
            -and (Test-Path -LiteralPath (Join-Path $probe 'frontend/modules')))) { $probe = Split-Path -Parent $probe }
    if ($probe -ne $repoRoot) {
        throw "BaselineHostRoot 位于另一个工作树（解析到 '$probe'，当前为 '$repoRoot'）：数据库所有权身份按 Host 所在工作树派生，baseline 与候选不在同一工作树会得到 PERSISTENCE_DATABASE_IDENTITY_MISMATCH。请把 baseline 复制到本工作树的 .codex-temp 下再传入。"
    }
}

$workspaceRoot = Join-Path $repoRoot '.codex-temp/workspace-delivery'
$statePath = Join-Path $workspaceRoot 'artifacts.json'
$excluded = 'InforCenter,Workspace,Part,Document'
$productRoots = @('server', 'apps', 'frontend', 'tools', 'Directory.Build.targets', 'ICP-Platform.sln', 'dotnet-tools.json')
$hostRoots = @('server', 'apps', 'tools', 'Directory.Build.targets', 'ICP-Platform.sln', 'dotnet-tools.json')
$shellRoots = @('frontend', 'Directory.Build.targets')

function Invoke-Native {
    param([string]$File, [string[]]$Arguments)
    # 子进程输出只显示，不进入函数返回值。
    & $File @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "命令失败（退出码 $LASTEXITCODE）：$File $($Arguments -join ' ')" }
}

# 摘要 = 受控路径下 tracked 与未忽略 untracked 文件的路径和内容，不含测试、文档与脚本之外的改动。
function Get-InputDigest {
    param([string[]]$Roots)
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    $files = & git -c core.quotepath=off ls-files -co --exclude-standard -- @Roots
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files 失败。' }
    $paths = @($files | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Sort-Object { $_ } -CaseSensitive)
    $hashes = $paths | & git hash-object --stdin-paths
    if ($LASTEXITCODE -ne 0) { throw 'git hash-object 失败。' }
    $text = (0..($paths.Count - 1) | ForEach-Object { "$($paths[$_])`t$($hashes[$_])" }) -join "`n"
    $sha = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

$state = if (Test-Path -LiteralPath $statePath) { Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
$run = Join-Path $workspaceRoot ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
New-Item -ItemType Directory -Path $run | Out-Null
$timings = [System.Collections.Generic.List[object]]::new()
$total = [Diagnostics.Stopwatch]::StartNew()

function Invoke-Stage {
    param([string]$Name, [scriptblock]$Action)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    Write-Host "== $Name"
    & $Action
    $timings.Add([ordered]@{ stage = $Name; status = 'ran'; seconds = [math]::Round($watch.Elapsed.TotalSeconds, 1) })
}

# 摘要未变且产物仍在时复用上一次产物；返回产物路径（新建在本次运行目录）。
function Resolve-Artifact {
    param([string]$Key, [string[]]$Roots, [string]$Leaf, [scriptblock]$Build)
    $digest = Get-InputDigest $Roots
    $previous = $state[$Key]
    if (-not $Force -and $previous -and $previous.digest -eq $digest -and (Test-Path -LiteralPath $previous.path)) {
        $timings.Add([ordered]@{ stage = $Key; status = 'reused'; seconds = 0; path = $previous.path })
        Write-Host "== $Key（复用 $($previous.path)）"
        return $previous.path
    }
    $path = Join-Path $run $Leaf
    Invoke-Stage $Key { & $Build $path }
    $state[$Key] = @{ digest = $digest; path = $path }
    $path
}

try {
    $bundleTool = 'server/tools/component-bundle/ComponentBundle.csproj'
    $package = Resolve-Artifact 'bundle' $productRoots 'bundle' {
        param($path)
        New-Item -ItemType Directory -Path $path | Out-Null
        $stage = Join-Path $path 'stage'
        $zip = Join-Path $path 'components.zip'
        Invoke-Native dotnet @('run', '--project', $bundleTool, '-c', 'Release', '--', 'build', $repoRoot, 'InforCenter', $stage)
        $common = @('run', '--project', $bundleTool, '-c', 'Release', '--no-build', '--no-restore', '--')
        Invoke-Native dotnet ($common + @('pack', $stage, $zip))
        Invoke-Native dotnet ($common + @('inspect', $zip))
    }
    $null = Resolve-Artifact 'host' $hostRoots 'host' {
        param($path)
        Invoke-Native dotnet @('publish', 'apps/host-api/HostApi.csproj', '-c', 'Release', '-t:Rebuild', '-p:DebugType=none',
            '-p:DebugSymbols=false', '-p:IncludeSourceRevisionInInformationalVersion=false',
            ('-p:NewpExcludedModuleIds=' + $excluded.Replace(',', '%2c')), '-o', $path)
    }
    $null = Resolve-Artifact 'shell' $shellRoots 'shell' {
        param($path)
        $env:NEWP_EXCLUDED_MODULE_IDS = $excluded
        try { Invoke-Native pnpm @('--dir', 'frontend', '--filter', '@icp-platform/shell', 'exec', 'vite', 'build', '--outDir', $path) }
        finally { Remove-Item Env:NEWP_EXCLUDED_MODULE_IDS -ErrorAction SilentlyContinue }
    }

    Invoke-Stage 'acceptance-build' {
        Invoke-Native dotnet @('build', 'tests/acceptance/Acceptance.Tests/Acceptance.Tests.csproj', '-c', 'Release')
    }

    $inputPath = Join-Path $run 'input.json'
    Invoke-Stage 'register' {
        $arguments = @('run', '--project', 'tests/acceptance/Acceptance.Runner/Acceptance.Runner.csproj', '-c', 'Release',
            '--no-build', '--no-restore', '--', '--workspace-delivery-input', $inputPath, '--repository-root', $repoRoot,
            '--host-root', $state['host'].path, '--shell-root', $state['shell'].path,
            '--package', (Join-Path $state['bundle'].path 'components.zip'))
        if ($BaselineHostRoot) { $arguments += @('--baseline-host-root', (Resolve-Path -LiteralPath $BaselineHostRoot).Path) }
        Invoke-Native dotnet $arguments
    }

    $state | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $statePath -Encoding utf8
    Set-Content -LiteralPath (Join-Path $workspaceRoot 'latest-input.txt') -Value $inputPath -Encoding utf8
    [ordered]@{ totalSeconds = [math]::Round($total.Elapsed.TotalSeconds, 1); stages = $timings } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'timings.json') -Encoding utf8
    Write-Host ''
    foreach ($timing in $timings) { Write-Host ('{0,-18} {1,-7} {2,6}s' -f $timing.stage, $timing.status, $timing.seconds) }
    Write-Host ('total              {0,14}s' -f [math]::Round($total.Elapsed.TotalSeconds, 1))
    Write-Host "`$env:NEWP_ACCEPTANCE_WORKSPACE_DELIVERY_INPUT = '$inputPath'"
}
catch {
    # 已完成阶段的产物仍可复用，先保存状态再原样抛出。
    $state | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $statePath -Encoding utf8
    throw
}
