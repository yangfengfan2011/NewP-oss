Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$supervisorLibrary = Join-Path $repoRoot "scripts\lib\dev-supervisor.ps1"
. $supervisorLibrary

$devRoot = Join-Path $repoRoot ".codex-temp\dev"
$statePath = Join-Path $devRoot "state.json"
$currentResultPath = Join-Path $devRoot "current-result.json"
$lockPath = Join-Path $devRoot "launcher.lock"
$launcherLock = $null

try {
    $identity = Get-DevWorktreeIdentity -RepositoryRoot $repoRoot
    $launcherLock = Enter-DevLauncherLock -Path $lockPath -TimeoutMilliseconds 5000

    if (-not (Test-Path -LiteralPath $statePath)) {
        Write-Host "DEV_ALREADY_STOPPED: no supervisor state exists for this worktree."
        exit 0
    }

    try {
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    }
    catch {
        throw "DEV_STATE_INVALID: state.json is not valid JSON; no process was stopped."
    }

    if ([string]$state.repositoryId -ne $identity.RepositoryId -or [string]$state.worktreeId -ne $identity.WorktreeId) {
        throw "DEV_STATE_IDENTITY_MISMATCH: state.json belongs to another repository or worktree; no process was stopped."
    }

    $roots = @(
        [pscustomobject]@{ Name = "host"; Expected = $state.processes.host; Port = [int]$state.ports.host },
        [pscustomobject]@{ Name = "shell"; Expected = $state.processes.shell; Port = [int]$state.ports.shell }
    )
    $liveRoots = [Collections.Generic.List[object]]::new()
    foreach ($root in $roots) {
        if ($null -eq $root.Expected) {
            continue
        }

        $actual = Get-DevProcessSnapshot -ProcessId ([int]$root.Expected.pid)
        if ($null -eq $actual) {
            continue
        }

        if (-not (Test-DevProcessIdentity -Expected $root.Expected -Actual $actual)) {
            throw "DEV_PROCESS_IDENTITY_MISMATCH: $($root.Name) PID was reused; no process was stopped."
        }

        $listeners = @(Get-DevListeningProcesses -Port $root.Port)
        if ($listeners.Count -gt 0 -and -not (Test-DevRootOwnsPort -ExpectedRoot $root.Expected -Port $root.Port)) {
            throw "DEV_LISTENER_OWNERSHIP_FAILED: port $($root.Port) is not exclusively owned by the recorded $($root.Name) process tree; no process was stopped."
        }

        $liveRoots.Add($root)
    }

    foreach ($root in $roots) {
        $listeners = @(Get-DevListeningProcesses -Port $root.Port)
        if ($listeners.Count -gt 0 -and -not ($liveRoots | Where-Object Name -eq $root.Name)) {
            throw "DEV_PORT_IN_USE: port $($root.Port) has an unverified listener; no process was stopped."
        }
    }

    $hostIsListening = @(Get-DevListeningProcesses -Port ([int]$state.ports.host)).Count -gt 0
    if ($hostIsListening) {
        $readiness = Get-DevReadinessResponse -Port ([int]$state.ports.host)
        if ($null -eq $readiness -or $null -eq $readiness.supervisor -or
            -not (Test-DevEndpointIdentity -Expected $state.endpointIdentity -Actual $readiness.supervisor)) {
            throw "DEV_ENDPOINT_IDENTITY_MISMATCH: Host endpoint token did not validate; no process was stopped."
        }
    }

    foreach ($rootName in @("shell", "host")) {
        $root = $liveRoots | Where-Object Name -eq $rootName | Select-Object -First 1
        if ($null -ne $root) {
            Stop-DevOwnedProcessTree -ExpectedRoot $root.Expected | Out-Null
        }
    }

    if (-not (Wait-DevPortsReleased -Ports @([int]$state.ports.host, [int]$state.ports.shell) -TimeoutSeconds 15)) {
        throw "DEV_PORT_RELEASE_TIMEOUT: verified process trees stopped but a development port remains occupied."
    }

    $stoppedAt = [DateTimeOffset]::UtcNow
    $stoppedState = [ordered]@{}
    foreach ($property in $state.PSObject.Properties) {
        $stoppedState[$property.Name] = $property.Value
    }
    $stoppedState["status"] = "stopped"
    $stoppedState["stoppedAtUtc"] = $stoppedAt.ToString("O")
    Write-DevJsonAtomic -Path $statePath -Value $stoppedState

    $hostPid = if ($null -eq $state.processes.host) { $null } else { $state.processes.host.pid }
    $shellPid = if ($null -eq $state.processes.shell) { $null } else { $state.processes.shell.pid }
    $result = [ordered]@{
        status = if ($liveRoots.Count -eq 0) { "already-stopped" } else { "stopped" }
        failureCode = $null
        completedAtUtc = $stoppedAt.ToString("O")
        durationMs = 0
        ports = $state.ports
        processIds = [ordered]@{ host = $hostPid; shell = $shellPid }
    }
    $stopRunId = "stop-" + $stoppedAt.ToString("yyyyMMddTHHmmssfff") + "-" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
    $stopRunDirectory = Join-Path $devRoot "runs\$stopRunId"
    New-Item -ItemType Directory -Path $stopRunDirectory | Out-Null
    Write-DevJsonAtomic -Path (Join-Path $stopRunDirectory "result.json") -Value $result
    Write-DevJsonAtomic -Path $currentResultPath -Value $result

    if ($liveRoots.Count -eq 0) {
        Write-Host "DEV_ALREADY_STOPPED: recorded process trees are not running."
    }
    else {
        Write-Host "DEV_STOPPED: verified Host and Shell process trees are stopped."
    }
    exit 0
}
catch {
    $message = ConvertTo-DevSanitizedText $_.Exception.Message
    $failureCode = if ($message -match '^(DEV_[A-Z0-9_]+):') { $Matches[1] } else { "DEV_STOP_FAILED" }
    Write-Host $message -ForegroundColor Red
    exit 1
}
finally {
    if ($null -ne $launcherLock) {
        $launcherLock.Dispose()
    }
}
