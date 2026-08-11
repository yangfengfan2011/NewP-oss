[CmdletBinding()]
param(
    [string]$DesignName,
    [string]$Category,
    [string]$DesignsRoot,
    [string]$TemplatePath,
    [string]$TargetsFile,
    [string]$RegisterAiFirst
)

$ErrorActionPreference = 'Stop'

throw 'RETIRED_ENTRY: new-design-doc 已退役。新设计使用当前 GitHub issue/spec、用户指定文档或已调用 skill 规定的产物；不要创建旧阶段设计目录。'
