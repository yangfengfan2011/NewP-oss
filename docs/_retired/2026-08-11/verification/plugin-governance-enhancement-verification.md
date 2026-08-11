# WP1 插件平台治理增强验证记录

## 目标

验证 WP1 是否已经满足“依赖可治理、生命周期可约束、卸载可闭环、版本可清理、rollback 可恢复”的阶段完成标准。

## 覆盖范围

- 发现阶段插件依赖治理
- 插件状态机中的 `Disabled` 语义区分
- 统一插件包 `uninstall` 闭环
- install warning / enable 依赖强校验 / uninstall warning
- 默认保留最近 2 个版本的版本清理策略
- HostApi / Web 路径 rollback 原子恢复

## fresh 验证命令

- `dotnet build .\tests\Kernel.Tests\Kernel.Tests.csproj -nr:false -v minimal`
- `dotnet build .\tools\PluginOps\PluginOps.csproj -nr:false -v minimal`
- `dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal`
- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter "FullyQualifiedName~PluginRuntimeAssemblyLoaderTests|FullyQualifiedName~PluginOperationsAdminServiceTests|FullyQualifiedName~UnifiedPluginPackageLifecycleOperationsTests|FullyQualifiedName~UnifiedPluginPackageInstallerTests|FullyQualifiedName~PluginOperationsLifecycleTests|FullyQualifiedName~PluginOperationsControllerTests|FullyQualifiedName~PluginOpsIntegrationTests" -nr:false -v minimal`
- `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/plugin-ops-client.test.ts src/__tests__/plugin-ops-page.test.tsx`
- `pnpm --dir .\frontend --filter @icp-platform/shell build`

## 本次结果

- `Kernel.Tests` 构建成功
- `PluginOps` 构建成功
- `HostApi` 构建成功
- WP1 相关定向测试共 `124` 个全部通过
- `plugin-ops` shell 定向 vitest `6` 个全部通过
- `@icp-platform/shell` 构建成功
- 发现阶段依赖不满足插件会被阻断，不进入程序集装载
- `uninstall` 已贯通 Kernel、HostApi、CLI 与管理脚本
- install warning / enable 强校验 / uninstall warning 已通过对应测试覆盖
- 版本清理策略已验证：install / enable 后会清理旧版本，同时保护当前启用版本与回滚目标
- rollback 在 CLI 与 HostApi/Web 路径均已具备失败恢复能力，控制器级失败注入返回受控失败而非未处理异常
- enable / disable / uninstall 在 HostApi/Web 路径上也已补齐失败恢复，控制器级失败注入返回 `400` 且状态可恢复
- 运维页已补齐 uninstall 动作，并以 warning tone 展示 install / uninstall 的治理提示

## 已知边界

- 依赖治理当前仍为模块级，不支持版本约束
- 运维页当前按 message 关键字推断 warning tone，后续若要进一步标准化，可考虑下沉为显式 message severity 字段
- 版本清理以“保护当前启用版本与回滚目标”为优先，因此在保护场景下可能临时保留超过 2 个历史版本目录

## 结论

- WP1 已完成
- 插件平台已从“最小可运行”收口到“行为可控、边界清晰、失败可诊断、回滚可恢复”的治理闭环
- 下一步建议转入 WP2 AI-first 规范落地；WP1 后续仅保留体验型优化项，例如统一的 warning severity 与 destructive-action 交互规范
