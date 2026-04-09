param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("SqlServer", "Oracle", "MySql")]
    [string]$Provider
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$hostProject = Join-Path $repoRoot "apps\host-api\HostApi.csproj"
$configTests = Join-Path $repoRoot "tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj"
$authorizationTests = Join-Path $repoRoot "tests\Authorization.Tests\Authorization.Tests.csproj"

switch ($Provider) {
    "SqlServer" {
        $migrationProject = Join-Path $repoRoot "server\platform-persistence-efcore.migrations.sqlserver\PlatformPersistence.EfCore.SqlServerMigrations.csproj"
        $connectionStringEnvName = "ConnectionStrings__SqlServer"
        $connectionString = "Server=localhost,1433;Database=ICP_Platform;User Id=sa;Password=REDACTED_FOR_OPEN_SOURCE;TrustServerCertificate=True;"
        $hostUrl = "http://127.0.0.1:5181"
    }
    "Oracle" {
        $migrationProject = Join-Path $repoRoot "server\platform-persistence-efcore.migrations.oracle\PlatformPersistence.EfCore.OracleMigrations.csproj"
        $connectionStringEnvName = "ConnectionStrings__Oracle"
        $connectionString = "User Id=system;Password=REDACTED_FOR_OPEN_SOURCE;Data Source=localhost:1521/FREEPDB1;"
        $hostUrl = "http://127.0.0.1:5183"
    }
    "MySql" {
        $migrationProject = Join-Path $repoRoot "server\platform-persistence-efcore.migrations.mysql\PlatformPersistence.EfCore.MySqlMigrations.csproj"
        $connectionStringEnvName = "ConnectionStrings__MySql"
        $connectionString = "Server=localhost;Port=3306;Database=icp_platform;User=icp;Password=REDACTED_FOR_OPEN_SOURCE;"
        $hostUrl = "http://127.0.0.1:5182"
    }
    default {
        throw "Unsupported provider: $Provider"
    }
}

$env:Persistence__Adapter = "EfCore"
$env:Persistence__Provider = $Provider
$env:Persistence__AutoMigrate = "false"
Set-Item -Path "Env:$connectionStringEnvName" -Value $connectionString

Push-Location $repoRoot
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed for $Provider" }

    dotnet build $hostProject -v minimal
    if ($LASTEXITCODE -ne 0) { throw "HostApi build failed for $Provider" }

    dotnet test $configTests -v minimal
    if ($LASTEXITCODE -ne 0) { throw "ConfigCenter tests failed for $Provider" }

    dotnet test $authorizationTests -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Authorization tests failed for $Provider" }

    dotnet tool run dotnet-ef database update `
        --project $migrationProject `
        --startup-project $migrationProject `
        --context "ICP.Platform.Persistence.EfCore.EfPlatformDbContext"
    if ($LASTEXITCODE -ne 0) { throw "dotnet-ef database update failed for $Provider" }

    $env:Persistence__AutoMigrate = "true"
    $env:Platform__SkipLocalAuthSeeding = "true"
    $env:ASPNETCORE_URLS = $hostUrl

    $stdout = Join-Path $repoRoot ".codex-temp\host-$($Provider.ToLowerInvariant())-smoke.out.log"
    $stderr = Join-Path $repoRoot ".codex-temp\host-$($Provider.ToLowerInvariant())-smoke.err.log"
    $process = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList @("run", "--project", $hostProject, "--no-build") `
        -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr `
        -PassThru

    try {
        $deadline = (Get-Date).AddSeconds(30)
        $healthy = $false

        while ((Get-Date) -lt $deadline) {
            if ($process.HasExited) {
                throw "HostApi exited early for $Provider. See $stdout and $stderr."
            }

            try {
                $response = Invoke-WebRequest -Uri "$hostUrl/swagger/v1/swagger.json" -UseBasicParsing -TimeoutSec 3
                if ($response.StatusCode -eq 200) {
                    $healthy = $true
                    break
                }
            }
            catch {
                Start-Sleep -Seconds 1
            }
        }

        if (-not $healthy) {
            throw "HostApi did not become healthy for $Provider within timeout. See $stdout and $stderr."
        }
    }
    finally {
        if ($process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit()
        }
    }
}
finally {
    Pop-Location
}
