Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$hostLog = Join-Path $repoRoot ".codex-temp\dev-host-api.out.log"
$hostErrLog = Join-Path $repoRoot ".codex-temp\dev-host-api.err.log"

try {
    & (Join-Path $repoRoot "scripts\stop-dev.ps1") | Out-Null

    & (Join-Path $repoRoot "scripts\dev.ps1") | Out-Null

    if (-not (Test-Path $hostLog)) {
        throw "Host API log was not created."
    }

    $stdout = Get-Content $hostLog -Raw -ErrorAction SilentlyContinue
    $stderr = Get-Content $hostErrLog -Raw -ErrorAction SilentlyContinue
    $combinedLogs = "$stdout`n$stderr"

    if ($combinedLogs -notmatch "Hosting environment: Development") {
        throw "dev.ps1 did not start Host API in Development."
    }

    if ($combinedLogs -match "Failed to determine the https port for redirect") {
        throw "dev.ps1 still logs the HTTPS redirection warning."
    }

    Write-Host "PASS: dev.ps1 starts Host API in Development without the HTTPS redirection warning."
}
finally {
    & (Join-Path $repoRoot "scripts\stop-dev.ps1") | Out-Null
}
