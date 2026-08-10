Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$supervisorLibrary = Join-Path $repoRoot "scripts\lib\dev-supervisor.ps1"

function Assert-True {
    param([bool]$Condition, [string]$Message)

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-False {
    param([bool]$Condition, [string]$Message)

    Assert-True (-not $Condition) $Message
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)

    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', actual '$Actual'."
    }
}

function Assert-ThrowsCode {
    param([scriptblock]$Action, [string]$Code)

    try {
        & $Action
    }
    catch {
        Assert-True ($_.Exception.Message.StartsWith("$Code`:", [StringComparison]::Ordinal)) "Expected failure code $Code but received '$($_.Exception.Message)'."
        return
    }

    throw "Expected failure code $Code but the action succeeded."
}

if (-not (Test-Path -LiteralPath $supervisorLibrary)) {
    throw "DEV_SUPERVISOR_LIBRARY_MISSING: $supervisorLibrary"
}

. $supervisorLibrary

$fingerprint = Get-DevCommandFingerprint -FilePath "C:\Program Files\dotnet\dotnet.exe" -Arguments @("run", "--project", "D:\Work\Git\NewP\apps\host-api\HostApi.csproj")
$equivalentFingerprint = Get-DevCommandFingerprint -FilePath "C:\Program Files\dotnet\DOTNET.exe" -Arguments @("run", "--project", "D:\Work\Git\NewP\apps\host-api\HostApi.csproj")
Assert-Equal $fingerprint $equivalentFingerprint "Command fingerprints must be stable across Windows path casing."

$expectedProcess = [pscustomobject]@{
    pid = 4100
    startTimeUtc = "2026-08-08T01:02:03.0000000Z"
    commandFingerprint = $fingerprint
}
$sameProcess = [pscustomobject]@{
    pid = 4100
    startTimeUtc = "2026-08-08T01:02:03.0000000Z"
    commandFingerprint = $fingerprint
}
$reusedPid = [pscustomobject]@{
    pid = 4100
    startTimeUtc = "2026-08-08T01:02:04.0000000Z"
    commandFingerprint = $fingerprint
}
Assert-True (Test-DevProcessIdentity -Expected $expectedProcess -Actual $sameProcess) "Exact process identity must validate."
Assert-False (Test-DevProcessIdentity -Expected $expectedProcess -Actual $reusedPid) "PID reuse must fail start-time validation."
$jsonRoundTrippedProcess = [pscustomobject]@{
    pid = 4100
    startTimeUtc = [DateTime]::Parse("2026-08-08T01:02:03.3486359Z").ToUniversalTime()
    commandFingerprint = $fingerprint
}
$fractionalProcess = [pscustomobject]@{
    pid = 4100
    startTimeUtc = "2026-08-08T01:02:03.3486359Z"
    commandFingerprint = $fingerprint
}
Assert-True (Test-DevProcessIdentity -Expected $jsonRoundTrippedProcess -Actual $fractionalProcess) "JSON DateTime conversion must preserve process identity."

$parentByProcessId = @{ 4300 = 4200; 4200 = 4100; 4100 = 4000 }
Assert-True (Test-DevListenerDescendsFromProcess -ListenerProcessId 4300 -RootProcessId 4100 -ParentByProcessId $parentByProcessId) "Listener descendants must be accepted."
Assert-False (Test-DevListenerDescendsFromProcess -ListenerProcessId 4300 -RootProcessId 9999 -ParentByProcessId $parentByProcessId) "Unrelated listeners must be rejected."

$expectedEndpoint = [pscustomobject]@{
    repositoryId = "repo-a"
    worktreeId = "worktree-a"
    runId = "run-a"
    endpointTokenFingerprint = "token-a"
}
$actualEndpoint = [pscustomobject]@{
    repositoryId = "repo-a"
    worktreeId = "worktree-a"
    runId = "run-a"
    endpointTokenFingerprint = "token-a"
}
$foreignEndpoint = [pscustomobject]@{
    repositoryId = "repo-a"
    worktreeId = "worktree-b"
    runId = "run-a"
    endpointTokenFingerprint = "token-a"
}
Assert-True (Test-DevEndpointIdentity -Expected $expectedEndpoint -Actual $actualEndpoint) "Matching endpoint identity must validate."
Assert-False (Test-DevEndpointIdentity -Expected $expectedEndpoint -Actual $foreignEndpoint) "Foreign worktree endpoints must be rejected."
$orderedEndpoint = [ordered]@{
    repositoryId = "repo-a"
    worktreeId = "worktree-a"
    runId = "run-a"
    endpointTokenFingerprint = "token-a"
}
Assert-True (Test-DevEndpointIdentity -Expected $orderedEndpoint -Actual $actualEndpoint) "Runtime ordered endpoint state must validate against JSON."

$mainPorts = Get-DevWorktreePorts -IsMainWorktree $true -WorktreeId "main"
Assert-Equal 5000 $mainPorts.HostPort "Main worktree Host port is fixed."
Assert-Equal 3000 $mainPorts.ShellPort "Main worktree Shell port is fixed."
$linkedPortsA = Get-DevWorktreePorts -IsMainWorktree $false -WorktreeId "linked-a"
$linkedPortsB = Get-DevWorktreePorts -IsMainWorktree $false -WorktreeId "linked-a"
Assert-Equal $linkedPortsA.HostPort $linkedPortsB.HostPort "Linked worktree Host port must be deterministic."
Assert-Equal $linkedPortsA.ShellPort $linkedPortsB.ShellPort "Linked worktree Shell port must be deterministic."
Assert-False ($linkedPortsA.HostPort -eq 5000 -or $linkedPortsA.ShellPort -eq 3000) "Linked worktree ports must not claim main-worktree ports."

