param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("SqlServer", "Oracle", "MySql")]
    [string]$Provider
)

$ErrorActionPreference = "Stop"

if ($Provider -ne "SqlServer") {
    throw "$Provider schema smoke is unavailable: only SQL Server has the disposable Host migration acceptance path."
}

& (Join-Path $PSScriptRoot "Run-SqlServerSmoke.ps1")
