# WP4 Stage 9：Dictionary 静态资源转换与最小 i18n 基座

## 目标

- 将冻结样本 `Common.dic` 与 `Platform.dic` 转换为受控静态字典资源。
- 落地仓库内正式语言配置入口 `resources/localization/languages.json` 的消费链路。
- 在 `frontend/packages/core` 与 `frontend/packages/shell` 中补齐最小 i18n 基座与真实消费样例。

## 当前阶段归属

- Post-Phase-6
- `WP4 最小迁移连续性`
- `Stage 9：Dictionary 静态资源转换与最小 i18n 基座`

## 必读上下文

1. `AGENTS.md`
2. `docs/current-status.md`
3. `docs/designs/dictionary-enumeration-migration-preparation-design.md`
4. `docs/phases/post-phase-6-stage-map.md`
5. `docs/architecture/ai-first-execution-standard.md`
6. `resources/localization/languages.json`

## 允许修改的文件

- `scripts/migration/dictionary-to-i18n.ps1`
- `tests/Kernel.Tests/DictionaryToI18nScriptTests.cs`
- `resources/localization/**`
- `frontend/packages/core/src/**`
- `frontend/packages/shell/src/**`
- `frontend/packages/shell/tsconfig.json`
- `docs/current-status.md`
- `docs/phases/post-phase-6-stage-map.md`
- `docs/verification/verification-matrix.md`
- `docs/verification/wp4-stage-9-dictionary-static-resources-verification.md`

## 禁止触碰的范围

- `Enumeration` 的静态结构清单与本地化资源转换
- `ConfigCenter` 的数据库模型扩展或新导入 API
- 与 Stage 9 无直接关系的历史文档重写

## 最小交付物

- 一个可复跑的 `Dictionary -> resources/localization` 转换脚本
- 一组由真实冻结样本生成的静态字典 JSON
- `frontend/packages/core` 中的最小 i18n Provider / hook
- `frontend/packages/shell` 中的最小真实消费样例
- 一份 Stage 9 独立验证记录

## 建议验证方式

- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~DictionaryToI18nScriptTests -nr:false -v minimal`
- `powershell -NoProfile -ExecutionPolicy Bypass -Command "& '.\scripts\migration\dictionary-to-i18n.ps1' -InputPath @('D:\Github\ICP-X\OldP\Runtime\1_Server\Base\Config\Dictionary\Common.dic','D:\Github\ICP-X\OldP\Runtime\1_Server\Platform\Config\Dictionary\Platform.dic') -OutputRoot '.\resources\localization' -LanguagesConfigPath '.\resources\localization\languages.json'"`
- `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/localization-sample.test.tsx src/__tests__/home-page-shortcuts.test.tsx`
- `pnpm --dir .\frontend build`

## 交回主线程时必须汇报的内容

- 改了什么
- 生成了哪些静态资源文件
- 哪些验证通过
- 当前 Stage 9 结论
- 下一步是否可以进入 Stage 10
