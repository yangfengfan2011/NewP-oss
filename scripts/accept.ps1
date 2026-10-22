[CmdletBinding()]
param(
    [string[]]$Scenario = @(),
    [switch]$Presentation,
    [switch]$NoBuild
)

# 验收场景就是 Acceptance.Tests 里的普通 xUnit 测试，默认跳过；
# 本脚本设置环境变量与过滤器，并从原生 TRX 确认所选测试实际通过。
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$testProject = Join-Path $repoRoot "tests\acceptance\Acceptance.Tests\Acceptance.Tests.csproj"

# 只接受精确方法名；逗号列表在 -File 与脚本内调用中具有相同含义。
$selectedScenarios = @($Scenario | ForEach-Object { $_.Split(',') })
$retiredGroups = @('All', 'Workspace', 'Personnel', 'IdentityFoundation', 'Authorization', 'ExampleRequiredField')
if ($selectedScenarios.Count -eq 0 -or @($selectedScenarios | Where-Object {
    $_ -cnotmatch '\A[_\p{L}\p{Nl}][_\p{L}\p{Nl}\p{Nd}\p{Pc}\p{Mn}\p{Mc}\p{Cf}]*\z' -or $_ -in $retiredGroups
}).Count -gt 0) {
    [Console]::Error.WriteLine('ACCEPT_SCENARIO_REQUIRED: specify exact AcceptanceGateTests method names with -Scenario; module groups and All are retired.')
    exit 2
}
$selectedScenarios = @($selectedScenarios | Select-Object -Unique)
$suite = 'ICP.Platform.Acceptance.Tests.AcceptanceGateTests'
$expectedTests = @($selectedScenarios | ForEach-Object { "$suite.$_" })
$filter = ($expectedTests | ForEach-Object { "FullyQualifiedName=$_" }) -join "|"
$resultsDirectory = Join-Path ([IO.Path]::GetTempPath()) ("newp-acceptance-" + [Guid]::NewGuid().ToString("N"))
$resultPath = Join-Path $resultsDirectory "acceptance.trx"

$arguments = @("test", $testProject, "--configuration", "Release")
if ($NoBuild.IsPresent) {
    $arguments += @("--no-build", "--no-restore")
}
$arguments += @("--filter", $filter, "--logger", "console;verbosity=detailed",
    "--logger", "trx;LogFileName=acceptance.trx", "--results-directory", $resultsDirectory)

$environmentNames = @('NEWP_RUN_ACCEPTANCE', 'NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE', 'NEWP_RUN_NATIVE_COMPONENT_ACCEPTANCE', 'NEWP_ACCEPTANCE_PRESENTATION')
$previousEnvironment = @{}
foreach ($name in $environmentNames) { $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    foreach ($name in $environmentNames[0..2]) { [Environment]::SetEnvironmentVariable($name, '1') }
    if ($Presentation) { $env:NEWP_ACCEPTANCE_PRESENTATION = '1' }
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
        if ($testResults.Count -ne $expectedTests.Count) { throw "The result contains missing, duplicate, or unselected tests." }
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
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name]) }
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
