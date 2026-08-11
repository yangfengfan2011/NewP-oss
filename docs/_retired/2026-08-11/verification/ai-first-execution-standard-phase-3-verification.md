# WP2 AI-first 执行标准第三轮接入验证记录

## 目标

验证 WP2 第三轮是否已经把 AI-first 门禁从“受控基础集”扩展到“受控基础集 + 新增设计文档”，同时继续避免历史设计文档一次性全量达标的高风险扩散。

## 覆盖范围

- 受控门禁入口：`scripts/check-ai-first-design-targets.ps1`
- 附加设计显式传参：`-AdditionalPath`
- 当前工作区新增 / 修改设计文档的自动发现逻辑
- 回归测试：`tests/Kernel.Tests/AiFirstDesignValidationScriptTests.cs`

## fresh 验证命令

- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~AiFirstDesignValidationScriptTests -nr:false -v minimal`
- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~AiFirstDesignValidationScriptTests.CheckAiFirstDesignTargets_FailsWhenAdditionalDesignDoesNotMeetChecklist -nr:false -v minimal`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1`

## 本次结果

- `AiFirstDesignValidationScriptTests` 共 `10` 个全部通过
- 新增回归覆盖确认：
  - 当通过 `-AdditionalPath` 传入一份不满足 AI-first 清单的设计文档时，受控门禁会返回非零退出码
  - 当 `docs/designs` 下出现未跟踪且不满足清单的新设计文档时，受控门禁会自动发现并返回非零退出码
- `check-ai-first-design-targets.ps1` 当前行为已扩展为：
  - 始终校验固定规范入口文档
  - 支持显式传入附加设计文档
  - 默认自动发现当前工作区中新增或修改的 `docs/designs/*-design.md`
- 在当前工作区下，受控门禁实跑仍能通过，说明自动发现逻辑没有误伤现有 WP2 改动边界

## 已知边界

- 自动发现当前仍基于工作区变化，不等同于 CI 中对远端分支差异的完整历史比对
- 当前没有把历史 `docs/designs/*.md` 全量纳入门禁；这是刻意保持边界，而不是遗漏
- 若后续修改既有旧设计文档且它们仍不满足新标准，受控门禁会开始暴露这些缺口，需要结合具体任务逐步补齐

## 结论

- WP2 第三轮已完成
- AI-first 门禁已从“只校验规范入口”推进到“覆盖真实新增设计文档工作流”
- 下一步最合理的推进点是：把新增设计文档的识别规则进一步稳定到提交流程或任务脚手架中，同时继续保持对历史文档的渐进式治理，而不是一次性清账
