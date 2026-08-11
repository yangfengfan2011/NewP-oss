# 插件兼容策略

本文档定义当前运行层插件平台的最小兼容策略。

## 1. 当前兼容判断维度

当前已落地的正式判断包括三项：

- 宿主版本兼容
- 宿主禁用名单
- 可选的入口程序集完整性校验

插件通过 `plugin.json` 中的 `hostVersion.min` / `hostVersion.max` 声明可接受的宿主版本范围，并可通过 `entryAssemblySha256` 声明入口程序集的期望哈希值。

示例：

```json
{
  "hostVersion": {
    "min": "1.0.0",
    "max": "2.0.0"
  }
}
```

## 2. 当前行为

### 2.1 宿主版本不兼容

当宿主版本不满足插件声明范围时：

- 该插件被标记为 `IncompatibleHostVersion`
- 插件不会进入运行时程序集装载阶段
- 宿主继续启动
- 宿主日志会输出明确的兼容失败信息

### 2.2 插件被禁用

当插件 `moduleId` 出现在 `Platform:DisabledPlugins` 中时：

- 该插件被标记为 `PluginDisabled`
- 插件不会进入运行时程序集装载阶段
- 宿主继续启动
- 宿主日志会输出明确的禁用信息

### 2.3 入口程序集完整性失败

当插件清单声明了 `entryAssemblySha256` 且实际入口程序集哈希不匹配时：

- 该插件被标记为 `EntryAssemblyHashMismatch`
- 插件不会进入运行时程序集装载阶段
- 宿主继续启动
- 宿主日志会输出明确的哈希失败信息

当前整体策略仍然是“阻断问题插件、不中断整体宿主”。

## 3. 当前版本来源

宿主版本当前取值顺序为：

1. `Platform:HostVersion`
2. 宿主程序集版本
3. 默认回退值 `1.0.0`

当前仓库已在 `apps/host-api/appsettings.json` 中显式声明：

- `Platform:HostVersion = 1.0.0`
- `Platform:DisabledPlugins = []`

插件清单当前还支持：

- `entryAssemblySha256`
  可选的入口程序集 SHA-256 声明；未声明时不参与校验。

## 4. 当前未纳入正式兼容判断的内容

以下信息已在设计中出现，但本轮尚未进入正式强校验：

- 插件之间的依赖满足性
- SDK / Contracts 版本兼容矩阵
- 前后端插件资源版本对齐
- 数据迁移器兼容性
- 签名与来源验证

这些内容后续继续增强，不代表当前已经实现。

## 5. 设计原则

当前兼容策略遵循：

- 先实现明确、可解释、可日志化的最小规则
- 先解决宿主与插件之间的一阶兼容问题
- 不在第一轮把依赖治理、签名治理、前端对齐一次性揉进装配入口
- 对单个问题插件采取“可阻断、可记录、可解释”的宿主级处理

## 6. 回滚与排障

当前排障路径：

1. 检查宿主日志中的插件失败记录
2. 如果失败类型为 `PluginDisabled`，检查 `Platform:DisabledPlugins`
3. 如果失败类型为 `IncompatibleHostVersion`，确认 `plugin.json` 中的 `hostVersion` 与宿主配置版本
4. 如果失败类型为 `EntryAssemblyHashMismatch`，确认 `entryAssemblySha256` 与实际构建产物一致
5. 修正插件清单、调整配置或回退插件包
6. 重新构建并启动宿主验证

更正式的升级与回滚说明见 `docs/guides/plugin-upgrade-and-rollback.md`。
