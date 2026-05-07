param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("init", "update", "block", "complete-task", "plan-completed", "show")]
    [string]$Action,

    [Parameter(Mandatory = $true)]
    [string]$PlanPath,

    [string]$LockPath = "",
    [string]$TaskTitle = "",
    [int]$TaskIndex = -1,
    [string]$TaskHeadingHash = "",
    [string]$Step = "",
    [string]$ChangedFile = "",
    [string]$VerificationCommand = "",
    [string]$ReviewResult = "",
    [string]$LastFailedCommand = "",
    [string]$Reason = "",
    [string]$NextAction = "",
    [switch]$IncrementVerificationFailure,
    [switch]$IncrementReviewFailure
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    $root = git rev-parse --show-toplevel 2>$null
    if ([string]::IsNullOrWhiteSpace($root)) {
        throw "Must run inside a git repository."
    }

    return (Resolve-Path -LiteralPath $root.Trim()).Path
}

function Convert-ToRepoRelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $fullPath = $Path
    if (-not [System.IO.Path]::IsPathRooted($fullPath)) {
        $fullPath = Join-Path $RepoRoot $Path
    }

    $resolved = [System.IO.Path]::GetFullPath($fullPath)
    $rootWithSeparator = $RepoRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside repository: $Path"
    }

    return $resolved.Substring($rootWithSeparator.Length).Replace("\", "/")
}

function Get-PlanId {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PlanPath
    )

    $fileName = [System.IO.Path]::GetFileNameWithoutExtension($PlanPath)
    $slug = $fileName.ToLowerInvariant() -replace "[^a-z0-9\-]+", "-"
    $slug = $slug.Trim("-")
    if ([string]::IsNullOrWhiteSpace($slug)) {
        throw "Unable to derive plan id from: $PlanPath"
    }

    return $slug
}

function Assert-AllowedLockPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LockPath
    )

    if ($LockPath -notmatch '^\.codex/automation-locks/[^/]+\.lock$') {
        throw "Lock path is not allowed: $LockPath. Expected .codex/automation-locks/*.lock"
    }
}

function New-EmptyState {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PlanPath,
        [Parameter(Mandatory = $true)]
        [string]$LockPath
    )

    [ordered]@{
        planPath = $PlanPath
        lockPath = $LockPath
        status = "RUNNING"
        targetTaskTitle = ""
        targetTaskIndex = -1
        targetTaskHeadingHash = ""
        startedAt = (Get-Date).ToString("o")
        updatedAt = (Get-Date).ToString("o")
        completedSteps = @()
        changedFiles = @()
        verificationCommands = @()
        verificationFailureCount = 0
        reviewFailureCount = 0
        lastFailedCommand = ""
        reviewResults = @()
        blockerReason = ""
        nextAction = ""
    }
}

function Read-State {
    param(
        [Parameter(Mandatory = $true)]
        [string]$StatePath
    )

    if (-not (Test-Path -LiteralPath $StatePath)) {
        return $null
    }

    return Get-Content -Raw -LiteralPath $StatePath -Encoding UTF8 | ConvertFrom-Json
}

function Save-State {
    param(
        [Parameter(Mandatory = $true)]
        [string]$StatePath,
        [Parameter(Mandatory = $true)]
        $State
    )

    $State.updatedAt = (Get-Date).ToString("o")
    $directory = Split-Path -Parent $StatePath
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $State | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $StatePath -Encoding UTF8
}

function Add-UniqueValue {
    param(
        [Parameter(Mandatory = $true)]
        [AllowNull()]
        [AllowEmptyCollection()]
        [object[]]$Values,
        [string]$Value
    )

    if ($null -eq $Values) {
        $Values = @()
    }

    $normalizedValues = @()
    foreach ($existingValue in @($Values)) {
        if (-not [string]::IsNullOrWhiteSpace($existingValue)) {
            $normalizedValues += $existingValue
        }
    }

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return @($normalizedValues)
    }

    if ($normalizedValues -contains $Value) {
        return @($normalizedValues)
    }

    return @($normalizedValues) + $Value
}

function Touch-Lock {
    param(
        [string]$LockPath
    )

    if ([string]::IsNullOrWhiteSpace($LockPath)) {
        return
    }

    if (Test-Path -LiteralPath $LockPath) {
        (Get-Item -LiteralPath $LockPath).LastWriteTime = Get-Date
    }
}

$repoRoot = Get-RepoRoot
$planRelativePath = Convert-ToRepoRelativePath -RepoRoot $repoRoot -Path $PlanPath
$planId = Get-PlanId -PlanPath $planRelativePath

if ([string]::IsNullOrWhiteSpace($LockPath)) {
    $LockPath = ".codex/automation-locks/$planId.lock"
}

$lockRelativePath = Convert-ToRepoRelativePath -RepoRoot $repoRoot -Path $LockPath
Assert-AllowedLockPath -LockPath $lockRelativePath
$statePath = Join-Path $repoRoot ".codex-temp/plan-runs/$planId.json"
$lockFullPath = Join-Path $repoRoot $lockRelativePath

$state = Read-State -StatePath $statePath
if ($null -eq $state) {
    $state = New-EmptyState -PlanPath $planRelativePath -LockPath $lockRelativePath
}

if ($Action -eq "init") {
    $state = New-EmptyState -PlanPath $planRelativePath -LockPath $lockRelativePath
    $state.targetTaskTitle = $TaskTitle
    $state.targetTaskIndex = $TaskIndex
    $state.targetTaskHeadingHash = $TaskHeadingHash
}
elseif ($Action -eq "update") {
    $state.status = "RUNNING"
}
elseif ($Action -eq "block") {
    $state.status = "BLOCKED"
    $state.blockerReason = $Reason
    $state.nextAction = $NextAction
}
elseif ($Action -eq "complete-task") {
    $state.status = "COMPLETED_TASK"
}
elseif ($Action -eq "plan-completed") {
    $state.status = "PLAN_COMPLETED"
}
elseif ($Action -eq "show") {
    $state | ConvertTo-Json -Depth 8
    return
}

if (-not [string]::IsNullOrWhiteSpace($TaskTitle)) {
    $state.targetTaskTitle = $TaskTitle
}

if ($TaskIndex -ge 0) {
    $state.targetTaskIndex = $TaskIndex
}

if (-not [string]::IsNullOrWhiteSpace($TaskHeadingHash)) {
    $state.targetTaskHeadingHash = $TaskHeadingHash
}

$state.completedSteps = Add-UniqueValue -Values @($state.completedSteps) -Value $Step
$state.changedFiles = Add-UniqueValue -Values @($state.changedFiles) -Value $ChangedFile
$state.verificationCommands = Add-UniqueValue -Values @($state.verificationCommands) -Value $VerificationCommand
$state.reviewResults = Add-UniqueValue -Values @($state.reviewResults) -Value $ReviewResult

if ($IncrementVerificationFailure) {
    $state.verificationFailureCount = [int]$state.verificationFailureCount + 1
}

if ($IncrementReviewFailure) {
    $state.reviewFailureCount = [int]$state.reviewFailureCount + 1
}

if (-not [string]::IsNullOrWhiteSpace($LastFailedCommand)) {
    $state.lastFailedCommand = $LastFailedCommand
}

Save-State -StatePath $statePath -State $state
Touch-Lock -LockPath $lockFullPath

Write-Host "Plan run state updated: $statePath"
Write-Host "Status: $($state.status)"
