# WP2 AI-first 执行标准第六轮收口验证记录

## 目标

验证 WP2 第六轮是否已经提供“按模板创建设计文档并默认登记到 AI-first 门禁”的统一入口，减少执行者在新增设计创建阶段的手工步骤。

## 覆盖范围

- 设计创建入口：`scripts/new-design-doc.ps1`
- Shell 包装入口：`scripts/new-design-doc.sh`
- 目标登记入口：`scripts/register-ai-first-design-target.ps1`
- 文档入口：`docs/designs/README.md`、`docs/architecture/ai-first-execution-standard.md`
- 回归测试：`tests/Kernel.Tests/AiFirstDesignValidationScriptTests.cs`

## fresh 验证命令

- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~AiFirstDesignValidationScriptTests -nr:false -v minimal`
- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~AiFirstDesignValidationScriptTests.NewDesignDoc_CreatesDesignFromTemplateAndRegistersTarget -nr:false -v minimal`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1`

## 本次结果

- `AiFirstDesignValidationScriptTests` 已扩展到 `13` 个并全部通过
- 新增回归覆盖确认：
  - `new-design-doc.ps1` 会按模板创建 `<name>-design.md`
  - 在默认开启登记时，新设计会被写入 AI-first 显式目标清单
- 设计入口文档与正式标准文档均已补齐创建命令，新增设计的最小标准路径已收敛为单一脚本入口

## 已知边界

- 当前创建入口仍是最小脚手架，只复制模板，不自动填充设计标题或元数据
- 还没有与更高层协作模板、任务包或 IDE 命令进一步联动
- 本轮仍未改变历史设计文档的渐进式治理边界

## 结论

- WP2 第六轮已完成
- AI-first 门禁现在已经覆盖“基础清单、校验、自动发现、登记、创建”这一条更完整的新增设计最小闭环
- 下一步最合理的推进点是：把 `new-design-doc` 入口接入更高层协作模板或执行手册，让新增设计默认从这条路径开始
