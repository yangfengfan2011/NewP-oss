$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$logDir = Join-Path $repoRoot ".codex-temp"
$frontendUrl = "http://localhost:3000"
$backendUrl = "http://localhost:5000"
$frontendOutLog = Join-Path $logDir "dev-frontend.out.log"
$frontendErrLog = Join-Path $logDir "dev-frontend.err.log"
$backendOutLog = Join-Path $logDir "dev-host-api.out.log"
$backendErrLog = Join-Path $logDir "dev-host-api.err.log"
$frontendPidFile = Join-Path $logDir "dev-frontend.pid"
$backendPidFile = Join-Path $logDir "dev-host-api.pid"

Write-Host "=== Start ICP local dev environment ===" -ForegroundColor Green

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: dotnet was not found." -ForegroundColor Red
    exit 1
}

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: node was not found." -ForegroundColor Red
    exit 1
}

if (-not (Get-Command pnpm -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: pnpm was not found." -ForegroundColor Red
    exit 1
}

if (Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1) {
    Write-Host "ERROR: port 5000 is already in use." -ForegroundColor Red
    exit 1
}

if (Get-NetTCPConnection -LocalPort 3000 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1) {
    Write-Host "ERROR: port 3000 is already in use." -ForegroundColor Red
    exit 1
}

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

foreach ($path in @($frontendOutLog, $frontendErrLog, $backendOutLog, $backendErrLog, $frontendPidFile, $backendPidFile)) {
    if (Test-Path $path) {
        Remove-Item $path -Force
    }
}

Write-Host "Starting Host API..." -ForegroundColor Cyan
$previousAspNetCoreEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousDotnetEnvironment = $env:DOTNET_ENVIRONMENT

try {
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:DOTNET_ENVIRONMENT = "Development"

    $backendProcess = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList "run", "--project", (Join-Path $repoRoot "apps\host-api\HostApi.csproj") `
        -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $backendOutLog `
        -RedirectStandardError $backendErrLog `
        -PassThru
}
finally {
    if ($null -eq $previousAspNetCoreEnvironment) {
        Remove-Item Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
    } else {
        $env:ASPNETCORE_ENVIRONMENT = $previousAspNetCoreEnvironment
    }

    if ($null -eq $previousDotnetEnvironment) {
        Remove-Item Env:DOTNET_ENVIRONMENT -ErrorAction SilentlyContinue
    } else {
        $env:DOTNET_ENVIRONMENT = $previousDotnetEnvironment
    }
}

Set-Content -Path $backendPidFile -Value $backendProcess.Id

$backendReady = $false
$frontendReady = $false
$frontendProcess = $null
$backendDeadline = (Get-Date).AddSeconds(60)

while ((Get-Date) -lt $backendDeadline -and -not $backendReady) {
    try {
        Invoke-WebRequest -UseBasicParsing "$backendUrl/api/auth/current-user" -TimeoutSec 3 | Out-Null
        $backendReady = $true
    } catch {
        if ($_.Exception.Response) {
            $backendReady = $true
        }
    }

    if (-not $backendReady) {
        Start-Sleep -Seconds 1
    }
}

if ($backendReady) {
    Write-Host "Starting Frontend Shell..." -ForegroundColor Cyan
    $frontendProcess = Start-Process `
        -FilePath "pnpm.cmd" `
        -ArgumentList "-C", (Join-Path $repoRoot "frontend"), "--filter", "@icp-platform/shell", "dev" `
        -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $frontendOutLog `
        -RedirectStandardError $frontendErrLog `
        -PassThru

    Set-Content -Path $frontendPidFile -Value $frontendProcess.Id

    $frontendDeadline = (Get-Date).AddSeconds(60)

    while ((Get-Date) -lt $frontendDeadline -and -not $frontendReady) {
        try {
            Invoke-WebRequest -UseBasicParsing $frontendUrl -TimeoutSec 3 | Out-Null
            $frontendReady = $true
        } catch {
            Start-Sleep -Seconds 1
        }
    }
}

Write-Host ""
Write-Host "=== Startup result ===" -ForegroundColor Green
Write-Host "Host API PID: $($backendProcess.Id)" -ForegroundColor Yellow
Write-Host "Frontend PID: $(if ($frontendProcess) { $frontendProcess.Id } else { 'not started' })" -ForegroundColor Yellow
Write-Host "Host API logs: $backendOutLog / $backendErrLog"
Write-Host "Frontend logs: $frontendOutLog / $frontendErrLog"
Write-Host "PID files: $backendPidFile / $frontendPidFile"
Write-Host ""
Write-Host "Host API:  $backendUrl"
Write-Host "Frontend:  $frontendUrl"
Write-Host ""
Write-Host "Default login: admin / admin123" -ForegroundColor Cyan
Write-Host "Stop services: .\\scripts\\stop-dev.ps1" -ForegroundColor DarkGray

if (-not $backendReady -or -not $frontendReady) {
    Write-Host ""
    Write-Host "One or more services did not become ready in time. Check the logs." -ForegroundColor Yellow
    exit 1
}