Assert-ThrowsCode -Code "DEV_FAKE_CHILD_EXITED" -Action {
    Wait-DevChildProbe `
        -IsChildAlive { $false } `
        -Probe { $null } `
        -IsReady { param($value) $false } `
        -TimeoutMilliseconds 500 `
        -PollMilliseconds 10 `
        -ExitFailureCode "DEV_FAKE_CHILD_EXITED" `
        -TimeoutFailureCode "DEV_FAKE_TIMEOUT"
}
$timeoutStopwatch = [Diagnostics.Stopwatch]::StartNew()
Assert-ThrowsCode -Code "DEV_FAKE_TIMEOUT" -Action {
    Wait-DevChildProbe `
        -IsChildAlive { $true } `
        -Probe { $null } `
        -IsReady { param($value) $false } `
        -TimeoutMilliseconds 80 `
        -PollMilliseconds 10 `
        -ExitFailureCode "DEV_FAKE_CHILD_EXITED" `
        -TimeoutFailureCode "DEV_FAKE_TIMEOUT"
}
Assert-True ($timeoutStopwatch.ElapsedMilliseconds -lt 1000) "Fake timeout must fail without a production startup delay."

$fakeListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
try {
    $fakeListener.Start()
    $fakeListenerPort = ([Net.IPEndPoint]$fakeListener.LocalEndpoint).Port
    $listenerOwners = @(Get-DevListeningProcesses -Port $fakeListenerPort)
    Assert-True ($listenerOwners.Count -ge 1) "The fake listener must be discovered."
    Assert-True ($listenerOwners.ProcessId -contains $PID) "The fake listener must retain its real owner PID."
}
finally {
    $fakeListener.Stop()
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("newp-dev-supervisor-tests-" + [Guid]::NewGuid().ToString("N"))
$lockPath = Join-Path $tempRoot "launcher.lock"
$jsonPath = Join-Path $tempRoot "state.json"
$jsonlPath = Join-Path $tempRoot "stages.jsonl"

try {
    New-Item -ItemType Directory -Path $tempRoot | Out-Null
    $firstLock = Enter-DevLauncherLock -Path $lockPath -TimeoutMilliseconds 250
    try {
        Assert-ThrowsCode -Code "DEV_LAUNCHER_LOCK_BUSY" -Action {
            $secondLock = Enter-DevLauncherLock -Path $lockPath -TimeoutMilliseconds 100
            $secondLock.Dispose()
        }
    }
    finally {
        $firstLock.Dispose()
    }

    Write-DevJsonAtomic -Path $jsonPath -Value ([ordered]@{ status = "ready"; runId = "run-a" })
    $state = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
    Assert-Equal "ready" $state.status "Atomic state must be valid JSON."
    Write-DevJsonLine -Path $jsonlPath -Value ([ordered]@{ stage = "host-ready"; status = "passed" })
    $stage = Get-Content -LiteralPath $jsonlPath -Raw | ConvertFrom-Json
    Assert-Equal "host-ready" $stage.stage "Stage output must be JSONL."
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        $resolvedTempRoot = [IO.Path]::GetFullPath($tempRoot)
        $resolvedSystemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
        $tempItem = Get-Item -LiteralPath $resolvedTempRoot
        if (-not $resolvedTempRoot.StartsWith($resolvedSystemTemp, [StringComparison]::OrdinalIgnoreCase) -or
            -not $tempItem.Name.StartsWith("newp-dev-supervisor-tests-", [StringComparison]::Ordinal) -or
            [bool]($tempItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing to remove an unexpected integration-test directory: $resolvedTempRoot"
        }
        $reparsePoint = Get-ChildItem -LiteralPath $resolvedTempRoot -Force -Recurse |
            Where-Object { [bool]($_.Attributes -band [IO.FileAttributes]::ReparsePoint) } |
            Select-Object -First 1
        if ($null -ne $reparsePoint) {
            throw "Refusing to remove an integration-test directory containing a reparse point."
        }

        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

$sanitized = ConvertTo-DevSanitizedText "Server=db;User Id=operator;Password=REDACTED_FOR_OPEN_SOURCE;Data Source=D:\private\db"
Assert-False ($sanitized -match "operator|secret|D:\\private") "Diagnostics must redact credentials and private absolute paths."

$devScriptSource = Get-Content -LiteralPath (Join-Path $repoRoot "scripts\dev.ps1") -Raw
$stopScriptSource = Get-Content -LiteralPath (Join-Path $repoRoot "scripts\stop-dev.ps1") -Raw
$viteSource = Get-Content -LiteralPath (Join-Path $repoRoot "frontend\packages\shell\vite.config.ts") -Raw
Assert-True ($devScriptSource -match [regex]::Escape("scripts\lib\dev-supervisor.ps1")) "dev.ps1 must use the shared ownership library."
Assert-True ($stopScriptSource -match [regex]::Escape("scripts\lib\dev-supervisor.ps1")) "stop-dev.ps1 must use the shared ownership library."
Assert-True ($devScriptSource -notmatch "LocalAuth__") "dev.ps1 must not restore the removed LocalAuth configuration path."
Assert-True ($devScriptSource -notmatch "settings\.LocalAuth") "dev.ps1 must not read a plaintext password from appsettings."
Assert-True ($devScriptSource -match "antiforgery-token") "dev.ps1 authenticated smoke must follow the Host antiforgery protocol."
Assert-True ($viteSource -match "strictPort\s*:\s*true") "Vite must refuse fallback ports."
Assert-True ($viteSource -match "ICP_DEV_SHELL_PORT") "Vite must accept the supervisor-selected Shell port."
Assert-True ($viteSource -match "ICP_DEV_HOST_PORT") "Vite proxy must use the supervisor-selected Host port."

Write-Host "PASS: Windows development supervisor contracts."
