Set-StrictMode -Version Latest

function Get-DevSha256Text {
    param([Parameter(Mandatory)][string]$Value)

    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    $hash = [Security.Cryptography.SHA256]::HashData($bytes)
    return [Convert]::ToHexString($hash).ToLowerInvariant()
}

function Get-DevCommandFingerprint {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Arguments
    )

    $normalizedExecutable = $FilePath.Trim().Replace('/', '\').ToLowerInvariant()
    $normalizedArguments = @($Arguments | ForEach-Object { ([string]$_).Trim() })
    return Get-DevSha256Text -Value ($normalizedExecutable + "`0" + ($normalizedArguments -join "`0"))
}

function Test-DevProcessIdentity {
    param(
        [Parameter(Mandatory)]$Expected,
        [Parameter(Mandatory)]$Actual
    )

    if ([int]$Expected.pid -ne [int]$Actual.pid) {
        return $false
    }

    try {
        $expectedStart = if ($Expected.startTimeUtc -is [DateTime]) {
            [DateTimeOffset]::new(([DateTime]$Expected.startTimeUtc).ToUniversalTime())
        }
        elseif ($Expected.startTimeUtc -is [DateTimeOffset]) {
            ([DateTimeOffset]$Expected.startTimeUtc).ToUniversalTime()
        }
        else {
            [DateTimeOffset]::Parse(
                [string]$Expected.startTimeUtc,
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::AssumeUniversal).ToUniversalTime()
        }
        $actualStart = if ($Actual.startTimeUtc -is [DateTime]) {
            [DateTimeOffset]::new(([DateTime]$Actual.startTimeUtc).ToUniversalTime())
        }
        elseif ($Actual.startTimeUtc -is [DateTimeOffset]) {
            ([DateTimeOffset]$Actual.startTimeUtc).ToUniversalTime()
        }
        else {
            [DateTimeOffset]::Parse(
                [string]$Actual.startTimeUtc,
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::AssumeUniversal).ToUniversalTime()
        }
    }
    catch {
        return $false
    }

    return $expectedStart.UtcTicks -eq $actualStart.UtcTicks -and
        [string]::Equals(
            [string]$Expected.commandFingerprint,
            [string]$Actual.commandFingerprint,
            [StringComparison]::OrdinalIgnoreCase)
}

function Test-DevListenerDescendsFromProcess {
    param(
        [Parameter(Mandatory)][int]$ListenerProcessId,
        [Parameter(Mandatory)][int]$RootProcessId,
        [Parameter(Mandatory)][hashtable]$ParentByProcessId
    )

    $current = $ListenerProcessId
    $visited = [Collections.Generic.HashSet[int]]::new()
    for ($depth = 0; $depth -lt 128; $depth++) {
        if ($current -eq $RootProcessId) {
            return $true
        }

        if (-not $visited.Add($current) -or -not $ParentByProcessId.ContainsKey($current)) {
            return $false
        }

        $current = [int]$ParentByProcessId[$current]
        if ($current -le 0) {
            return $false
        }
    }

    return $false
}

function Test-DevEndpointIdentity {
    param(
        [Parameter(Mandatory)]$Expected,
        [Parameter(Mandatory)]$Actual
    )

    foreach ($propertyName in @("repositoryId", "worktreeId", "runId", "endpointTokenFingerprint")) {
        $expectedProperty = $Expected.PSObject.Properties[$propertyName]
        $actualProperty = $Actual.PSObject.Properties[$propertyName]
        $expectedExists = $null -ne $expectedProperty
        $actualExists = $null -ne $actualProperty
        $expectedValue = if ($expectedExists) { $expectedProperty.Value } else { $null }
        $actualValue = if ($actualExists) { $actualProperty.Value } else { $null }

        if ($Expected -is [Collections.IDictionary] -and $Expected.Contains($propertyName)) {
            $expectedExists = $true
            $expectedValue = $Expected[$propertyName]
        }
        if ($Actual -is [Collections.IDictionary] -and $Actual.Contains($propertyName)) {
            $actualExists = $true
            $actualValue = $Actual[$propertyName]
        }

        if (-not $expectedExists -or -not $actualExists -or
            -not [string]::Equals([string]$expectedValue, [string]$actualValue, [StringComparison]::Ordinal)) {
            return $false
        }
    }

    return $true
}

