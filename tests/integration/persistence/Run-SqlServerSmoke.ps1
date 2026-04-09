$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "Invoke-PersistenceSmoke.ps1") -Provider "SqlServer"
