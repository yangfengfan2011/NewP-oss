Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$projectPath = Join-Path $repoRoot "apps\host-api\HostApi.csproj"

if (-not (Test-Path $projectPath)) {
    throw "Could not find Host API project at $projectPath"
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("icp-hostapi-warning-test-" + [Guid]::NewGuid().ToString("N"))
$stdoutLog = Join-Path $tempRoot "hostapi.out.log"
$stderrLog = Join-Path $tempRoot "hostapi.err.log"
$port = 5099
$process = $null

function Stop-TestProcess {
    param([System.Diagnostics.Process]$Process)

    if ($null -eq $Process) {
        return
    }

    try {
        if (-not $Process.HasExited) {
            Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
        }
    } catch {
    }
}

try {
    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

    $originalEnvironment = @{
        ASPNETCORE_ENVIRONMENT = $env:ASPNETCORE_ENVIRONMENT
        ASPNETCORE_URLS = $env:ASPNETCORE_URLS
    }

    try {
        $env:ASPNETCORE_ENVIRONMENT = "Development"
        $env:ASPNETCORE_URLS = "http://localhost:$port"

        $process = Start-Process `
            -FilePath "dotnet" `
            -ArgumentList "run", "--project", $projectPath `
            -WorkingDirectory $repoRoot `
            -RedirectStandardOutput $stdoutLog `
            -RedirectStandardError $stderrLog `
            -PassThru

        $deadline = (Get-Date).AddSeconds(60)
        $started = $false

        while ((Get-Date) -lt $deadline -and -not $started) {
            if ($process.HasExited) {
                throw "Host API exited before startup completed.`nSTDOUT:`n$(Get-Content $stdoutLog -Raw -ErrorAction SilentlyContinue)`nSTDERR:`n$(Get-Content $stderrLog -Raw -ErrorAction SilentlyContinue)"
            }

            if (Test-Path $stdoutLog) {
                $stdout = Get-Content $stdoutLog -Raw
                if ($stdout -match "Now listening on: http://localhost:$port") {
                    $started = $true
                    break
                }
            }

            Start-Sleep -Seconds 1
        }

        if (-not $started) {
            throw "Host API did not start listening on http://localhost:$port within 60 seconds."
        }

        try {
            Invoke-WebRequest -UseBasicParsing "http://localhost:$port/api/auth/current-user" -TimeoutSec 5 | Out-Null
        } catch {
            if (-not $_.Exception.Response) {
                throw
            }
        }

        Start-Sleep -Seconds 2

        $stdout = Get-Content $stdoutLog -Raw -ErrorAction SilentlyContinue
        $stderr = Get-Content $stderrLog -Raw -ErrorAction SilentlyContinue
        $combinedLogs = "$stdout`n$stderr"

        if ($combinedLogs -match "Failed to determine the https port for redirect") {
            throw "Development startup still logs the HTTPS redirection warning."
        }

        Write-Host "PASS: Development startup does not log the HTTPS redirection warning."
    }
    finally {
        if ($null -eq $originalEnvironment.ASPNETCORE_ENVIRONMENT) {
            Remove-Item Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
        } else {
            $env:ASPNETCORE_ENVIRONMENT = $originalEnvironment.ASPNETCORE_ENVIRONMENT
        }

        if ($null -eq $originalEnvironment.ASPNETCORE_URLS) {
            Remove-Item Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue
        } else {
            $env:ASPNETCORE_URLS = $originalEnvironment.ASPNETCORE_URLS
        }
    }
}
finally {
    Stop-TestProcess $process

    if (Test-Path $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