function Get-DevWorktreePorts {
    param(
        [Parameter(Mandatory)][bool]$IsMainWorktree,
        [Parameter(Mandatory)][string]$WorktreeId,
        [int]$HostPort,
        [int]$ShellPort
    )

    if (($HostPort -gt 0) -xor ($ShellPort -gt 0)) {
        throw "DEV_PORT_OVERRIDE_INCOMPLETE: HostPort and ShellPort must be supplied together."
    }

    if ($HostPort -gt 0) {
        if ($HostPort -gt 65535 -or $ShellPort -gt 65535 -or $HostPort -eq $ShellPort) {
            throw "DEV_PORT_OVERRIDE_INVALID: Ports must be distinct values in the TCP port range."
        }

        return [pscustomobject]@{ HostPort = $HostPort; ShellPort = $ShellPort }
    }

    if ($IsMainWorktree) {
        return [pscustomobject]@{ HostPort = 5000; ShellPort = 3000 }
    }

    $hashBytes = [Convert]::FromHexString((Get-DevSha256Text -Value $WorktreeId))
    $offset = [BitConverter]::ToUInt32($hashBytes, 0) % 700
    return [pscustomobject]@{
        HostPort = 5100 + [int]$offset
        ShellPort = 3100 + [int]$offset
    }
}

