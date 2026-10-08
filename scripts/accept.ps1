[CmdletBinding()]
param(
    [ValidateSet("IdentityFoundation", "Personnel", "ExampleRequiredField", "Authorization", "Workspace", "All")]
    [string]$Gate,
    [switch]$NoBuild
)

# 验收闸门就是 Acceptance.Tests 里的普通 xUnit 测试，默认跳过；
# 本脚本设置环境变量与过滤器，并从原生 TRX 确认所选测试实际通过。
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$testProject = Join-Path $repoRoot "tests\acceptance\Acceptance.Tests\Acceptance.Tests.csproj"

if ([string]::IsNullOrWhiteSpace($Gate)) {
    [Console]::Error.WriteLine("ACCEPT_GATE_REQUIRED: specify -Gate IdentityFoundation, Personnel, ExampleRequiredField, Authorization, Workspace, or All.")
    exit 2
}

$suite = "ICP.Platform.Acceptance.Tests.AcceptanceGateTests"
$selectedGates = if ($Gate -eq "All") {
    @("IdentityFoundation", "Personnel", "ExampleRequiredField", "Authorization", "Workspace")
} else {
    @($Gate)
}
$expectedTests = @($selectedGates | ForEach-Object { "$suite.$($_)_CompletesEveryScenario" })
$filter = ($expectedTests | ForEach-Object { "FullyQualifiedName=$_" }) -join "|"
$resultsDirectory = Join-Path ([IO.Path]::GetTempPath()) ("newp-acceptance-" + [Guid]::NewGuid().ToString("N"))
$resultPath = Join-Path $resultsDirectory "acceptance.trx"

$arguments = @("test", $testProject, "--configuration", "Release")
if ($NoBuild.IsPresent) {
    $arguments += @("--no-build", "--no-restore")
}
$arguments += @("--filter", $filter, "--logger", "console;verbosity=detailed",
    "--logger", "trx;LogFileName=acceptance.trx", "--results-directory", $resultsDirectory)

$previousAcceptance = $env:NEWP_RUN_ACCEPTANCE
$env:NEWP_RUN_ACCEPTANCE = "1"
try {
    dotnet @arguments
    $testExitCode = $LASTEXITCODE
    if ($testExitCode -ne 0) {
        exit $testExitCode
    }

    try {
        [xml]$result = Get-Content -LiteralPath $resultPath -Raw
        $namespaces = [Xml.XmlNamespaceManager]::new($result.NameTable)
        $namespaces.AddNamespace("t", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
        $testResults = @($result.SelectNodes("/t:TestRun/t:Results/t:UnitTestResult", $namespaces))
        foreach ($expectedTest in $expectedTests) {
            $matches = @($testResults | Where-Object { $_.testName -ceq $expectedTest })
            if ($matches.Count -ne 1 -or $matches[0].outcome -cne "Passed") {
                throw "Required test '$expectedTest' did not execute and pass exactly once."
            }
        }
    }
    catch {
        [Console]::Error.WriteLine("Acceptance result validation failed: " + $_.Exception.Message)
        exit 1
    }
    exit 0
}
finally {
    $env:NEWP_RUN_ACCEPTANCE = $previousAcceptance
    # TRX 只用于本次执行确认；不长期保存可能包含场景输出的副本。
    Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $resultsDirectory) {
        try {
            [IO.Directory]::Delete($resultsDirectory)
        }
        catch {
            Write-Warning "Could not remove temporary test results directory '$resultsDirectory': $($_.Exception.Message)"
        }
    }
}
