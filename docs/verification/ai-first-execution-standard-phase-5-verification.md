# WP2 AI-first 执行标准第五轮收口验证记录

## 目标

验证 WP2 第五轮是否已经提供“把新增设计文档登记到 AI-first 显式目标清单”的稳定命令入口，并保持相对路径写入与重复登记幂等。

## 覆盖范围

- 目标登记入口：`scripts/register-ai-first-design-target.ps1`
- Shell 包装入口：`scripts/register-ai-first-design-target.sh`
- 显式目标清单：`docs/architecture/ai-first-design-check-targets.txt`
- 文档入口：`docs/designs/README.md`、`docs/architecture/ai-first-execution-standard.md`
- 回归测试：`tests/Kernel.Tests/AiFirstDesignValidationScriptTests.cs`

## fresh 验证命令

- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~AiFirstDesignValidationScriptTests -nr:false -v minimal`
- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~AiFirstDesignValidationScriptTests.RegisterAiFirstDesignTarget_AppendsRelativeDesignPathOnlyOnce -nr:false -v minimal`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1`

## 本次结果

- `AiFirstDesignValidationScriptTests` 已扩展到 `12` 个并全部通过
- 新增回归覆盖确认：
  - 通过 `register-ai-first-design-target.ps1` 可将设计文档以相对路径写入目标清单
  - 对同一设计文档重复登记不会重复追加
- 正式标准文档与设计入口文档已同时补齐登记命令，新增设计进入门禁不再只能依赖隐式发现或手工编辑清单

## 已知边界

- 当前登记入口仍需执行者主动调用，尚未与脚手架或任务模板自动联动
- 当前没有自动把“已登记设计”与“当前 PR/工作区新增设计”做一致性比对
- 本轮仍未改变历史设计文档的渐进式治理边界

## 结论

- WP2 第五轮已完成
- AI-first 门禁现在同时具备：显式基础清单、校验入口、自动发现补充、以及可重复使用的登记入口
- 下一步最合理的推进点是：将登记动作嵌入新增设计的创建流程，例如任务模板或脚手架，而不是继续依赖执行者手工记忆
