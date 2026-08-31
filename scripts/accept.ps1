[CmdletBinding()]
param(
    [ValidateSet("IdentityFoundation", "Personnel", "ExampleRequiredField", "All")]
    [string]$Gate,
    [switch]$NoBuild
)

# 验收闸门就是 Acceptance.Tests 里的普通 xUnit 测试，默认跳过；
# 本脚本只是把环境变量与 --filter 拼好，等价于直接跑 dotnet test。
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$testProject = Join-Path $repoRoot "tests\acceptance\Acceptance.Tests\Acceptance.Tests.csproj"

if ([string]::IsNullOrWhiteSpace($Gate)) {
    [Console]::Error.WriteLine("ACCEPT_GATE_REQUIRED: specify -Gate IdentityFoundation, Personnel, ExampleRequiredField, or All.")
    exit 2
}

$suite = "ICP.Platform.Acceptance.Tests.AcceptanceGateTests"
$filter = if ($Gate -eq "All") {
    "FullyQualifiedName~$suite"
} else {
    "FullyQualifiedName~$suite.${Gate}_CompletesEveryScenario"
}

$arguments = @("test", $testProject, "--configuration", "Release")
if ($NoBuild.IsPresent) {
    $arguments += @("--no-build", "--no-restore")
}
$arguments += @("--filter", $filter, "--logger", "console;verbosity=detailed")

$env:NEWP_RUN_ACCEPTANCE = "1"
try {
    dotnet @arguments
    exit $LASTEXITCODE
}
finally {
    Remove-Item Env:\NEWP_RUN_ACCEPTANCE -ErrorAction SilentlyContinue
}
