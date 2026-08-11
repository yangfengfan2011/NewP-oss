# WP2 AI-first 执行标准第二轮接入验证记录

## 目标

验证 WP2 第二轮是否已经把 AI-first 受控目标校验接入根测试入口与 CI 配置，同时保持范围仍受控在当前已达标的规范文档集合。

## 覆盖范围

- 受控目标校验入口：`scripts/check-ai-first-design-targets.ps1`
- Shell 包装入口：`scripts/check-ai-first-design-targets.sh`
- 根测试入口：`scripts/test.ps1`、`scripts/test.sh`
- CI 接入：`.github/workflows/ci.yml`
- 回归测试：`tests/Kernel.Tests/AiFirstDesignValidationScriptTests.cs`

## fresh 验证命令

- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~AiFirstDesignValidationScriptTests -nr:false -v minimal`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1`
- `bash -n ./scripts/check-ai-first-design-targets.sh`
- `bash -n ./scripts/test.sh`

## 本次结果

- `AiFirstDesignValidationScriptTests` 共 `8` 个全部通过，其中新增了受控目标入口的回归覆盖
- `check-ai-first-design-targets.ps1` 实跑通过，当前受控目标集为：
  - `docs/designs/detailed-design-template.md`
  - `docs/architecture/ai-first-execution-standard.md`
- `scripts/test.ps1` 已确认接入 AI-first 检查段，脚本输出中可见受控目标检查成功执行
- `scripts/check-ai-first-design-targets.sh` 与 `scripts/test.sh` 已完成 Shell 语法检查
- `.github/workflows/ci.yml` 已新增 AI-first 设计目标校验步骤，主线 CI 将在后端测试后执行该检查

## 观察到的非本轮阻塞项

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1` 整体未全绿，但失败来自仓库既存测试问题，而不是本轮新增的 AI-first 检查段
- 本次观测到的既存失败包括：
  - `PluginDiscoveryTests.PluginDiscoveryService_ValidPluginsDirectory_ReturnsDiscoveredPlugins`
  - `PluginDiscoveryTests.PluginDiscoveryService_PluginWithoutSha256_SkipsValidation`
  - `HostApiPluginManifestRouteTests.HostApiStarts_WhenInstalledPluginStateIsMissing_UsingBuiltInModuleFallback`
  - `HostApiPluginManifestRouteTests.FrontendPluginManifestRoute_ReturnsJson`
- 上述失败与当前机器缺少 `SqlServer` 连接串及若干既存测试状态有关，不作为本轮 WP2 是否接入成功的判定依据

## 已知边界

- 当前受控门禁仍只覆盖两份规范入口文档，不自动扫描全部 `docs/designs/*.md`
- CI 目前只校验受控目标集，还未对“所有新增设计文档”自动建门禁
- `scripts/test.ps1` 与 `scripts/test.sh` 仍是仓库级综合入口，现有无关失败会影响其整体退出状态

## 结论

- WP2 第二轮已完成
- AI-first 校验已从“定向手工命令”推进到“受控目标入口 + 根测试入口 + CI 配置接入”
- 下一步最合理的推进点是：只对新增设计文档建立可扩展的目标清单或自动发现规则，不提前要求历史设计文档一次性全量达标
