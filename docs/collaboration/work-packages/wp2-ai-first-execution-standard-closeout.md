# WP2：AI-First 执行标准收口

## 1. 目标

将已经落地的 AI-first 正式标准、设计创建入口、校验脚本和受控门禁，接入当前协作入口与工作包入口，使 WP2 从“已有能力”升级为“默认工作路径”。

本工作包只处理 WP2 收口所需的协作文档、状态文档和验证记录，不进入新的业务实现，也不扩展为全仓文档治理。

## 2. 当前阶段归属

- 后续路线中的 `WP2`
- 属于“AI-first 执行标准落地”的最终收口包
- 应在 `docs/architecture/ai-first-execution-standard.md`、相关脚本与测试已经存在的前提下推进

## 3. 必读上下文

执行前请先阅读：

- `AGENTS.md`
- `docs/current-status.md`
- `docs/architecture/ai-first-execution-standard.md`
- `docs/designs/README.md`
- `docs/collaboration/current-collaboration-mode.md`
- `docs/collaboration/github-copilot-working-mode.md`
- `docs/collaboration/post-phase-6-copilot-stage-map.md`
- `docs/designs/ai-first-execution-standard-design.md`

## 4. 允许修改的文件

- `docs/collaboration/current-collaboration-mode.md`
- `docs/collaboration/github-copilot-working-mode.md`
- `docs/collaboration/post-phase-6-copilot-stage-map.md`
- `docs/architecture/ai-first-execution-standard.md`
- `docs/current-status.md`
- `docs/README.md`
- `docs/verification/ai-first-execution-standard-phase-7-verification.md`
- `tests/Kernel.Tests/AiFirstDesignValidationScriptTests.cs`
- `scripts/new-design-doc.ps1`

## 5. 禁止触碰的范围

- 不修改历史 `WP2` 设计工作包 `docs/collaboration/work-packages/wp2-ai-first-execution-standard-design.md`
- 不重写 `docs/designs/ai-first-execution-standard-design.md`
- 不扩成对全部历史 `docs/designs/*-design.md` 的一次性治理
- 不修改 `Plan.md`
- 不顺手进入 `WP3` 的大范围状态对齐
- 不修改任何与本轮无关的后端、前端或插件治理实现文件

## 6. 执行要求

- 优先把已有入口挂到协作流程，不重新发明新的治理机制
- 如果补脚本，只允许补“设计创建入口的最小闭环健壮性”，不新增新的大脚手架
- 明确说明何时使用 `new-design-doc`、何时使用 `validate-ai-first-design`、何时使用 `check-ai-first-design-targets`
- 更新 `docs/current-status.md` 时，要把 WP2 改成“已收口”，并把下一步推荐切到 `WP3`

## 7. 验收标准

满足以下条件才算完成：

1. 当前协作入口明确引用 `docs/architecture/ai-first-execution-standard.md`
2. 当前协作入口明确写出新增设计文档的默认创建与校验路径
3. `docs/current-status.md` 明确把 WP2 标为已收口
4. `new-design-doc.ps1` 不再允许无效模板直接创建并登记设计文档
5. 定向测试和定向门禁验证均重新通过

## 8. 建议验证方式

至少执行以下验证：

- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~AiFirstDesignValidationScriptTests -nr:false -v minimal`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1`

## 9. 交回 Codex 时必须汇报

完成后请用简短结构汇报：

- 改了什么
- 哪些协作入口已接入 AI-first 标准
- 跑了哪些验证
- 当前还有哪些刻意保留在 WP3 的边界
