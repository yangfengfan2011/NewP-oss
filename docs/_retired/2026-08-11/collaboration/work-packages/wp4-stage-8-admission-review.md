# WP4 Stage 8：实施准入复核

## 目标

- 逐条复核 `docs/designs/dictionary-enumeration-migration-preparation-design.md` 第 8 节中的正式实施准入条件。
- 输出 `可进入实现 / 暂不可进入实现` 的结论与依据。
- 如果暂不可进入实现，只输出最小阻塞项，不越级开始 `Dictionary / Enumeration` 导入实现。

## 当前阶段归属

- Post-Phase-6
- `WP4 最小迁移连续性`
- `Stage 8：实施准入复核`

## 必读上下文

1. `AGENTS.md`
2. `docs/current-status.md`
3. `docs/designs/post-phase-6-governance-and-ai-first-roadmap-design.md`
4. `docs/designs/dictionary-enumeration-migration-preparation-design.md`
5. `docs/architecture/ai-first-execution-standard.md`
6. `docs/collaboration/current-collaboration-mode.md`
7. `docs/history/collaboration/github-copilot-working-mode.md`
8. `docs/phases/post-phase-6-stage-map.md`

## 允许修改的文件

- `docs/current-status.md`
- `docs/designs/dictionary-enumeration-migration-preparation-design.md`
- `docs/phases/post-phase-6-stage-map.md`
- `docs/collaboration/work-packages/wp4-stage-8-admission-review.md`
- `docs/verification/verification-matrix.md`
- `docs/verification/wp4-stage-8-admission-review-verification.md`

## 禁止触碰的范围

- `server/`、`frontend/`、`apps/` 下的实现代码
- `Dictionary / Enumeration` 的正式导入 API、解析器、测试与脚本实现
- 与 `WP4 Stage 8` 无直接关系的历史文档重写

## 执行要求

1. 先核对设计入口、样本入口、规则入口、验证入口、协作入口。
2. 只围绕第 8 节的 5 条准入条件收集证据，不扩展到 Stage 9 / Stage 10。
3. 如需最小补文档，仅限补齐 Stage 8 所需的显式入口、验证记录和状态同步。
4. 至少运行一组与 `ConfigCenter`、Setting 模板和设计门禁直接相关的定向验证。
5. 汇报时必须区分：已满足、缺证据、有冲突。

## 最小交付物

- 一份 Stage 8 准入复核验证记录
- 当前状态文档中的 Stage 8 结论同步
- 验证矩阵中的 Stage 8 定向验证入口

## 建议验证方式

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\designs\dictionary-enumeration-migration-preparation-design.md`
- `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "FullyQualifiedName~LegacySettingImportServiceTests|FullyQualifiedName~ConfigServiceTests|FullyQualifiedName~ConfigCenterModelDefinitionTests" -nr:false -v minimal`
- 样本路径存在性检查、工作包存在性检查、相关文档 `Select-String` 核对

## 交回主线程时必须汇报的内容

- 改了什么
- 修改了哪些文件
- 哪些验证通过，哪些验证失败
- 当前准入结论
- 剩余阻塞项
- 如果阻塞解除，下一包更建议先做 `Stage 9` 还是 `Stage 10`
