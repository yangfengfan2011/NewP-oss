param(
    [ValidateRange(0, 65535)]
    [int]$HostPort = 0,

    [ValidateRange(0, 65535)]
    [int]$ShellPort = 0,

    [ValidateRange(5, 600)]
    [int]$HostStartupTimeoutSeconds = 120,

    [ValidateRange(5, 600)]
    [int]$ShellStartupTimeoutSeconds = 90,

    [ValidateRange(5, 120)]
    [int]$ShellRequestTimeoutSeconds = 10,

    [ValidateSet("Development", "Acceptance")]
    [string]$HostEnvironmentName = "Development",

    [switch]$SkipDependencyInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$supervisorLibrary = Join-Path $repoRoot "scripts\lib\dev-supervisor.ps1"
. $supervisorLibrary

$frontendRoot = Join-Path $repoRoot "frontend"
$frontendPackagePath = Join-Path $frontendRoot "package.json"
$hostProjectPath = Join-Path $repoRoot "apps\host-api\HostApi.csproj"
$devRoot = Join-Path $repoRoot ".codex-temp\dev"
$statePath = Join-Path $devRoot "state.json"
$currentResultPath = Join-Path $devRoot "current-result.json"
$lockPath = Join-Path $devRoot "launcher.lock"
$launcherLock = $null
$hostProcess = $null
$shellProcess = $null
$hostRoot = $null
$shellRoot = $null
$runDirectory = $null
$stagesPath = $null
$resultPath = $null
$identity = $null
$ports = $null
$logPaths = $null
$endpointIdentity = $null
$startedAt = [DateTimeOffset]::UtcNow
$stopwatch = [Diagnostics.Stopwatch]::StartNew()

function Write-DevStage {
    param(
        [Parameter(Mandatory)][string]$Stage,
        [Parameter(Mandatory)][string]$Status,
        [AllowNull()][string]$FailureCode = $null,
        [AllowNull()][string]$Diagnostic = $null
    )

    if ([string]::IsNullOrWhiteSpace($script:stagesPath)) {
        return
    }

    Write-DevJsonLine -Path $script:stagesPath -Value ([ordered]@{
        timestampUtc = [DateTimeOffset]::UtcNow.ToString("O")
        elapsedMs = $script:stopwatch.ElapsedMilliseconds
        stage = $Stage
        status = $Status
        failureCode = $FailureCode
        diagnostic = ConvertTo-DevSanitizedText $Diagnostic
    })
}

function Get-StartedProcessSnapshot {
    param([Parameter(Mandatory)][Diagnostics.Process]$Process)

    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $snapshot = Get-DevProcessSnapshot -ProcessId $Process.Id
        if ($null -ne $snapshot) {
            return $snapshot
        }

        if ($Process.HasExited) {
            throw "DEV_CHILD_EXITED: process $($Process.Id) exited before its identity could be captured."
        }

        Start-Sleep -Milliseconds 50
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "DEV_PROCESS_IDENTITY_UNAVAILABLE: process $($Process.Id) could not be identified."
}

function Invoke-WithProcessEnvironment {
    param(
        [Parameter(Mandatory)][hashtable]$Variables,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    $previous = @{}
    try {
        foreach ($entry in $Variables.GetEnumerator()) {
            $previous[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, [EnvironmentVariableTarget]::Process)
            [Environment]::SetEnvironmentVariable($entry.Key, [string]$entry.Value, [EnvironmentVariableTarget]::Process)
        }

        return & $Action
    }
    finally {
        foreach ($entry in $Variables.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable($entry.Key, $previous[$entry.Key], [EnvironmentVariableTarget]::Process)
        }
    }
}

function Test-ChildExited {
    param([AllowNull()][Diagnostics.Process]$Process)

    return $null -eq $Process -or $Process.HasExited
}

function Wait-HostReady {
    param(
        [Parameter(Mandatory)][Diagnostics.Process]$Process,
        [Parameter(Mandatory)][int]$Port,
        [Parameter(Mandatory)]$ExpectedEndpoint,
        [Parameter(Mandatory)][int]$TimeoutSeconds
    )

    return Wait-DevChildProbe `
        -IsChildAlive { -not (Test-ChildExited $Process) } `
        -Probe { Get-DevReadinessResponse -Port $Port } `
        -IsReady {
            param($readiness)
            if ($null -eq $readiness) {
                return $false
            }

            if ($null -eq $readiness.supervisor -or -not (Test-DevEndpointIdentity -Expected $ExpectedEndpoint -Actual $readiness.supervisor)) {
                throw "DEV_HOST_IDENTITY_MISMATCH: the Host readiness endpoint does not belong to this run."
            }

            if ([string]::Equals([string]$readiness.status, "ready", [StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }

            if ([string]::Equals([string]$readiness.status, "failed", [StringComparison]::OrdinalIgnoreCase)) {
                $failureCode = if ([string]::IsNullOrWhiteSpace([string]$readiness.failureCode)) { "unknown" } else { [string]$readiness.failureCode }
                throw "DEV_HOST_READINESS_FAILED: Host reported failure code $failureCode."
            }

            return $false
        } `
        -TimeoutMilliseconds ($TimeoutSeconds * 1000) `
        -PollMilliseconds 250 `
        -ExitFailureCode "DEV_HOST_EXITED" `
        -TimeoutFailureCode "DEV_HOST_READY_TIMEOUT"
}

function Wait-ShellHttpReady {
    param(
        [Parameter(Mandatory)][Diagnostics.Process]$Process,
        [Parameter(Mandatory)][int]$Port,
        [Parameter(Mandatory)][int]$TimeoutSeconds
    )

    $null = Wait-DevChildProbe `
        -IsChildAlive { -not (Test-ChildExited $Process) } `
        -Probe {
            try {
                return Invoke-WebRequest -Uri "http://127.0.0.1:$Port/" -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 3
            }
            catch {
                return $null
            }
        } `
        -IsReady {
            param($response)
            return $null -ne $response -and
                $response.StatusCode -eq 200 -and
                [string]$response.Headers["Content-Type"] -match "text/html"
        } `
        -TimeoutMilliseconds ($TimeoutSeconds * 1000) `
        -PollMilliseconds 250 `
        -ExitFailureCode "DEV_SHELL_EXITED" `
        -TimeoutFailureCode "DEV_SHELL_READY_TIMEOUT"
}

function Invoke-ShellAcceptance {
    param(
        [Parameter(Mandatory)][int]$Port,
        [Parameter(Mandatory)]$ExpectedEndpoint,
        [AllowNull()]$Credentials,
        [Parameter(Mandatory)][int]$RequestTimeoutSeconds
    )

    $baseUri = "http://127.0.0.1:$Port"
    $rootResponse = Invoke-WebRequest -Uri "$baseUri/" -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 5
    if ($rootResponse.StatusCode -ne 200 -or [string]$rootResponse.Headers["Content-Type"] -notmatch "text/html") {
        throw "DEV_SHELL_HTML_INVALID: Shell origin did not serve HTML."
    }

    foreach ($manifestPath in @(
        "/plugins/frontend-plugin-manifest.json",
        "/plugins/localization-manifest.json",
        "/plugins/metadata-runtime-manifest.json")) {
        $manifestResponse = Invoke-WebRequest -Uri "$baseUri$manifestPath" -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 5
        if ($manifestResponse.StatusCode -ne 200) {
            throw "DEV_SHELL_MANIFEST_FAILED: Shell origin returned $($manifestResponse.StatusCode) for $manifestPath."
        }

        try {
            $null = $manifestResponse.Content | ConvertFrom-Json
        }
        catch {
            throw "DEV_SHELL_MANIFEST_INVALID: Shell origin returned invalid JSON for $manifestPath."
        }
    }

    $proxyResponse = Invoke-WebRequest -Uri "$baseUri/api/health/ready" -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 5
    if ($proxyResponse.StatusCode -ne 200) {
        throw "DEV_SHELL_PROXY_FAILED: proxied Host readiness returned $($proxyResponse.StatusCode)."
    }

    $proxyReadiness = $proxyResponse.Content | ConvertFrom-Json
    if ($null -eq $proxyReadiness.supervisor -or -not (Test-DevEndpointIdentity -Expected $ExpectedEndpoint -Actual $proxyReadiness.supervisor)) {
        throw "DEV_SHELL_PROXY_IDENTITY_MISMATCH: proxied readiness belongs to another run."
    }

    if ($null -eq $Credentials) {
        return
    }

    $webSession = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
    $tokenResponse = Invoke-WebRequest `
        -Uri "$baseUri/api/auth/antiforgery-token" `
        -WebSession $webSession `
        -UseBasicParsing `
        -SkipHttpErrorCheck `
        -TimeoutSec $RequestTimeoutSeconds
    if ($tokenResponse.StatusCode -ne 200) {
        throw "DEV_ANTIFORGERY_TOKEN_FAILED: token endpoint returned $($tokenResponse.StatusCode)."
    }

    $tokenPayload = $tokenResponse.Content | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace([string]$tokenPayload.requestToken)) {
        throw "DEV_ANTIFORGERY_TOKEN_INVALID: token endpoint returned an empty request token."
    }

    $loginBody = @{ username = $Credentials.Username; password = REDACTED_FOR_OPEN_SOURCE } | ConvertTo-Json -Compress
    $loginResponse = Invoke-WebRequest `
        -Uri "$baseUri/api/auth/login" `
        -Method Post `
        -Headers @{ "X-CSRF-TOKEN" = [string]$tokenPayload.requestToken } `
        -ContentType "application/json" `
        -Body $loginBody `
        -WebSession $webSession `
        -UseBasicParsing `
        -SkipHttpErrorCheck `
        -TimeoutSec $RequestTimeoutSeconds
    if ($loginResponse.StatusCode -ne 200) {
        throw "DEV_LOGIN_FAILED: Identity-backed login returned $($loginResponse.StatusCode)."
    }

    $currentUserResponse = Invoke-WebRequest `
        -Uri "$baseUri/api/auth/current-user" `
        -WebSession $webSession `
        -UseBasicParsing `
        -SkipHttpErrorCheck `
        -TimeoutSec $RequestTimeoutSeconds
    if ($currentUserResponse.StatusCode -ne 200) {
        throw "DEV_CURRENT_USER_FAILED: authenticated current-user returned $($currentUserResponse.StatusCode)."
    }

    $currentUser = $currentUserResponse.Content | ConvertFrom-Json
    if (-not [string]::Equals([string]$currentUser.username, [string]$Credentials.Username, [StringComparison]::Ordinal)) {
        throw "DEV_CURRENT_USER_MISMATCH: current-user did not match the Identity-backed login."
    }
}

function Get-DevelopmentSmokeCredentials {
    $username = [string]$env:NEWP_DEV_ADMIN_USERNAME
    $password = [string]$env:NEWP_DEV_ADMIN_PASSWORD
    $hasUsername = -not [string]::IsNullOrWhiteSpace($username)
    $hasPassword = -not [string]::IsNullOrWhiteSpace($password)
    if (-not $hasUsername -and -not $hasPassword) {
        return $null
    }

    if (-not $hasUsername -or -not $hasPassword) {
        throw "DEV_AUTH_SMOKE_CREDENTIALS_INCOMPLETE: set both NEWP_DEV_ADMIN_USERNAME and NEWP_DEV_ADMIN_PASSWORD or neither."
    }

    return [pscustomobject]@{ Username = $username; Password = REDACTED_FOR_OPEN_SOURCE }
}

function Assert-Toolchain {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "DEV_DOTNET_MISSING: dotnet was not found."
    }

    if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
        throw "DEV_NODE_MISSING: node was not found."
    }

    $packageConfig = Get-Content -LiteralPath $frontendPackagePath -Raw | ConvertFrom-Json
    if ([string]$packageConfig.packageManager -notmatch '^pnpm@(?<Version>\d+\.\d+\.\d+)$') {
        throw "DEV_PNPM_VERSION_UNPINNED: frontend/package.json must declare an exact pnpm version."
    }

    $requiredPnpmVersion = $Matches.Version
    $runtime = $packageConfig.devEngines.runtime
    if ([string]$runtime.name -ne "node" -or [string]$runtime.version -notmatch '^\d+\.\d+\.\d+$') {
        throw "DEV_NODE_VERSION_UNPINNED: frontend/package.json must declare an exact Node.js version."
    }

    $currentNodeVersion = (& node --version).Trim().TrimStart('v')
    if ($LASTEXITCODE -ne 0 -or $currentNodeVersion -ne [string]$runtime.version) {
        throw "DEV_NODE_VERSION_MISMATCH: Node.js $($runtime.version) is required; current version is $currentNodeVersion."
    }

    $corepackCommand = Get-Command corepack.cmd -ErrorAction SilentlyContinue
    if ($null -eq $corepackCommand) {
        throw "DEV_COREPACK_MISSING: Corepack is required to resolve the pinned pnpm version."
    }

    Push-Location $frontendRoot
    try {
        $pnpmVersionOutput = @(& $corepackCommand.Source pnpm --version)
        $pnpmExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    $currentPnpmVersion = if ($pnpmVersionOutput.Count -eq 0) { "" } else { [string]$pnpmVersionOutput[-1] }
    if ($pnpmExitCode -ne 0 -or $currentPnpmVersion.Trim() -ne $requiredPnpmVersion) {
        throw "DEV_PNPM_VERSION_MISMATCH: Corepack could not provide pnpm $requiredPnpmVersion."
    }

    return [pscustomobject]@{
        CorepackPath = $corepackCommand.Source
        NodeVersion = [string]$runtime.version
        PnpmVersion = $requiredPnpmVersion
    }
}

function Ensure-FrontendDependencies {
    param(
        [Parameter(Mandatory)][string]$CorepackPath,
        [Parameter(Mandatory)][bool]$SkipInstall
    )

    $nodeModulesPath = Join-Path $frontendRoot "node_modules"
    $needsInstall = -not (Test-Path -LiteralPath $nodeModulesPath)
    if (-not $needsInstall) {
        Push-Location $frontendRoot
        try {
            & $CorepackPath pnpm --filter "@icp-platform/shell" exec vite --version *> $null
            $needsInstall = $LASTEXITCODE -ne 0
        }
        finally {
            Pop-Location
        }
    }

    if (-not $needsInstall) {
        return
    }

    if ($SkipInstall) {
        throw "DEV_FRONTEND_DEPENDENCIES_MISSING: frontend dependencies are unavailable and install was disabled."
    }

    Push-Location $frontendRoot
    try {
        & $CorepackPath pnpm install
        if ($LASTEXITCODE -ne 0) {
            throw "DEV_FRONTEND_INSTALL_FAILED: pnpm install returned exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function Write-RunResult {
    param(
        [Parameter(Mandatory)][string]$Status,
        [Parameter(Mandatory)][bool]$Reused,
        [AllowNull()][string]$FailureCode = $null,
        [AllowNull()][string]$Diagnostic = $null,
        [AllowNull()]$EndpointIdentity = $null,
        [AllowNull()]$Ports = $null,
        [AllowNull()]$Processes = $null,
        [AllowNull()]$LogPaths = $null
    )

    $hostPid = if ($null -eq $Processes -or $null -eq $Processes.host) { $null } else { $Processes.host.pid }
    $shellPid = if ($null -eq $Processes -or $null -eq $Processes.shell) { $null } else { $Processes.shell.pid }
    $result = [ordered]@{
        status = $Status
        reused = $Reused
        failureCode = $FailureCode
        diagnostic = ConvertTo-DevSanitizedText $Diagnostic
        startedAtUtc = $script:startedAt.ToString("O")
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        durationMs = $script:stopwatch.ElapsedMilliseconds
        endpointIdentity = $EndpointIdentity
        ports = $Ports
        processIds = if ($null -eq $Processes) { $null } else { [ordered]@{ host = $hostPid; shell = $shellPid } }
        logs = $LogPaths
    }
    Write-DevJsonAtomic -Path $script:resultPath -Value $result
    Write-DevJsonAtomic -Path $script:currentResultPath -Value $result
}

function Stop-StartedProcesses {
    if ($null -ne $script:shellRoot) {
        Stop-DevOwnedProcessTree -ExpectedRoot $script:shellRoot | Out-Null
    }

    if ($null -ne $script:hostRoot) {
        Stop-DevOwnedProcessTree -ExpectedRoot $script:hostRoot | Out-Null
    }
}

try {
    $identity = Get-DevWorktreeIdentity -RepositoryRoot $repoRoot
    $ports = Get-DevWorktreePorts `
        -IsMainWorktree $identity.IsMainWorktree `
        -WorktreeId $identity.WorktreeId `
        -HostPort $HostPort `
        -ShellPort $ShellPort
    $credentials = Get-DevelopmentSmokeCredentials

    $launcherLock = Enter-DevLauncherLock -Path $lockPath -TimeoutMilliseconds 5000

    $existingState = $null
    if (Test-Path -LiteralPath $statePath) {
        try {
            $existingState = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        }
        catch {
            throw "DEV_STATE_INVALID: state.json is not valid JSON."
        }
    }

    if ($null -ne $existingState -and
        [string]$existingState.status -eq "ready" -and
        [string]$existingState.repositoryId -eq $identity.RepositoryId -and
        [string]$existingState.worktreeId -eq $identity.WorktreeId -and
        [string]$existingState.commit -eq $identity.Commit -and
        [int]$existingState.ports.host -eq $ports.HostPort -and
        [int]$existingState.ports.shell -eq $ports.ShellPort -and
        (Test-DevStateOwnership -State $existingState)) {
        Invoke-ShellAcceptance `
            -Port $ports.ShellPort `
            -ExpectedEndpoint $existingState.endpointIdentity `
            -Credentials $credentials `
            -RequestTimeoutSeconds $ShellRequestTimeoutSeconds

        $reuseRunId = "reuse-" + [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
        $runDirectory = Join-Path $devRoot "runs\$reuseRunId"
        New-Item -ItemType Directory -Path $runDirectory | Out-Null
        $stagesPath = Join-Path $runDirectory "stages.jsonl"
        $resultPath = Join-Path $runDirectory "result.json"
        Write-DevStage -Stage "reuse" -Status "passed"
        Write-RunResult `
            -Status "ready" `
            -Reused $true `
            -EndpointIdentity $existingState.endpointIdentity `
            -Ports $existingState.ports `
            -Processes $existingState.processes `
            -LogPaths $existingState.logs
        Write-Host "DEV_READY_REUSED: Host and Shell are already ready for this worktree."
        Write-Host "Host: http://127.0.0.1:$($ports.HostPort)"
        Write-Host "Shell: http://127.0.0.1:$($ports.ShellPort)"
        exit 0
    }

    foreach ($port in @($ports.HostPort, $ports.ShellPort)) {
        $listeners = @(Get-DevListeningProcesses -Port $port)
        if ($listeners.Count -gt 0) {
            $summary = @($listeners | ForEach-Object { "port $($_.Port) pid $($_.ProcessId) owner $($_.ProcessName)" }) -join "; "
            throw "DEV_PORT_IN_USE: $summary. No process was stopped."
        }
    }

    $runId = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
    $endpointTokenFingerprint = Get-DevSha256Text -Value ([Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)))
    $endpointIdentity = [ordered]@{
        repositoryId = $identity.RepositoryId
        worktreeId = $identity.WorktreeId
        runId = $runId
        endpointTokenFingerprint = $endpointTokenFingerprint
    }
    $runDirectory = Join-Path $devRoot "runs\$runId"
    New-Item -ItemType Directory -Path $runDirectory | Out-Null
    $stagesPath = Join-Path $runDirectory "stages.jsonl"
    $resultPath = Join-Path $runDirectory "result.json"
    $hostOutLog = Join-Path $runDirectory "host.stdout.log"
    $hostErrLog = Join-Path $runDirectory "host.stderr.log"
    $shellOutLog = Join-Path $runDirectory "shell.stdout.log"
    $shellErrLog = Join-Path $runDirectory "shell.stderr.log"
    $logPaths = [ordered]@{
        hostStdout = $hostOutLog
        hostStderr = $hostErrLog
        shellStdout = $shellOutLog
        shellStderr = $shellErrLog
    }

    Write-DevStage -Stage "prerequisites" -Status "started"
    $toolchain = Assert-Toolchain
    Ensure-FrontendDependencies -CorepackPath $toolchain.CorepackPath -SkipInstall $SkipDependencyInstall.IsPresent
    Write-DevStage -Stage "prerequisites" -Status "passed"

    Write-DevStage -Stage "host-start" -Status "started"
    $hostEnvironment = @{
        "ASPNETCORE_ENVIRONMENT" = $HostEnvironmentName
        "DOTNET_ENVIRONMENT" = $HostEnvironmentName
        "ASPNETCORE_URLS" = "http://127.0.0.1:$($ports.HostPort)"
        "DevSupervisor__RepositoryId" = $identity.RepositoryId
        "DevSupervisor__WorktreeId" = $identity.WorktreeId
        "DevSupervisor__RunId" = $runId
        "DevSupervisor__EndpointTokenFingerprint" = $endpointTokenFingerprint
    }
    if (-not [string]::IsNullOrWhiteSpace($env:NEWP_DEV_SQLSERVER_CONNECTION_STRING)) {
        $hostEnvironment["ConnectionStrings__SqlServer"] = $env:NEWP_DEV_SQLSERVER_CONNECTION_STRING
        $hostEnvironment["Persistence__Adapter"] = "EfCore"
        $hostEnvironment["Persistence__Provider"] = "SqlServer"
        $hostEnvironment["Persistence__AutoMigrate"] = "true"
        $hostEnvironment["Persistence__AutoSeed"] = "true"
    }

    $hostProcess = Invoke-WithProcessEnvironment -Variables $hostEnvironment -Action {
        Start-Process `
            -FilePath "dotnet" `
            -ArgumentList @("run", "--project", $hostProjectPath, "--no-launch-profile") `
            -WorkingDirectory $repoRoot `
            -RedirectStandardOutput $hostOutLog `
            -RedirectStandardError $hostErrLog `
            -WindowStyle Hidden `
            -PassThru
    }
    $hostRoot = Get-StartedProcessSnapshot -Process $hostProcess
    Write-DevStage -Stage "host-start" -Status "passed"

    $startingState = [ordered]@{
        schemaVersion = 1
        status = "starting"
        repositoryId = $identity.RepositoryId
        worktreeId = $identity.WorktreeId
        commit = $identity.Commit
        runId = $runId
        startedAtUtc = $startedAt.ToString("O")
        ports = [ordered]@{ host = $ports.HostPort; shell = $ports.ShellPort }
        endpointIdentity = $endpointIdentity
        processes = [ordered]@{ host = $hostRoot; shell = $null }
        logs = $logPaths
        runDirectory = $runDirectory
    }
    Write-DevJsonAtomic -Path $statePath -Value $startingState

    Write-DevStage -Stage "host-ready" -Status "started"
    $null = Wait-HostReady `
        -Process $hostProcess `
        -Port $ports.HostPort `
        -ExpectedEndpoint $endpointIdentity `
        -TimeoutSeconds $HostStartupTimeoutSeconds
    Write-DevStage -Stage "host-ready" -Status "passed"

    Write-DevStage -Stage "shell-start" -Status "started"
    $shellEnvironment = @{
        "ICP_DEV_HOST_PORT" = [string]$ports.HostPort
        "ICP_DEV_SHELL_PORT" = [string]$ports.ShellPort
    }
    $shellProcess = Invoke-WithProcessEnvironment -Variables $shellEnvironment -Action {
        Start-Process `
            -FilePath $toolchain.CorepackPath `
            -ArgumentList @("pnpm", "--filter", "@icp-platform/shell", "dev") `
            -WorkingDirectory $frontendRoot `
            -RedirectStandardOutput $shellOutLog `
            -RedirectStandardError $shellErrLog `
            -WindowStyle Hidden `
            -PassThru
    }
    $shellRoot = Get-StartedProcessSnapshot -Process $shellProcess
    Write-DevStage -Stage "shell-start" -Status "passed"
    Write-DevStage -Stage "shell-ready" -Status "started"
    Wait-ShellHttpReady -Process $shellProcess -Port $ports.ShellPort -TimeoutSeconds $ShellStartupTimeoutSeconds
    Invoke-ShellAcceptance `
        -Port $ports.ShellPort `
        -ExpectedEndpoint $endpointIdentity `
        -Credentials $credentials `
        -RequestTimeoutSeconds $ShellRequestTimeoutSeconds
    Write-DevStage -Stage "shell-ready" -Status "passed"

    if (-not (Test-DevRootOwnsPort -ExpectedRoot $hostRoot -Port $ports.HostPort) -or
        -not (Test-DevRootOwnsPort -ExpectedRoot $shellRoot -Port $ports.ShellPort)) {
        throw "DEV_LISTENER_OWNERSHIP_FAILED: a ready listener is outside its launched process tree."
    }

    $processes = [ordered]@{ host = $hostRoot; shell = $shellRoot }
    $readyState = [ordered]@{
        schemaVersion = 1
        status = "ready"
        repositoryId = $identity.RepositoryId
        worktreeId = $identity.WorktreeId
        commit = $identity.Commit
        runId = $runId
        startedAtUtc = $startedAt.ToString("O")
        readyAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        ports = [ordered]@{ host = $ports.HostPort; shell = $ports.ShellPort }
        endpointIdentity = $endpointIdentity
        processes = $processes
        logs = $logPaths
        runDirectory = $runDirectory
    }
    Write-DevJsonAtomic -Path (Join-Path $runDirectory "ownership.json") -Value $readyState
    Write-DevJsonAtomic -Path $statePath -Value $readyState
    Write-RunResult `
        -Status "ready" `
        -Reused $false `
        -EndpointIdentity $endpointIdentity `
        -Ports $readyState.ports `
        -Processes $processes `
        -LogPaths $logPaths

    Write-Host "DEV_READY: Host and Shell passed the complete readiness contract."
    Write-Host "Host: http://127.0.0.1:$($ports.HostPort)"
    Write-Host "Shell: http://127.0.0.1:$($ports.ShellPort)"
    Write-Host "Run diagnostics: $runDirectory"
    exit 0
}
catch {
    $message = ConvertTo-DevSanitizedText $_.Exception.Message
    $failureCode = if ($message -match '^(DEV_[A-Z0-9_]+):') { $Matches[1] } else { "DEV_START_FAILED" }
    Write-DevStage -Stage "startup" -Status "failed" -FailureCode $failureCode -Diagnostic $message

    try {
        Stop-StartedProcesses
        if ($null -ne $identity -and $null -ne $ports) {
            $null = Wait-DevPortsReleased -Ports @($ports.HostPort, $ports.ShellPort)
        }
    }
    catch {
        $rollbackMessage = ConvertTo-DevSanitizedText $_.Exception.Message
        Write-DevStage -Stage "rollback" -Status "failed" -FailureCode "DEV_ROLLBACK_FAILED" -Diagnostic $rollbackMessage
        $message = "$message Rollback: $rollbackMessage"
        $failureCode = "DEV_ROLLBACK_FAILED"
    }

    if (-not [string]::IsNullOrWhiteSpace($resultPath)) {
        $failureProcesses = if ($null -eq $hostRoot -and $null -eq $shellRoot) { $null } else {
            [ordered]@{ host = $hostRoot; shell = $shellRoot }
        }
        $failureLogs = if ([string]::IsNullOrWhiteSpace($runDirectory)) { $null } else { $logPaths }
        $failurePorts = if ($null -eq $ports) { $null } else { [ordered]@{ host = $ports.HostPort; shell = $ports.ShellPort } }
        Write-RunResult `
            -Status "failed" `
            -Reused $false `
            -FailureCode $failureCode `
            -Diagnostic $message `
            -Ports $failurePorts `
            -Processes $failureProcesses `
            -LogPaths $failureLogs

        $failedState = [ordered]@{
            schemaVersion = 1
            status = "failed"
            failureCode = $failureCode
            diagnostic = $message
            repositoryId = if ($null -eq $identity) { $null } else { $identity.RepositoryId }
            worktreeId = if ($null -eq $identity) { $null } else { $identity.WorktreeId }
            commit = if ($null -eq $identity) { $null } else { $identity.Commit }
            runId = if ($null -eq $endpointIdentity) { $null } else { $endpointIdentity.runId }
            startedAtUtc = $startedAt.ToString("O")
            failedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
            ports = $failurePorts
            endpointIdentity = $endpointIdentity
            processes = $failureProcesses
            logs = $failureLogs
            runDirectory = $runDirectory
        }
        Write-DevJsonAtomic -Path $statePath -Value $failedState
    }

    Write-Host $message -ForegroundColor Red
    if (-not [string]::IsNullOrWhiteSpace($runDirectory)) {
        Write-Host "Run diagnostics: $runDirectory"
    }
    exit 1
}
finally {
    if ($null -ne $launcherLock) {
        $launcherLock.Dispose()
    }
}
