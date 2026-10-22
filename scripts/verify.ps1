# 仓库完整验证入口。局部修改按 quick-verification.md 选择定向检查；没有 CI，本机是唯一的验证位置。
#
# -Quick 仍是全仓验证变体：跳过 diff-check、独立 dotnet restore、前端 lint/build。
# 它不是定向入口；明确要求完整验证的交付节点使用完整模式，并按验收条件显式选择场景。
#
# 本脚本**不要求也不检查干净工作树**。本机是唯一的验证位置，脏树跑验证是常态，
# 拦住它等于把 verify 变成只有提交前才能用的东西。但结果必须能事后归属，
# 所以 result.json 记录 HEAD、起跑时的脏状态，以及工作树在跑的过程中是否变过。
# 归属靠记录，不靠设卡。
[CmdletBinding()]
param(
    [switch]$Quick,
    [string[]]$AcceptanceScenario = @(),
    [ValidateSet("Baseline", "Candidate")]
    [string]$ProductionColdStartMode,
    [string]$ProductionColdStartBaselineResult
)

$ErrorActionPreference = "Stop"
# 不设模块默认集；列表逐项传给 accept 的精确方法名入口。
$AcceptanceScenario = @($AcceptanceScenario | ForEach-Object { $_.Split(',') } | Select-Object -Unique)
if (@($AcceptanceScenario | Where-Object {
    $_ -cnotmatch '\A[_\p{L}\p{Nl}][_\p{L}\p{Nl}\p{Nd}\p{Pc}\p{Mn}\p{Mc}\p{Cf}]*\z' -or
    $_ -in @('All', 'Workspace', 'Personnel', 'IdentityFoundation', 'Authorization', 'ExampleRequiredField')
}).Count -gt 0) { throw 'Specify exact AcceptanceGateTests method names with -AcceptanceScenario; module groups and All are retired.' }
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$runId = "{0}-{1}" -f (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssfffZ"), ([guid]::NewGuid().ToString("N"))
$outputRoot = Join-Path $repoRoot ".codex-temp\verify"
$runDirectory = Join-Path $outputRoot "runs\$runId"
$resultPath = Join-Path $runDirectory "result.json"
$currentResultPath = Join-Path $outputRoot "current-result.json"
$msBuildDiagnosticDirectory = Join-Path $runDirectory "msbuild"
$startedAtUtc = [DateTimeOffset]::UtcNow
$stageResults = [System.Collections.Generic.List[object]]::new()
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$pwshPath = (Get-Process -Id $PID).Path
$corepackPath = if ($IsWindows) { "corepack.cmd" } else { "corepack" }

New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $msBuildDiagnosticDirectory -Force | Out-Null
$env:MSBUILDDEBUGPATH = $msBuildDiagnosticDirectory

# 工作树快照只用于归属，不用于判定。porcelain 只负责计数；Digest 使用临时 index
# 生成真实 tree hash，避免同一个已脏路径内容改变后仍被误判为没有变化。
# 临时 index 从 HEAD 开始吸收当前工作树，不读取或修改用户的真实暂存区。
function Test-GitObjectId {
    param(
        [AllowNull()]
        [string]$Value
    )

    return -not [string]::IsNullOrWhiteSpace($Value) -and
        $Value -cmatch '\A(?:[0-9a-f]{40}|[0-9a-f]{64})\z'
}

function Get-WorkingTreeSnapshot {
    $entries = @(& git -C $repoRoot status --porcelain 2>$null)
    $statusExitCode = $LASTEXITCODE
    if ($statusExitCode -ne 0) {
        throw "VERIFY_CANDIDATE_SNAPSHOT_FAILED: git status exited with code $statusExitCode"
    }
    $temporaryIndexPath = Join-Path $runDirectory ("candidate-{0}.index" -f ([guid]::NewGuid().ToString("N")))
    $temporaryObjectDirectory = $temporaryIndexPath + "-objects"
    $objectDirectoryOutput = @(& git -C $repoRoot rev-parse --git-path objects 2>$null)
    $objectDirectoryExitCode = $LASTEXITCODE
    $objectDirectory = $objectDirectoryOutput | Select-Object -Last 1
    if ($objectDirectoryExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($objectDirectory)) {
        throw "VERIFY_CANDIDATE_SNAPSHOT_FAILED: git object directory is unavailable"
    }
    if (-not [System.IO.Path]::IsPathRooted($objectDirectory)) {
        $objectDirectory = Join-Path $repoRoot $objectDirectory
    }
    $objectDirectory = [System.IO.Path]::GetFullPath($objectDirectory)

    $previousGitIndexFile = $env:GIT_INDEX_FILE
    $previousGitObjectDirectory = $env:GIT_OBJECT_DIRECTORY
    $previousGitAlternateObjectDirectories = $env:GIT_ALTERNATE_OBJECT_DIRECTORIES
    try {
        New-Item -ItemType Directory -Path $temporaryObjectDirectory -Force | Out-Null
        $env:GIT_INDEX_FILE = $temporaryIndexPath
        $env:GIT_OBJECT_DIRECTORY = $temporaryObjectDirectory
        $env:GIT_ALTERNATE_OBJECT_DIRECTORIES = if ([string]::IsNullOrWhiteSpace($previousGitAlternateObjectDirectories)) {
            $objectDirectory
        }
        else {
            $objectDirectory + [System.IO.Path]::PathSeparator + $previousGitAlternateObjectDirectories
        }

        $null = @(& git -C $repoRoot read-tree HEAD 2>$null)
        $readTreeExitCode = $LASTEXITCODE
        if ($readTreeExitCode -ne 0) {
            throw "VERIFY_CANDIDATE_SNAPSHOT_FAILED: git read-tree exited with code $readTreeExitCode"
        }

        $null = @(& git -C $repoRoot add -A -- . 2>$null)
        $addExitCode = $LASTEXITCODE
        if ($addExitCode -ne 0) {
            throw "VERIFY_CANDIDATE_SNAPSHOT_FAILED: git add exited with code $addExitCode"
        }

        $writeTreeOutput = @(& git -C $repoRoot write-tree 2>$null)
        $writeTreeExitCode = $LASTEXITCODE
        $treeHash = [string]($writeTreeOutput | Select-Object -Last 1)
        if ($writeTreeExitCode -ne 0) {
            throw "VERIFY_CANDIDATE_SNAPSHOT_FAILED: git write-tree exited with code $writeTreeExitCode"
        }
        $treeHash = $treeHash.Trim()
        if (-not (Test-GitObjectId -Value $treeHash)) {
            throw "VERIFY_CANDIDATE_SNAPSHOT_FAILED: git write-tree returned an invalid object id"
        }

        return [ordered]@{
            Dirty = $entries.Count -gt 0
            EntryCount = $entries.Count
            Digest = $treeHash
        }
    }
    finally {
        if ($null -eq $previousGitIndexFile) {
            Remove-Item Env:\GIT_INDEX_FILE -ErrorAction SilentlyContinue
        }
        else {
            $env:GIT_INDEX_FILE = $previousGitIndexFile
        }
        if ($null -eq $previousGitObjectDirectory) {
            Remove-Item Env:\GIT_OBJECT_DIRECTORY -ErrorAction SilentlyContinue
        }
        else {
            $env:GIT_OBJECT_DIRECTORY = $previousGitObjectDirectory
        }
        if ($null -eq $previousGitAlternateObjectDirectories) {
            Remove-Item Env:\GIT_ALTERNATE_OBJECT_DIRECTORIES -ErrorAction SilentlyContinue
        }
        else {
            $env:GIT_ALTERNATE_OBJECT_DIRECTORIES = $previousGitAlternateObjectDirectories
        }
        Remove-Item -LiteralPath $temporaryIndexPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath ($temporaryIndexPath + ".lock") -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $temporaryObjectDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# 快照失败不许拦住验证。这一段只负责让结论可追溯，它自己出问题时应当留下
# 「这次无法归属」的记录，而不是把整轮验证一起带走——尤其是它跑在写出第一份
# result.json 之前，抛出去会连失败记录都没有。归属靠记录，不靠设卡。
function Get-WorkingTreeSnapshotOrUnavailable {
    try {
        return Get-WorkingTreeSnapshot
    }
    catch {
        $diagnostic = $_.Exception.Message
        if (-not $diagnostic.StartsWith("VERIFY_CANDIDATE_SNAPSHOT_FAILED:", [StringComparison]::Ordinal)) {
            $diagnostic = "VERIFY_CANDIDATE_SNAPSHOT_FAILED: unexpected snapshot error"
        }
        return [ordered]@{
            Dirty = $null
            EntryCount = $null
            Digest = $null
            Diagnostic = $diagnostic
        }
    }
}

function Get-HeadCommitOrUnavailable {
    try {
        $headOutput = @(& git -C $repoRoot rev-parse HEAD 2>$null)
        $headExitCode = $LASTEXITCODE
        $commit = [string]($headOutput | Select-Object -Last 1)
        if ($headExitCode -ne 0) {
            return [ordered]@{
                Commit = $null
                Diagnostic = "VERIFY_CANDIDATE_HEAD_FAILED: git rev-parse exited with code $headExitCode"
            }
        }
        $commit = $commit.Trim()
        if (-not (Test-GitObjectId -Value $commit)) {
            return [ordered]@{
                Commit = $null
                Diagnostic = "VERIFY_CANDIDATE_HEAD_FAILED: git rev-parse returned an invalid object id"
            }
        }
        return [ordered]@{
            Commit = $commit
            Diagnostic = $null
        }
    }
    catch {
        return [ordered]@{
            Commit = $null
            Diagnostic = "VERIFY_CANDIDATE_HEAD_FAILED: unexpected HEAD probe error"
        }
    }
}

$startHead = Get-HeadCommitOrUnavailable
$startSnapshot = Get-WorkingTreeSnapshotOrUnavailable

function Save-VerificationResult {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("running", "passed", "failed")]
        [string]$Status,
        [string]$FailureStage,
        [string]$FailureMessage
    )

    $capturedAtUtc = [DateTimeOffset]::UtcNow
    $completedAtUtc = if ($Status -eq "running") { $null } else { $capturedAtUtc.ToString("O") }
    # 只在收尾时再取一次快照：每个阶段都跑 git status 没有意义，
    # 而「跑的过程中变过没有」只在有结论时才需要回答。
    #
    # HEAD 单独比一次：跑到一半提交时，已跟踪改动会同时从工作树消失，
    # porcelain 前后可以完全一样，只看工作树察觉不到候选已经换了。
    $treeChangedDuringRun = $null
    $headChangedDuringRun = $null
    $candidateDiagnostics = [System.Collections.Generic.List[string]]::new()
    foreach ($diagnostic in @($startHead.Diagnostic, $startSnapshot.Diagnostic)) {
        if (-not [string]::IsNullOrWhiteSpace($diagnostic)) { $candidateDiagnostics.Add($diagnostic) }
    }
    if ($Status -ne "running") {
        $endSnapshot = Get-WorkingTreeSnapshotOrUnavailable
        if (-not [string]::IsNullOrWhiteSpace($endSnapshot.Diagnostic)) {
            $candidateDiagnostics.Add($endSnapshot.Diagnostic)
        }
        # 任一端取不到就留 null：拿不到 digest 时说「没变过」是编造，
        # 而 snapshotDiagnostic 会说明为什么答不上来。
        if ($null -ne $startSnapshot.Digest -and $null -ne $endSnapshot.Digest) {
            $treeChangedDuringRun = -not [string]::Equals(
                $endSnapshot.Digest, $startSnapshot.Digest, [StringComparison]::Ordinal)
        }
        $endHead = Get-HeadCommitOrUnavailable
        if (-not [string]::IsNullOrWhiteSpace($endHead.Diagnostic)) {
            $candidateDiagnostics.Add($endHead.Diagnostic)
        }
        # HEAD 也只比较两个真实值；任一端不可用时留 null，不用哨兵值编造结论。
        if ($null -ne $startHead.Commit -and $null -ne $endHead.Commit) {
            $headChangedDuringRun = -not [string]::Equals(
                $endHead.Commit, $startHead.Commit, [StringComparison]::Ordinal)
        }
    }
    $snapshotDiagnostic = if ($candidateDiagnostics.Count -gt 0) {
        $candidateDiagnostics -join "; "
    }
    else {
        $null
    }
    $result = [ordered]@{
        schemaVersion = "1.2"
        runId = $runId
        status = $Status
        quick = $Quick.IsPresent
        repositoryRoot = $repoRoot
        candidate = [ordered]@{
            headCommit = $startHead.Commit
            dirty = $startSnapshot.Dirty
            dirtyEntryCount = $startSnapshot.EntryCount
            treeChangedDuringRun = $treeChangedDuringRun
            headChangedDuringRun = $headChangedDuringRun
            snapshotDiagnostic = $snapshotDiagnostic
        }
        startedAtUtc = $startedAtUtc.ToString("O")
        completedAtUtc = $completedAtUtc
        elapsedMs = [long]($capturedAtUtc - $startedAtUtc).TotalMilliseconds
        failureStage = $FailureStage
        failureMessage = $FailureMessage
        msbuildDiagnosticPath = [System.IO.Path]::GetRelativePath($repoRoot, $msBuildDiagnosticDirectory).Replace('\', '/')
        stages = @($stageResults)
    }
    $json = $result | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($resultPath, $json + [Environment]::NewLine, $utf8NoBom)
    [System.IO.File]::WriteAllText($currentResultPath, $json + [Environment]::NewLine, $utf8NoBom)
}

function Invoke-VerificationStage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $stageStartedAtUtc = [DateTimeOffset]::UtcNow
    $logPath = Join-Path $runDirectory ($Name + ".log")
    $displayCommand = (@($FilePath) + $Arguments) -join " "
    Write-Host ""
    Write-Host "=== $Name ===" -ForegroundColor Green
    Write-Host $displayCommand -ForegroundColor DarkGray

    $exitCode = 1
    $failureMessage = $null
    Push-Location $repoRoot
    try {
        & $FilePath @Arguments 2>&1 | Tee-Object -FilePath $logPath
        $exitCode = $LASTEXITCODE
    }
    catch {
        $failureMessage = $_.Exception.Message
        [System.IO.File]::AppendAllText($logPath, $failureMessage + [Environment]::NewLine, $utf8NoBom)
    }
    finally {
        Pop-Location
    }

    $stageCompletedAtUtc = [DateTimeOffset]::UtcNow
    $stageResults.Add([ordered]@{
        name = $Name
        command = $displayCommand
        startedAtUtc = $stageStartedAtUtc.ToString("O")
        completedAtUtc = $stageCompletedAtUtc.ToString("O")
        elapsedMs = [long]($stageCompletedAtUtc - $stageStartedAtUtc).TotalMilliseconds
        exitCode = $exitCode
        logPath = [System.IO.Path]::GetRelativePath($repoRoot, $logPath).Replace('\', '/')
    })
    Save-VerificationResult -Status "running"

    if ($exitCode -ne 0)
    {
        if ([string]::IsNullOrWhiteSpace($failureMessage)) {
            $failureMessage = "stage exited with code $exitCode"
        }
        throw "VERIFY_STAGE_FAILED: $Name - $failureMessage"
    }
}

Save-VerificationResult -Status "running"
$currentStage = $null
try {
    if (-not $Quick)
    {
        $currentStage = "diff-check"
        Invoke-VerificationStage -Name $currentStage -FilePath "git" -Arguments @("diff", "--check")
    }

    $currentStage = "current-docs"
    Invoke-VerificationStage -Name $currentStage -FilePath $pwshPath -Arguments @(
        "-NoLogo", "-NoProfile", "-File", (Join-Path $PSScriptRoot "check-current-docs.ps1"),
        "-Root", $repoRoot)

    $currentStage = "dic-localization"
    Invoke-VerificationStage -Name $currentStage -FilePath $pwshPath -Arguments @(
        "-NoLogo", "-NoProfile", "-File", (Join-Path $PSScriptRoot "validate-dic-localization.ps1"),
        "-Root", $repoRoot)

    if ($Quick)
    {
        # 开发循环里包引用很少变；让 build 自己按需 restore。
        $currentStage = "dotnet-build"
        Invoke-VerificationStage -Name $currentStage -FilePath "dotnet" -Arguments @(
            "build", "ICP-Platform.sln", "--configuration", "Release")
    }
    else
    {
        $currentStage = "dotnet-restore"
        Invoke-VerificationStage -Name $currentStage -FilePath "dotnet" -Arguments @(
            "restore", "ICP-Platform.sln")

        $currentStage = "dotnet-build"
        Invoke-VerificationStage -Name $currentStage -FilePath "dotnet" -Arguments @(
            "build", "ICP-Platform.sln", "--configuration", "Release", "--no-restore")
    }

    $currentStage = "migration-manifest-preflight"
    Invoke-VerificationStage -Name $currentStage -FilePath "dotnet" -Arguments @(
        "run", "--project", "server/tools/migration-manifest/MigrationManifest.csproj",
        "--configuration", "Release", "--no-build", "--no-restore", "--",
        "check", "--all", "--no-build", "--repository-root", $repoRoot)

    $currentStage = "model-dto-sync-check"
    Invoke-VerificationStage -Name $currentStage -FilePath "dotnet" -Arguments @(
        "run", "--project", "server/tools/model-dto-sync/ModelDtoSync.csproj",
        "--configuration", "Release", "--no-build", "--no-restore", "--", "--check", $repoRoot)

    # 普通仓库测试排除显式验收入口，避免继承的环境开关扩大运行范围。
    $currentStage = "dotnet-test"
    Invoke-VerificationStage -Name $currentStage -FilePath "dotnet" -Arguments @(
        "test", "ICP-Platform.sln", "--configuration", "Release", "--no-build", "--no-restore",
        "--filter", "FullyQualifiedName!~ICP.Platform.Acceptance.Tests.AcceptanceGateTests.")

    if (-not $Quick)
    {
        $currentStage = "frontend-lint"
        Invoke-VerificationStage -Name $currentStage -FilePath $corepackPath -Arguments @(
            "pnpm", "--dir", "frontend", "lint")
    }

    $currentStage = "frontend-test"
    Invoke-VerificationStage -Name $currentStage -FilePath $corepackPath -Arguments @(
        "pnpm", "--dir", "frontend", "test")

    if (-not $Quick)
    {
        $currentStage = "frontend-build"
        Invoke-VerificationStage -Name $currentStage -FilePath $corepackPath -Arguments @(
            "pnpm", "--dir", "frontend", "build")
    }

    foreach ($scenario in $AcceptanceScenario)
    {
        $currentStage = "acceptance-" + $scenario
        Invoke-VerificationStage -Name $currentStage -FilePath $pwshPath -Arguments @(
            "-NoLogo", "-NoProfile", "-File", (Join-Path $PSScriptRoot "accept.ps1"),
            "-Scenario", $scenario, "-NoBuild")
    }

    if (-not [string]::IsNullOrWhiteSpace($ProductionColdStartMode))
    {
        $currentStage = "production-cold-start-" + $ProductionColdStartMode.ToLowerInvariant()
        $coldStartArguments = @(
            "-NoLogo", "-NoProfile", "-File", (Join-Path $PSScriptRoot "accept-production-cold-start.ps1"),
            "-Mode", $ProductionColdStartMode)
        if (-not [string]::IsNullOrWhiteSpace($ProductionColdStartBaselineResult))
        {
            $coldStartArguments += @("-BaselineResult", $ProductionColdStartBaselineResult)
        }
        Invoke-VerificationStage -Name $currentStage -FilePath $pwshPath -Arguments $coldStartArguments
    }

    Save-VerificationResult -Status "passed"
    Write-Host ""
    if ($Quick)
    {
        Write-Host "Quick 仍覆盖全仓，已跳过 diff-check、独立 dotnet restore 与前端 lint/build；明确要求完整验证的交付节点请使用完整模式，并按验收条件选择场景。" -ForegroundColor Yellow
    }
    Write-Host "VERIFY_RESULT: $resultPath" -ForegroundColor Green
    exit 0
}
catch {
    Save-VerificationResult -Status "failed" -FailureStage $currentStage -FailureMessage $_.Exception.Message
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine("VERIFY_RESULT: $resultPath")
    exit 1
}
