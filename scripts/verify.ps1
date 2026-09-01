# 仓库唯一的验证入口。没有 CI，本机就是唯一的验证位置。
#
# -Quick 供开发循环使用：跳过只有全量验证才需要的三步（diff-check、
# dotnet restore、前端 build），其余门禁与完整跑法完全一致——不允许出现
# 「快跑绿了、慢跑才发现」的两套判据。
#
# 本脚本**不要求也不检查干净工作树**。本机是唯一的验证位置，脏树跑验证是常态，
# 拦住它等于把 verify 变成只有提交前才能用的东西。但结果必须能事后归属，
# 所以 result.json 记录 HEAD、起跑时的脏状态，以及工作树在跑的过程中是否变过。
# 归属靠记录，不靠设卡。
[CmdletBinding()]
param(
    [switch]$Quick,
    [ValidateSet("IdentityFoundation", "Personnel", "ExampleRequiredField")]
    [string[]]$AcceptanceGate = @(),
    [ValidateSet("Baseline", "Candidate")]
    [string]$ProductionColdStartMode,
    [string]$ProductionColdStartBaselineResult
)

$ErrorActionPreference = "Stop"
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

# 工作树快照只用于归属，不用于判定。取 porcelain 原文而不是布尔值，
# 是为了能分辨「一直脏」和「跑的过程中又变了」——后者才意味着结论无法归属。
function Get-WorkingTreeSnapshot {
    $entries = @(& git -C $repoRoot status --porcelain)
    return [ordered]@{
        Dirty = $entries.Count -gt 0
        EntryCount = $entries.Count
        Digest = ($entries -join "`n")
    }
}

$headCommit = (& git -C $repoRoot rev-parse HEAD)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($headCommit)) { $headCommit = "unknown" }
$startSnapshot = Get-WorkingTreeSnapshot

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
    if ($Status -ne "running") {
        $treeChangedDuringRun = -not [string]::Equals(
            (Get-WorkingTreeSnapshot).Digest, $startSnapshot.Digest, [StringComparison]::Ordinal)
        $endHeadCommit = (& git -C $repoRoot rev-parse HEAD)
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($endHeadCommit)) { $endHeadCommit = "unknown" }
        $headChangedDuringRun = -not [string]::Equals($endHeadCommit, $headCommit, [StringComparison]::Ordinal)
    }
    $result = [ordered]@{
        schemaVersion = "1.1"
        runId = $runId
        status = $Status
        quick = $Quick.IsPresent
        repositoryRoot = $repoRoot
        candidate = [ordered]@{
            headCommit = $headCommit
            dirty = $startSnapshot.Dirty
            dirtyEntryCount = $startSnapshot.EntryCount
            treeChangedDuringRun = $treeChangedDuringRun
            headChangedDuringRun = $headChangedDuringRun
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

    $currentStage = "model-dto-sync-check"
    Invoke-VerificationStage -Name $currentStage -FilePath "dotnet" -Arguments @(
        "run", "--project", "server/tools/model-dto-sync/ModelDtoSync.csproj",
        "--configuration", "Release", "--no-build", "--no-restore", "--", "--check", $repoRoot)

    $currentStage = "dotnet-test"
    Invoke-VerificationStage -Name $currentStage -FilePath "dotnet" -Arguments @(
        "test", "ICP-Platform.sln", "--configuration", "Release", "--no-build", "--no-restore")

    $currentStage = "frontend-test"
    Invoke-VerificationStage -Name $currentStage -FilePath $corepackPath -Arguments @(
        "pnpm", "--dir", "frontend", "test")

    if (-not $Quick)
    {
        $currentStage = "frontend-build"
        Invoke-VerificationStage -Name $currentStage -FilePath $corepackPath -Arguments @(
            "pnpm", "--dir", "frontend", "build")
    }

    foreach ($gate in $AcceptanceGate)
    {
        $currentStage = "acceptance-" + $gate.ToLowerInvariant()
        Invoke-VerificationStage -Name $currentStage -FilePath $pwshPath -Arguments @(
            "-NoLogo", "-NoProfile", "-File", (Join-Path $PSScriptRoot "accept.ps1"),
            "-Gate", $gate, "-NoBuild")
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
        Write-Host "已跳过 diff-check、dotnet restore 与前端 build；合并前请跑一次完整 verify.ps1。" -ForegroundColor Yellow
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
