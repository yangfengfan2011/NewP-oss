# 插件治理

本文档说明当前插件平台的治理机制与策略。

## 治理范围

当前插件治理覆盖以下维度：

### 1. 版本兼容治理

插件通过 `plugin.json` 中的 `hostVersion` 字段声明可接受的宿主版本范围：

```json
{
  "hostVersion": {
    "min": "1.0.0",
    "max": "2.0.0"
  }
}
```

宿主在插件发现阶段会校验当前版本是否满足插件声明范围。不兼容的插件会被标记为 `IncompatibleHostVersion` 并跳过装载。

宿主版本取值顺序：

1. `Platform:HostVersion` 配置项
2. 宿主程序集版本
3. 默认回退值 `1.0.0`

### 2. 插件禁用治理

宿主支持通过配置显式禁用指定插件。

配置方式（`appsettings.json`）：

```json
{
  "Platform": {
    "DisabledPlugins": ["ModuleA", "ModuleB"]
  }
}
```

禁用策略在兼容性校验阶段生效，被禁用的插件会被标记为 `PluginDisabled` 并跳过装载。

### 3. 完整性校验治理

插件清单支持可选的 `entryAssemblySha256` 字段，用于声明入口程序集的 SHA-256 哈希值：

```json
{
  "entryAssemblySha256": "a1b2c3d4e5f6..."
}
```

当清单中声明了该字段时，宿主会在装载前计算入口程序集的实际哈希值并与声明值比对。不匹配的插件会被标记为 `EntryAssemblyHashMismatch` 并跳过装载。

如果清单中未声明该字段，则跳过哈希校验。

### 4. 失败隔离策略

当前采用"宿主继续启动，问题插件跳过"的容错策略。

失败场景包括：

- 插件目录不存在
- 清单文件不存在或格式非法
- 入口程序集不存在
- 宿主版本不兼容
- 插件被显式禁用
- 入口程序集哈希不匹配
- 程序集装载失败

对于上述失败：

- 宿主不会因单个插件失败而整体崩溃
- 失败插件不会进入运行时程序集集合
- 宿主日志会输出插件目录、失败类型和错误消息
- 失败插件会被记录在 `PluginRuntimeAssemblyLoadResult.FailedPlugins` 中

### 5. 治理日志

宿主启动时会输出插件治理摘要：

```
Plugin discovery root: ./modules, host version: 1.0.0, loadable plugins: 6, skipped plugins: 0
```

对于每个失败插件，会输出详细信息：

```
Skipped plugin package ./modules/BadPlugin: IncompatibleHostVersion - Plugin requires host version 2.0.0 to 3.0.0, but current host version is incompatible
```

## 治理实现位置

- 插件发现服务：`server/kernel/PluginDiscovery/PluginDiscoveryService.cs`
- 兼容性验证器：`server/kernel/PluginDiscovery/PluginCompatibilityValidator.cs`
- 宿主版本检查器：`server/kernel/PluginDiscovery/HostVersionChecker.cs`
- 禁用策略：`server/kernel/PluginDiscovery/PluginDisablePolicy.cs`
- 哈希验证器：`server/kernel/PluginDiscovery/PluginHashValidator.cs`
- 运行时装载器：`server/kernel/PluginDiscovery/PluginRuntimeAssemblyLoader.cs`
- 宿主入口：`apps/host-api/Program.cs`

## 当前未实现的治理内容

以下治理能力尚未纳入当前版本：

- 插件之间的依赖满足性校验
- 多版本插件并存策略
- 插件签名验证（当前只有可选的哈希校验）
- 远程插件市场与分发机制
- SDK / Contracts 版本兼容矩阵
- 前后端插件资源版本对齐
- 数据迁移器兼容性治理
- 插件来源与发布者验证

这些能力将在后续迭代中逐步补齐。

## 治理原则

当前治理机制遵循以下原则：

1. **明确性**：每个治理规则都有清晰的判断逻辑和失败消息
2. **可观测性**：所有治理决策都会输出到宿主日志
3. **渐进性**：先实现最小必要规则，后续逐步增强
4. **容错性**：单个插件失败不影响宿主整体启动
5. **可配置性**：关键治理参数（如禁用列表）可通过配置调整
