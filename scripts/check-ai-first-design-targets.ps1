[CmdletBinding()]
param(
    [string[]]$AdditionalPath,
    [string]$IncludeChangedDesigns,
    [string]$TargetsFile
)

$ErrorActionPreference = 'Stop'

throw 'RETIRED_ENTRY: check-ai-first-design-targets 已退役。当前 test/verify/CI 使用 check-current-docs.ps1；设计工件由 AGENTS.md、GitHub issue/spec 和按需 skill 管理。'
