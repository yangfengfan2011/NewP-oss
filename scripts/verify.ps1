[CmdletBinding()]
param(
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
    $result = [ordered]@{
        schemaVersion = "1.0"
        runId = $runId
        status = $Status
        repositoryRoot = $repoRoot
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
    $currentStage = "diff-check"
    Invoke-VerificationStage -Name $currentStage -FilePath "git" -Arguments @("diff", "--check")

    $currentStage = "current-docs"
    Invoke-VerificationStage -Name $currentStage -FilePath $pwshPath -Arguments @(
        "-NoLogo", "-NoProfile", "-File", (Join-Path $PSScriptRoot "check-current-docs.ps1"),
        "-Root", $repoRoot)

    $currentStage = "dic-localization"
    Invoke-VerificationStage -Name $currentStage -FilePath $pwshPath -Arguments @(
        "-NoLogo", "-NoProfile", "-File", (Join-Path $PSScriptRoot "validate-dic-localization.ps1"),
        "-Root", $repoRoot)

    $currentStage = "dotnet-restore"
    Invoke-VerificationStage -Name $currentStage -FilePath "dotnet" -Arguments @(
        "restore", "ICP-Platform.sln")

    $currentStage = "dotnet-build"
    Invoke-VerificationStage -Name $currentStage -FilePath "dotnet" -Arguments @(
        "build", "ICP-Platform.sln", "--configuration", "Release", "--no-restore")

    $currentStage = "model-dto-sync-check"
    Invoke-VerificationStage -Name $currentStage -FilePath "dotnet" -Arguments @(
        "run", "--project", "server/tools/model-dto-sync/ModelDtoSync.csproj",
        "--configuration", "Release", "--no-build", "--no-restore", "--", "--check", $repoRoot)

    $currentStage = "dotnet-test"
    Invoke-VerificationStage -Name $currentStage -FilePath "dotnet" -Arguments @(
        "test", "ICP-Platform.sln", "--configuration", "Release", "--no-build", "--no-restore", "--maxcpucount:1")

    $currentStage = "frontend-test"
    Invoke-VerificationStage -Name $currentStage -FilePath $corepackPath -Arguments @(
        "pnpm", "--dir", "frontend", "test")

    $currentStage = "frontend-build"
    Invoke-VerificationStage -Name $currentStage -FilePath $corepackPath -Arguments @(
        "pnpm", "--dir", "frontend", "build")

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
    Write-Host "VERIFY_RESULT: $resultPath" -ForegroundColor Green
    exit 0
}
catch {
    Save-VerificationResult -Status "failed" -FailureStage $currentStage -FailureMessage $_.Exception.Message
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine("VERIFY_RESULT: $resultPath")
    exit 1
}
