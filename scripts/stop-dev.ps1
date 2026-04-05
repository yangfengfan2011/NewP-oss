$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$logDir = Join-Path $repoRoot ".codex-temp"
$pidFiles = @(
    (Join-Path $logDir "dev-host-api.pid"),
    (Join-Path $logDir "dev-frontend.pid")
)

$stopped = New-Object System.Collections.Generic.List[string]

function Stop-TrackedProcess {
    param([string]$PidFile)

    if (-not (Test-Path $PidFile)) {
        return
    }

    $content = (Get-Content $PidFile -Raw).Trim()
    if (-not $content) {
        Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
        return
    }

    $procId = 0
    if (-not [int]::TryParse($content, [ref]$procId)) {
        Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
        return
    }

    $process = Get-Process -Id $procId -ErrorAction SilentlyContinue
    if ($process) {
        Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
        $stopped.Add("pid:$procId") | Out-Null
    }

    Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
}

foreach ($pidFile in $pidFiles) {
    Stop-TrackedProcess -PidFile $pidFile
}

foreach ($port in @(5000, 3000)) {
    $connections = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    foreach ($connection in $connections) {
        $procId = $connection.OwningProcess
        if ($procId) {
            Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
            $stopped.Add("port:$port pid:$procId") | Out-Null
        }
    }
}

if ($stopped.Count -eq 0) {
    Write-Host "No tracked dev processes were running." -ForegroundColor Yellow
    exit 0
}

Write-Host "Stopped dev processes:" -ForegroundColor Green
$stopped | Select-Object -Unique | ForEach-Object { Write-Host "  $_" }
