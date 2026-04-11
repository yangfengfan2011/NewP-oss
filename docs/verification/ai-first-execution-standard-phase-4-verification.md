# WP2 AI-first 执行标准第四轮收口验证记录

## 目标

验证 WP2 第四轮是否已经把 AI-first 受控基础目标集合从脚本硬编码收口为仓库内显式清单，并保持受控门禁、回归测试与现有入口一致工作。

## 覆盖范围

- 显式目标清单：`docs/architecture/ai-first-design-check-targets.txt`
- 受控门禁入口：`scripts/check-ai-first-design-targets.ps1`
- `-TargetsFile` 显式目标文件参数
- 回归测试：`tests/Kernel.Tests/AiFirstDesignValidationScriptTests.cs`

## fresh 验证命令

- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~AiFirstDesignValidationScriptTests -nr:false -v minimal`
- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~AiFirstDesignValidationScriptTests.CheckAiFirstDesignTargets_UsesExplicitTargetsFile -nr:false -v minimal`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1`

## 本次结果

- `AiFirstDesignValidationScriptTests` 共 `11` 个全部通过
- 新增回归覆盖确认：
  - 当通过 `-TargetsFile` 指定一份显式目标清单，且清单中包含不满足 AI-first 清单的设计文档时，受控门禁会返回非零退出码
- 默认受控门禁仍可成功执行，说明清单文件 `docs/architecture/ai-first-design-check-targets.txt` 能正确驱动当前基础目标集合
- AI-first 执行标准文档已更新为引用显式目标清单，受控基础集的来源不再散落在脚本内部

## 已知边界

- 当前显式清单只覆盖受控基础目标，不自动承载所有新增设计文档
- 新增设计文档仍主要依赖自动发现或显式传参纳入门禁，尚未与脚手架或专门登记文件完全打通
- 本轮没有改变历史设计文档的治理边界，仍然保持渐进式对齐策略

## 结论

- WP2 第四轮已完成
- AI-first 受控基础集已经从“脚本硬编码”收口为“仓库内显式清单 + 脚本消费”的更稳定结构
- 下一步最合理的推进点是：将新增设计文档的登记或发现机制进一步稳定到脚手架、任务模板或更明确的提交流程，而不是继续叠加隐式约定