function Enter-DevLauncherLock {
    param(
        [Parameter(Mandatory)][string]$Path,
        [int]$TimeoutMilliseconds = 5000
    )

    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $deadline = [DateTime]::UtcNow.AddMilliseconds([Math]::Max(0, $TimeoutMilliseconds))
    do {
        try {
            return [IO.File]::Open($Path, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        }
        catch [IO.IOException] {
            if ([DateTime]::UtcNow -ge $deadline) {
                throw "DEV_LAUNCHER_LOCK_BUSY: another start or stop operation owns the worktree launcher lock."
            }

            Start-Sleep -Milliseconds 25
        }
    } while ($true)
}

function Write-DevJsonAtomic {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]$Value
    )

    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $temporaryPath = Join-Path $directory ((Split-Path -Leaf $Path) + ".tmp." + [Guid]::NewGuid().ToString("N"))
    try {
        $json = $Value | ConvertTo-Json -Depth 16
        [IO.File]::WriteAllText($temporaryPath, $json + "`n", [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporaryPath, $Path, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Write-DevJsonLine {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]$Value
    )

    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $json = $Value | ConvertTo-Json -Depth 12 -Compress
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
    try {
        $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
        try {
            $writer.WriteLine($json)
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function ConvertTo-DevSanitizedText {
    param([AllowNull()][string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    $sanitized = $Value
    $sanitized = [regex]::Replace(
        $sanitized,
        '(?i)(password|pwd|user\s*id|uid)\s*=\s*[^;,\s]+',
        '$1=<redacted>')
    $sanitized = [regex]::Replace(
        $sanitized,
        '(?i)\b[A-Z]:\\[^;\r\n]*',
        '<absolute-path>')
    return $sanitized
}

function Wait-DevChildProbe {
    param(
        [Parameter(Mandatory)][scriptblock]$IsChildAlive,
        [Parameter(Mandatory)][scriptblock]$Probe,
        [Parameter(Mandatory)][scriptblock]$IsReady,
        [Parameter(Mandatory)][ValidateRange(1, 600000)][int]$TimeoutMilliseconds,
        [ValidateRange(1, 60000)][int]$PollMilliseconds = 250,
        [Parameter(Mandatory)][string]$ExitFailureCode,
        [Parameter(Mandatory)][string]$TimeoutFailureCode
    )

    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
    do {
        if (-not [bool](& $IsChildAlive)) {
            throw "$ExitFailureCode`: child process exited before readiness completed."
        }

        $probeValue = & $Probe
        if ([bool](& $IsReady $probeValue)) {
            return $probeValue
        }

        Start-Sleep -Milliseconds $PollMilliseconds
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "$TimeoutFailureCode`: readiness did not complete within $TimeoutMilliseconds milliseconds."
}

function Get-DevGitValue {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    $output = @(& git -C $RepositoryRoot @Arguments 2>$null)
    if ($LASTEXITCODE -ne 0 -or $output.Count -eq 0) {
        throw "DEV_GIT_IDENTITY_FAILED: unable to resolve the worktree identity."
    }

    return $output[-1].Trim()
}

function Get-DevWorktreeIdentity {
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $commonDirectoryValue = Get-DevGitValue -RepositoryRoot $root -Arguments @("rev-parse", "--git-common-dir")
    $gitDirectoryValue = Get-DevGitValue -RepositoryRoot $root -Arguments @("rev-parse", "--git-dir")
    $commonDirectory = [IO.Path]::GetFullPath((Join-Path $root $commonDirectoryValue))
    $gitDirectory = [IO.Path]::GetFullPath((Join-Path $root $gitDirectoryValue))
    $normalCommon = $commonDirectory.TrimEnd('\', '/').ToLowerInvariant()
    $normalGit = $gitDirectory.TrimEnd('\', '/').ToLowerInvariant()

    return [pscustomobject]@{
        RepositoryId = (Get-DevSha256Text -Value $normalCommon).Substring(0, 16)
        WorktreeId = (Get-DevSha256Text -Value $normalGit).Substring(0, 16)
        IsMainWorktree = [string]::Equals($normalCommon, $normalGit, [StringComparison]::OrdinalIgnoreCase)
        Commit = Get-DevGitValue -RepositoryRoot $root -Arguments @("rev-parse", "HEAD")
    }
}

function Get-DevProcessSnapshot {
    param([Parameter(Mandatory)][int]$ProcessId)

    $cimProcess = Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessId" -ErrorAction SilentlyContinue
    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $cimProcess -or $null -eq $process) {
        return $null
    }

    try {
        $startTimeUtc = $process.StartTime.ToUniversalTime().ToString("O", [Globalization.CultureInfo]::InvariantCulture)
    }
    catch {
        return $null
    }

    $executablePath = if ([string]::IsNullOrWhiteSpace([string]$cimProcess.ExecutablePath)) {
        [string]$process.Path
    }
    else {
        [string]$cimProcess.ExecutablePath
    }
    $commandLine = if ($null -eq $cimProcess.CommandLine) { "" } else { [string]$cimProcess.CommandLine }

    return [pscustomobject]@{
        pid = $ProcessId
        parentPid = [int]$cimProcess.ParentProcessId
        startTimeUtc = $startTimeUtc
        commandFingerprint = Get-DevSha256Text -Value (($executablePath.Trim().ToLowerInvariant()) + "`0" + $commandLine)
        processName = [string]$process.ProcessName
    }
}

function Get-DevParentProcessMap {
    $map = @{}
    foreach ($process in @(Get-CimInstance Win32_Process -ErrorAction Stop)) {
        $map[[int]$process.ProcessId] = [int]$process.ParentProcessId
    }

    return $map
}

function Get-DevListeningProcesses {
    param([Parameter(Mandatory)][int]$Port)

    $connections = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
    return @($connections | ForEach-Object {
        $process = Get-Process -Id ([int]$_.OwningProcess) -ErrorAction SilentlyContinue
        [pscustomobject]@{
            Port = $Port
            ProcessId = [int]$_.OwningProcess
            ProcessName = if ($null -eq $process) { "unknown" } else { [string]$process.ProcessName }
        }
    } | Sort-Object ProcessId -Unique)
}

function Test-DevRootOwnsPort {
    param(
        [Parameter(Mandatory)]$ExpectedRoot,
        [Parameter(Mandatory)][int]$Port
    )

    $actualRoot = Get-DevProcessSnapshot -ProcessId ([int]$ExpectedRoot.pid)
    if ($null -eq $actualRoot -or -not (Test-DevProcessIdentity -Expected $ExpectedRoot -Actual $actualRoot)) {
        return $false
    }

    $listeners = @(Get-DevListeningProcesses -Port $Port)
    if ($listeners.Count -ne 1) {
        return $false
    }

    $parentMap = Get-DevParentProcessMap
    return Test-DevListenerDescendsFromProcess `
        -ListenerProcessId $listeners[0].ProcessId `
        -RootProcessId ([int]$ExpectedRoot.pid) `
        -ParentByProcessId $parentMap
}

function Get-DevProcessTreeIds {
    param([Parameter(Mandatory)][int]$RootProcessId)

    $childrenByParent = @{}
    foreach ($process in @(Get-CimInstance Win32_Process -ErrorAction Stop)) {
        $parentId = [int]$process.ParentProcessId
        if (-not $childrenByParent.ContainsKey($parentId)) {
            $childrenByParent[$parentId] = [Collections.Generic.List[int]]::new()
        }

        $childrenByParent[$parentId].Add([int]$process.ProcessId)
    }

    $result = [Collections.Generic.List[int]]::new()
    $queue = [Collections.Generic.Queue[int]]::new()
    $queue.Enqueue($RootProcessId)
    while ($queue.Count -gt 0) {
        $current = $queue.Dequeue()
        $result.Add($current)
        if ($childrenByParent.ContainsKey($current)) {
            foreach ($childId in $childrenByParent[$current]) {
                $queue.Enqueue($childId)
            }
        }
    }

    return @($result)
}

function Stop-DevOwnedProcessTree {
    param(
        [Parameter(Mandatory)]$ExpectedRoot,
        [int]$WaitMilliseconds = 5000
    )

    $actualRoot = Get-DevProcessSnapshot -ProcessId ([int]$ExpectedRoot.pid)
    if ($null -eq $actualRoot) {
        return $false
    }

    if (-not (Test-DevProcessIdentity -Expected $ExpectedRoot -Actual $actualRoot)) {
        throw "DEV_PROCESS_IDENTITY_MISMATCH: refusing to stop PID $($ExpectedRoot.pid)."
    }

    $treeIds = @(Get-DevProcessTreeIds -RootProcessId ([int]$ExpectedRoot.pid))
    $treeSnapshots = @{}
    foreach ($processId in $treeIds) {
        $snapshot = Get-DevProcessSnapshot -ProcessId $processId
        if ($null -ne $snapshot) {
            $treeSnapshots[$processId] = $snapshot
        }
    }

    [array]::Reverse($treeIds)
    foreach ($processId in $treeIds) {
        $currentSnapshot = Get-DevProcessSnapshot -ProcessId $processId
        if ($null -eq $currentSnapshot) {
            continue
        }

        if (-not $treeSnapshots.ContainsKey($processId) -or
            -not (Test-DevProcessIdentity -Expected $treeSnapshots[$processId] -Actual $currentSnapshot)) {
            throw "DEV_PROCESS_IDENTITY_MISMATCH: refusing to stop reused process PID $processId."
        }

        try {
            Stop-Process -Id $processId -ErrorAction Stop
        }
        catch {
            if (Get-Process -Id $processId -ErrorAction SilentlyContinue) {
                throw
            }
        }
    }

    $deadline = [DateTime]::UtcNow.AddMilliseconds($WaitMilliseconds)
    while ([DateTime]::UtcNow -lt $deadline -and (Get-Process -Id ([int]$ExpectedRoot.pid) -ErrorAction SilentlyContinue)) {
        Start-Sleep -Milliseconds 50
    }

    $remaining = Get-Process -Id ([int]$ExpectedRoot.pid) -ErrorAction SilentlyContinue
    if ($null -ne $remaining) {
        $remainingSnapshot = Get-DevProcessSnapshot -ProcessId ([int]$ExpectedRoot.pid)
        if ($null -eq $remainingSnapshot -or
            -not (Test-DevProcessIdentity -Expected $ExpectedRoot -Actual $remainingSnapshot)) {
            throw "DEV_PROCESS_IDENTITY_MISMATCH: refusing to force-stop reused root PID $($ExpectedRoot.pid)."
        }

        Stop-Process -Id ([int]$ExpectedRoot.pid) -Force -ErrorAction Stop
    }

    return $true
}

function Wait-DevPortsReleased {
    param(
        [Parameter(Mandatory)][int[]]$Ports,
        [int]$TimeoutSeconds = 15
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $occupied = @($Ports | Where-Object { @(Get-DevListeningProcesses -Port $_).Count -gt 0 })
        if ($occupied.Count -eq 0) {
            return $true
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    return $false
}

function Get-DevReadinessResponse {
    param(
        [Parameter(Mandatory)][int]$Port,
        [int]$TimeoutSeconds = 3
    )

    try {
        $response = Invoke-WebRequest `
            -Uri "http://127.0.0.1:$Port/api/health/ready" `
            -UseBasicParsing `
            -SkipHttpErrorCheck `
            -TimeoutSec $TimeoutSeconds
        if ([string]::IsNullOrWhiteSpace($response.Content)) {
            return $null
        }

        return $response.Content | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Test-DevStateOwnership {
    param([Parameter(Mandatory)]$State)

    if ($null -eq $State.processes -or $null -eq $State.ports -or $null -eq $State.endpointIdentity) {
        return $false
    }

    if (-not (Test-DevRootOwnsPort -ExpectedRoot $State.processes.host -Port ([int]$State.ports.host))) {
        return $false
    }

    if (-not (Test-DevRootOwnsPort -ExpectedRoot $State.processes.shell -Port ([int]$State.ports.shell))) {
        return $false
    }

    $readiness = Get-DevReadinessResponse -Port ([int]$State.ports.host)
    if ($null -eq $readiness -or $null -eq $readiness.supervisor) {
        return $false
    }

    return Test-DevEndpointIdentity -Expected $State.endpointIdentity -Actual $readiness.supervisor
}
