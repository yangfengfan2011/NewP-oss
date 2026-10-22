Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$sourceDevScript = Join-Path $repoRoot "scripts\dev.ps1"

if (-not (Test-Path $sourceDevScript)) {
    throw "Could not find source dev script at $sourceDevScript"
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("icp-dev-startup-test-" + [Guid]::NewGuid().ToString("N"))
$fakeBinDir = Join-Path $tempRoot "fake-bin"
$stateDir = Join-Path $tempRoot "state"
$sandboxScriptsDir = Join-Path $tempRoot "scripts"
$sandboxLogDir = Join-Path $tempRoot ".codex-temp"
$backendReadyFile = Join-Path $stateDir "backend-ready.txt"
$frontendStartedFile = Join-Path $stateDir "frontend-started.txt"
$sandboxDevScript = Join-Path $sandboxScriptsDir "dev.ps1"

function New-TestDirectory {
    param([string]$Path)

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

function Stop-TrackedProcess {
    param([string]$PidFile)

    if (-not (Test-Path $PidFile)) {
        return
    }

    $pidText = Get-Content $PidFile | Select-Object -First 1
    if ($pidText -notmatch "^\d+$") {
        return
    }

    Stop-Process -Id ([int]$pidText) -Force -ErrorAction SilentlyContinue
}

try {
    New-TestDirectory $fakeBinDir
    New-TestDirectory $stateDir
    New-TestDirectory $sandboxScriptsDir
    New-TestDirectory $sandboxLogDir
    New-TestDirectory (Join-Path $tempRoot "apps\host-api")
    New-TestDirectory (Join-Path $tempRoot "frontend")

    Copy-Item -Path $sourceDevScript -Destination $sandboxDevScript -Force
    Set-Content -Path (Join-Path $tempRoot "apps\host-api\HostApi.csproj") -Value '<Project Sdk="Microsoft.NET.Sdk.Web" />'
    Set-Content -Path (Join-Path $tempRoot "frontend\package.json") -Value '{ "name": "startup-test" }'

    $serveScript = @'
param(
    [int]$Port,
    [int]$DelayMs = 0,
    [int]$StatusCode = 200,
    [string]$Body = "",
    [string]$ReadyFile = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Start-Sleep -Milliseconds $DelayMs

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()

if ($ReadyFile) {
    Set-Content -Path $ReadyFile -Value (Get-Date -Format o)
}

$lastActivity = Get-Date

try {
    while (((Get-Date) - $lastActivity).TotalSeconds -lt 15) {
        $pendingContext = $listener.GetContextAsync()
        if (-not $pendingContext.Wait(1000)) {
            continue
        }

        $context = $pendingContext.Result
        $lastActivity = Get-Date
        $context.Response.StatusCode = $StatusCode

        if ($Body) {
            $buffer = [System.Text.Encoding]::UTF8.GetBytes($Body)
            $context.Response.OutputStream.Write($buffer, 0, $buffer.Length)
        }

        $context.Response.OutputStream.Close()
        $context.Response.Close()
    }
}
finally {
    $listener.Stop()
    $listener.Close()
}
'@

    Set-Content -Path (Join-Path $fakeBinDir "serve.ps1") -Value $serveScript

    Set-Content -Path (Join-Path $fakeBinDir "node.cmd") -Value "@echo off`r`nexit /b 0`r`n"

    $dotnetCmd = @"
@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0serve.ps1" -Port 5000 -DelayMs 5000 -StatusCode 401 -ReadyFile "$($backendReadyFile.Replace('"', '""'))"
"@

    $pnpmCmd = @"
@echo off
powershell -NoProfile -ExecutionPolicy Bypass -Command "Set-Content -Path '$($frontendStartedFile.Replace("'", "''"))' -Value (Get-Date -Format o)" >nul 2>nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0serve.ps1" -Port 3000 -DelayMs 0 -StatusCode 200 -Body "frontend-ready"
"@

    Set-Content -Path (Join-Path $fakeBinDir "dotnet.cmd") -Value $dotnetCmd
    Set-Content -Path (Join-Path $fakeBinDir "pnpm.cmd") -Value $pnpmCmd

    $originalPath = $env:PATH

    try {
        $env:PATH = "$fakeBinDir;$originalPath"
        & $sandboxDevScript | Out-Null
    }
    finally {
        $env:PATH = $originalPath
    }

    if (-not (Test-Path $frontendStartedFile)) {
        throw "Frontend start marker was not created."
    }

    if (-not (Test-Path $backendReadyFile)) {
        throw "Backend ready marker was not created."
    }

    $frontendStartedAt = [DateTimeOffset]::Parse((Get-Content $frontendStartedFile | Select-Object -First 1))
    $backendReadyAt = [DateTimeOffset]::Parse((Get-Content $backendReadyFile | Select-Object -First 1))

    if ($frontendStartedAt -lt $backendReadyAt) {
        throw "Frontend started at $frontendStartedAt before backend became ready at $backendReadyAt."
    }

    Write-Host "PASS: frontend start waited for backend readiness."
}
finally {
    Stop-TrackedProcess (Join-Path $sandboxLogDir "dev-host-api.pid")
    Stop-TrackedProcess (Join-Path $sandboxLogDir "dev-frontend.pid")

    if (Test-Path $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
