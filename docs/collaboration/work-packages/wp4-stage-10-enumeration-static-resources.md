# WP4 Stage 10：Enumeration 静态结构清单与本地化资源转换

## 目标

- 将冻结样本 `Enumerations.enum` 与 `Enumerations.dic` 转换为受控静态枚举结构清单与本地化资源。
- 落地 `resources/localization/languages.json` 驱动的启用语言过滤。
- 明确 `.enum` 与 `.dic` 不完全对齐时的受控处理规则。

## 当前阶段归属

- Post-Phase-6
- `WP4 最小迁移连续性`
- `Stage 10：Enumeration 静态结构清单与本地化资源转换`

## 必读上下文

1. `AGENTS.md`
2. `docs/current-status.md`
3. `docs/designs/dictionary-enumeration-migration-preparation-design.md`
4. `docs/phases/post-phase-6-stage-map.md`
5. `docs/architecture/ai-first-execution-standard.md`
6. `resources/localization/languages.json`

## 允许修改的文件

- `scripts/migration/enumeration-to-static-resources.ps1`
- `tests/Kernel.Tests/EnumerationToStaticResourcesScriptTests.cs`
- `resources/enumerations/**`
- `resources/localization/**/enumerations/**`
- `docs/current-status.md`
- `docs/phases/post-phase-6-stage-map.md`
- `docs/verification/verification-matrix.md`
- `docs/verification/wp4-stage-10-enumeration-static-resources-verification.md`

## 禁止触碰的范围

- `Dictionary` 已完成的静态资源与最小 i18n 基座
- `ConfigCenter` 的数据库模型扩展或新导入 API
- DB Enumeration 与运行时查询 API
- 与 Stage 10 无直接关系的历史文档重写

## 最小交付物

- 一个可复跑的 `Enumeration -> resources/enumerations + resources/localization/*/enumerations` 转换脚本
- 一组由真实冻结样本生成的静态枚举结构 JSON
- 一组由真实冻结样本生成的枚举本地化 JSON
- 一份 Stage 10 独立验证记录

## 建议验证方式

- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~EnumerationToStaticResourcesScriptTests -nr:false -v minimal`
- `powershell -NoProfile -ExecutionPolicy Bypass -Command "& '.\scripts\migration\enumeration-to-static-resources.ps1' -EnumerationPath 'D:\Github\ICP-X\OldP\Runtime\1_Server\Platform\Config\Enumeration\Enumerations.enum' -DictionaryPath 'D:\Github\ICP-X\OldP\Runtime\1_Server\Platform\Config\Enumeration\Enumerations.dic' -OutputRoot '.\resources' -LanguagesConfigPath '.\resources\localization\languages.json'"`
- `powershell -NoProfile -Command "$structureCount = (Get-ChildItem '.\resources\enumerations' -Filter *.json -File | Measure-Object).Count; $zhsCount = (Get-ChildItem '.\resources\localization\zhs\enumerations' -Filter *.json -File | Measure-Object).Count; $enCount = (Get-ChildItem '.\resources\localization\en\enumerations' -Filter *.json -File | Measure-Object).Count; $gender = ((Get-Content '.\resources\localization\zhs\enumerations\Gender.json' -Raw | ConvertFrom-Json).'Gender.Female'); $selectModeExists = Test-Path '.\resources\localization\zhs\enumerations\SelectMode.json'; ""StructureCount=$structureCount""; ""ZhsCount=$zhsCount""; ""EnCount=$enCount""; ""GenderFemale=$gender""; ""SelectModeLocalizationExists=$selectModeExists"""`

## 交回主线程时必须汇报的内容

- 改了什么
- 生成了哪些结构文件与本地化文件
- 哪些验证通过
- 当前 Stage 10 结论
- 当前范围内还剩哪些边界
