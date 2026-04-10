# 插件治理增强设计

## 1. 背景

当前仓库的插件平台已完成首版运行闭环：

- 插件清单驱动的目录扫描与程序集装载
- 宿主版本兼容校验
- 宿主禁用列表
- 可选的入口程序集 SHA-256 完整性校验
- 统一插件包的 install / enable / disable / rollback 运维动作
- 插件运维中心 V1 网页入口

这些能力让插件平台从"模块直接编码接入宿主"跨越到了"清单驱动、可运维、可隔离"。但首版闭环只解决了"插件能不能被装载和切换"，还没有解决"插件边界是否可控、依赖是否满足、卸载是否安全、失败是否可诊断、回滚语义是否充分"。

随着内建模块数量增加和后续外部插件接入预期，治理缺口在以下方面开始显现：

- 依赖只声明不校验：插件清单中的 `dependencies` 字段已被解析，但宿主发现阶段和安装阶段都不验证依赖是否满足。
- 卸载能力缺失：当前只有 disable，没有 uninstall；旧版本包内容会永久留在安装目录。
- 回滚语义有限：当前 rollback 只切换启用版本和重建运行时投影，不涉及插件自身持久化数据或迁移器产出。
- 生命周期缺少正式状态机：状态记录只是 `InstalledPluginRecord` 中的字符串字段，没有正式的状态转移约束。

本设计旨在明确这些治理增强的目标形态、边界和分阶段实施路径，为后续实现提供统一依据。

## 2. 目标

- 定义插件生命周期正式状态机，明确每个状态和每条合法转移。
- 定义依赖治理模型，明确依赖满足性校验的时机、范围和失败信号。
- 定义卸载语义，明确卸载对安装目录、状态目录、运行时投影和前端资源投影的影响。
- 统一回滚语义说明，区分"状态回滚"与"安装内容 + 投影 + 状态"回滚。
- 汇总所有治理相关的失败信号，建立统一的失败诊断入口。
- 明确 Host 和 Shell 在增强治理后仍不新增模块私有接入特例。

## 3. 非目标

- 不设计插件签名与发布者信任链体系。
- 不设计远程插件市场与分发机制。
- 不设计运行时热加载或热卸载。
- 不设计多节点集群级版本协调。
- 不设计 SDK / Contracts 版本兼容矩阵。
- 不设计前后端插件资源版本强对齐机制。
- 不修改任何现有代码。

## 4. 范围

本设计覆盖以下治理增强主题：

- 生命周期与状态模型
- 依赖治理模型
- 卸载、清理与回滚语义
- 失败信号与验证
- Host 与 Shell 接入边界约束

本设计涉及的现有代码边界（仅作为设计参考，本轮不修改代码）：

- `server/kernel/PluginDiscovery/`：插件发现、兼容校验、运行时装载
- `server/kernel/PluginPackages/`：统一插件包安装器、生命周期操作、运行时视图物化
- `server/kernel/ModuleLoader/AssemblyModuleLoader.cs`：模块依赖排序
- `tools/PluginOps/`：CLI 运维入口
- `apps/host-api/`：宿主启动入口

本设计涉及的现有文档（口径对齐依据）：

- `docs/architecture/plugin-package-format.md`
- `docs/architecture/plugin-runtime-loading.md`
- `docs/architecture/plugin-compatibility-policy.md`
- `docs/architecture/plugin-governance.md`
- `docs/guides/unified-plugin-package-operations.md`
- `docs/guides/plugin-upgrade-and-rollback.md`

## 5. 设计约束

### 5.1 架构约束

- Host 只负责启动、组合、模块加载和宿主级治理判断，不承接模块私有逻辑。
- Shell 只负责布局、Provider、菜单聚合、路由聚合和受控注册，不承接模块私有接入特例。
- 增强治理能力不得放宽 Host 和 Shell 的已有受控接入点。
- 外部插件开发仍通过 `Contracts / SDK / templates / controlled entry points` 接入，不为任何模块新增私有装配入口。

### 5.2 兼容性约束

- 增强治理必须向后兼容当前 `plugin.json` 清单格式；新增字段采用可选方式，不破坏现有清单。
- 当前 install / enable / disable / rollback 行为不因治理增强而改变已有语义。
- 当前宿主"阻断问题插件、不中断整体宿主"的失败隔离策略继续保持。

### 5.3 可验证性约束

- 每个新增治理规则都必须有对应的失败信号和日志输出。
- 每个状态转移都必须可在单元测试中验证。
- 治理增强不依赖单机路径、单机用户名或特定 IDE 状态。

## 6. 总体方案

### 6.1 当前边界

当前插件平台的治理能力集中在"发现阶段兼容性判断"和"运维动作最小闭环"两个层面：

- 发现阶段：宿主版本兼容、禁用列表、可选哈希校验。
- 运维动作：install、enable、disable、rollback，外加运行时视图物化。

治理增强的核心思路是：在不改变现有两层结构的前提下，补齐依赖满足性校验、状态机正式化、卸载能力和回滚语义完整性。

### 6.2 增强方向

治理增强分为四个方向：

1. **生命周期状态机正式化**：将 `InstalledPluginRecord` 中的隐式状态转换为正式状态机，明确合法转移和拒绝信号。
2. **依赖治理**：在发现阶段和安装阶段增加依赖满足性校验，对不满足的依赖输出明确失败信号。
3. **卸载与清理**：新增 uninstall 动作，定义其对安装目录、状态目录和运行时投影的影响。
4. **回滚语义完善**：区分运行时回滚与数据回滚，明确当前 rollback 的准确边界。

### 6.3 不选方案

不采用"先设计完整的插件市场与签名体系再补治理"的路线，因为：

- 当前仍以内建模块和受控外部插件为主要场景，签名与市场功能在当前阶段无真实驱动力。
- 治理增强应优先解决已暴露的结构性空缺（依赖、卸载、回滚），而不是先铺设分发渠道。

## 7. 生命周期与状态模型

### 7.1 当前状态

当前 `InstalledPluginRecord` 通过以下字段隐式表达插件状态：

- `Status`：字符串值，取值为 `"Installed"` 或 `"Failed"`。
- `CurrentEnabledVersion`：非空表示已启用，为空表示已安装但未启用或已禁用。
- `InstalledVersions`：已安装版本列表。

这些字段足以支撑当前运维动作，但缺乏正式的状态转移约束——调用方必须理解字段组合的隐含语义才能判断当前状态。

### 7.2 正式状态定义

建议将插件生命周期归纳为以下正式状态：

| 状态 | 含义 | 对应当前字段组合 |
|------|------|------------------|
| `NotInstalled` | 该模块从未安装过，安装目录中没有状态记录 | 无 `state/<moduleId>.json` |
| `Installed` | 至少一个版本已安装，但没有版本处于启用态 | `Status = "Installed"`, `CurrentEnabledVersion = null` |
| `Enabled` | 有一个版本处于启用态，运行时投影已就绪 | `Status = "Installed"`, `CurrentEnabledVersion != null` |
| `Disabled` | 曾经被启用但被显式禁用，运行时投影已移除 | `Status = "Installed"`, `CurrentEnabledVersion = null`（经 disable 动作达到） |
| `Uninstalled` | 曾经安装过但已被卸载，安装目录和运行时投影已清理 | 状态记录已移除 |

说明：

- `Installed` 与 `Disabled` 在当前字段组合上不可区分。正式化后建议在状态记录中增加一个表达禁用来源的标记，以区分"刚安装未启用"和"曾启用后被禁用"。
- `Uninstalled` 不会保留状态记录文件。如果需要保留卸载审计信息，应由审计模块记录，而不是在插件状态目录中留档。

### 7.3 状态转移矩阵

```
NotInstalled ──install──▶ Installed
Installed ────enable───▶ Enabled
Installed ────uninstall─▶ Uninstalled (即 NotInstalled)
Enabled ──────disable──▶ Disabled
Enabled ──────rollback─▶ Enabled (版本切换)
Enabled ──────enable───▶ Enabled (版本切换)
Disabled ─────enable───▶ Enabled
Disabled ─────uninstall─▶ Uninstalled (即 NotInstalled)
Installed ────install──▶ Installed (新版本追加)
Enabled ──────install──▶ Enabled (新版本追加，启用版本不变；若重装当前启用版本则刷新投影)
Disabled ─────install──▶ Disabled (新版本追加)
```

补充说明：

- 卸载的合法起点仅限 `Installed` 和 `Disabled`。`Enabled` 不属于合法卸载源状态，必须先经过 `disable` 转移到 `Disabled`，再执行 `uninstall`。

非法转移示例：

- `NotInstalled → enable`：必须先 install。
- `NotInstalled → disable`：无意义。
- `Enabled → uninstall`：必须先 disable，再 uninstall。直接对 Enabled 状态的插件执行 uninstall 应被拒绝。
- `Enabled → rollback` 但只有一个已安装版本：无可回滚版本，拒绝并返回失败信号。

### 7.4 设计要求

- 每条合法转移都应在操作执行前进行前置状态校验。
- 非法转移应返回明确的错误类型和消息，不静默忽略。
- 状态转移的结果应与运行时投影视图的实际变化保持一致——状态记录指向新版本时，投影视图必须已完成重建；若投影视图重建失败，状态记录应回退到转移前的值。

## 8. 依赖治理模型

### 8.1 当前边界

当前依赖处理存在于两个阶段：

1. **清单解析阶段**：`PluginManifest.Dependencies` 从 `plugin.json` 中读取依赖列表，类型为 `IReadOnlyList<string>`，只包含模块 ID，不包含版本约束。
2. **模块排序阶段**：`AssemblyModuleLoader` 使用深度优先拓扑排序，按 `IModuleMetadata.Dependencies` 对已加载模块排序。排序发现缺失依赖时，仅跳过该依赖，不阻断依赖方模块装载。

这意味着：如果插件 B 声明依赖插件 A，但 A 因宿主版本不兼容而未装载，B 仍然会进入装载阶段，可能在运行时因缺少 A 的服务注册而失败。

### 8.2 增强方案

增加依赖满足性校验，在以下两个时机执行：

#### 8.2.1 宿主启动发现阶段

在 `PluginDiscoveryService` 完成兼容性校验之后、执行程序集装载之前，新增依赖满足性检查：

- 对每个通过兼容性校验的插件，检查其 `dependencies` 列表中的所有模块 ID 是否都在本轮通过校验的插件集合中。
- 如果存在未满足的依赖，将该插件标记为新增的失败类型 `DependencyNotSatisfied`，附带缺失依赖的模块 ID 列表。
- 被标记的插件不进入程序集装载阶段。
- 依赖校验应支持传递性：如果 A 依赖 B、B 依赖 C，而 C 未满足，则 B 和 A 都应被标记为依赖不满足。

#### 8.2.2 统一插件包安装阶段

在 `UnifiedPluginPackageInstaller` 执行安装时，增加可选的依赖预检：

- 安装时读取待安装插件的 `dependencies`，检查每个依赖是否已在安装目录中存在。
- 如果依赖不满足，默认行为是输出警告但不阻断安装。原因是安装不等于启用，依赖可以在启用前补齐。
- 在启用（enable）阶段执行强校验：目标版本声明的所有依赖模块必须处于可装载状态——至少已启用并能进入运行时投影视图。如果依赖模块未启用或不存在，则拒绝启用并返回明确的失败信号。

### 8.3 依赖声明格式

当前 `dependencies` 为模块 ID 字符串列表，当前阶段不引入版本约束。后续如需版本约束，可扩展为对象数组格式：

当前格式（继续保持）：

```json
{
  "dependencies": ["Authorization", "ConfigCenter"]
}
```

后续建议格式（预留，当前不实现）：

```json
{
  "dependencies": [
    { "moduleId": "Authorization", "minVersion": "1.0.0" },
    { "moduleId": "ConfigCenter" }
  ]
}
```

### 8.4 依赖治理约束

- 循环依赖应在发现阶段被检测并拒绝，输出明确的循环路径。当前 `AssemblyModuleLoader` 已有循环检测，增强后应在更早阶段（清单解析后、程序集装载前）执行。
- 依赖冲突（同一模块被多个版本要求）在当前单版本启用模型下不会发生；后续如引入多版本并存，需要独立设计。
- 依赖校验结果应输出到宿主日志，格式与现有失败信号一致：`Skipped plugin package {dir}: DependencyNotSatisfied - Missing dependencies: {list}`。

## 9. 卸载、清理与回滚语义

### 9.1 当前边界

当前运维动作中没有 uninstall 命令。disable 只是清空 `CurrentEnabledVersion` 并重建运行时投影（移除该插件的投影视图），但所有已安装版本的包文件仍保留在 `packages/<moduleId>/<version>/` 下，状态文件 `state/<moduleId>.json` 也保留。

当前 rollback 切换到上一个已安装版本，并重建运行时投影，但不删除任何磁盘文件，也不处理插件可能产生的持久化数据。

### 9.2 卸载（uninstall）语义

卸载是比禁用更强的操作，其目标是将插件从安装目录中完全移除。

卸载应影响以下内容：

| 目录/文件 | 卸载行为 |
|-----------|----------|
| `packages/<moduleId>/` | 删除该模块所有已安装版本的包目录 |
| `state/<moduleId>.json` | 删除状态记录文件 |
| `runtime/backend-load-view/<moduleId>/` | 清理后端运行时投影 |
| `runtime/frontend-plugin-manifest.json` | 重建，移除该模块条目 |
| `public/<moduleId>/` | 删除前端静态资源投影 |

卸载不影响以下内容：

- 宿主配置中的 `Platform:DisabledPlugins` 条目（如果管理员手动添加过，卸载不自动移除配置条目）。
- 插件在宿主运行期间已经注册的服务或已产生的持久化数据（数据库表、文件等）——卸载只处理插件安装态，不处理插件运行时产出。
- 审计日志——卸载动作本身可以作为审计事件被记录，但卸载不会清理该插件过去的审计记录。

### 9.3 卸载前置条件

- 插件必须处于 `Installed` 或 `Disabled` 状态才能被卸载。
- 处于 `Enabled` 状态的插件必须先执行 disable，再执行 uninstall。直接对 Enabled 状态的插件执行 uninstall 应被拒绝，返回明确的错误信号。
- 如果其他已安装插件声明依赖当前待卸载插件，卸载操作应输出警告（列出依赖方），但当前阶段不强制阻断。后续建议在有更完整的依赖图后升级为强阻断。

### 9.4 回滚语义完善

当前 `rollback` 动作的准确语义：

- 前提：插件处于 `Enabled` 状态且 `InstalledVersions` 中存在上一个版本。
- 操作：将 `CurrentEnabledVersion` 切换到 `InstalledVersions` 列表中当前版本的前一个条目，然后重建运行时投影。
- 范围：只回滚"哪个版本被启用"和"运行时投影视图"。

回滚不覆盖以下内容：

- **插件持久化数据**：如果插件 v2 修改了数据库 schema 或写入了新数据格式，rollback 到 v1 不会自动执行数据库回滚。数据层回滚属于插件迁移器的职责，当前迁移器体系尚未与插件版本切换联动。
- **前端缓存**：回滚不清理用户浏览器中可能缓存的旧版前端资源。前端资源路径中包含版本号，版本切换后浏览器应加载新路径的资源。
- **宿主已载入的程序集**：当前 .NET 的 `Assembly.LoadFrom` 不支持卸载已载入程序集。回滚后需要重启宿主才能使后端程序集切换生效。

回滚失败恢复：

- 如果 rollback 过程中运行时投影重建失败，`UnifiedPluginPackageOperations` 应尝试恢复到 rollback 前的状态记录，避免"状态已回滚、投影未回滚"的中间态。
- 当前边界：CLI 包装层（`PluginOps` / `manage-unified-plugin` 脚本）当前已有基本的恢复思路，但 Web / HostApi 生命周期路径（`/api/plugin-ops/*`）尚未具备完整的原子恢复保证。这意味着通过网页运维中心执行 rollback 时，如果投影重建失败，可能出现状态与投影不一致的中间态。
- 后续建议：在 Web 运维路径中补齐与 CLI 同等的原子恢复保护，确保所有入口的 rollback 失败行为一致。这应作为后续治理实施的补齐项。

### 9.5 清理策略

安装目录中可能随时间累积多个旧版本包。建议后续引入可选的版本保留策略：

- 默认保留最近 N 个已安装版本（建议 N=2：当前启用版本 + 上一个可回滚版本）。
- 超出保留数量的最旧版本在下一次 install 或 enable 时自动清理。
- 清理只移除 `packages/<moduleId>/<oldVersion>/` 和 `public/<moduleId>/<oldVersion>/`，不影响状态记录中的 `InstalledVersions` 条目以外的内容。

当前阶段不实现自动清理，只在设计文档中预留方向。

## 10. 失败信号与验证

### 10.1 现有失败信号

当前已落地的失败类型（`PluginScanErrorType` 枚举）：

| 错误类型 | 含义 |
|----------|------|
| `DirectoryNotFound` | 插件目录不存在 |
| `ManifestNotFound` | `plugin.json` 不存在 |
| `InvalidManifest` | 清单 JSON 格式非法 |
| `EntryAssemblyNotFound` | 入口程序集路径无法解析 |
| `IncompatibleHostVersion` | 宿主版本不在插件声明范围内 |
| `PluginDisabled` | 插件在宿主禁用列表中 |
| `EntryAssemblyHashMismatch` | 入口程序集 SHA-256 不匹配 |
| `EntryAssemblyLoadFailed` | 程序集 `Assembly.LoadFrom` 失败 |

运维动作失败通过 `UnifiedPluginLifecycleResult.Status = "Failed"` 加 `ErrorMessage` 报告。

### 10.2 新增失败信号

治理增强后需要补充以下失败信号：

| 错误类型 | 含义 | 触发阶段 |
|----------|------|----------|
| `DependencyNotSatisfied` | 声明依赖的模块未通过发现阶段校验 | 宿主启动发现阶段 |
| `CircularDependency` | 插件之间存在循环依赖 | 宿主启动发现阶段 |
| `EnableDependencyMissing` | 启用时依赖的模块未处于已启用状态或不存在 | enable 动作 |
| `UninstallWhileEnabled` | 对处于启用态的插件执行卸载 | uninstall 动作 |
| `UninstallDependencyWarning` | 卸载的插件被其他已安装插件依赖（警告级） | uninstall 动作 |
| `RollbackNoTarget` | 没有可回滚的上一个版本 | rollback 动作 |

### 10.3 失败信号输出格式

所有失败信号统一采用如下日志格式，与现有格式一致：

```
Skipped plugin package {pluginDirectory}: {ErrorType} - {DetailMessage}
```

运维动作失败统一通过 `UnifiedPluginLifecycleResult` 返回：

```json
{
  "moduleId": "SomePlugin",
  "version": "1.0.0",
  "status": "Failed",
  "errorMessage": "Enable failed: dependency 'Authorization' is not enabled"
}
```

### 10.4 验证要求

治理增强实施后，以下验证应通过：

- 每个新增失败类型应有对应的单元测试覆盖。
- 宿主启动时依赖不满足的插件不进入程序集装载阶段。
- 对 Enabled 状态插件执行 uninstall 应被拒绝。
- rollback 到唯一版本时应返回明确失败。
- enable 时依赖模块未启用应返回明确失败。
- 卸载后 `packages/`、`state/`、`runtime/` 和 `public/` 下对应模块内容应不存在。

## 11. 风险与回滚思路

### 11.1 主要风险

| 风险 | 影响 | 控制方式 |
|------|------|----------|
| 依赖校验引入后，现有不声明依赖的内建插件不受影响，但新增正式依赖声明的插件可能因依赖排列不当被阻断 | 需要同步确认内建模块的 `dependencies` 字段准确性 | 实施前先审查所有现有 `plugin.json` 中的 `dependencies` 声明 |
| 卸载操作可能误删正在使用的插件包目录 | 数据丢失 | 卸载前强制要求 Disabled 状态；卸载动作应先校验状态再执行文件删除 |
| 回滚语义与插件数据库迁移不联动，可能导致回滚后数据 schema 不匹配 | 运行时数据访问异常 | 当前版本明确回滚只覆盖程序集和投影，不覆盖数据；数据回滚需要独立设计 |
| 状态机正式化后，现有状态记录可能不完全符合新状态定义 | 需要升级迁移步骤 | 实施时增加状态记录版本检测和向后兼容处理 |

### 11.2 回滚思路

- **设计层面**：本轮只产出设计文档，如设计方向有误，可直接修改或替换文档，不涉及代码回滚。
- **后续实施层面**：
  - 依赖校验可通过配置开关控制是否启用，允许渐进式启用。
  - 卸载功能新增独立命令，不修改现有 install / enable / disable / rollback 行为。
  - 状态机正式化应增加向后兼容读取逻辑，确保旧状态记录在不升级的情况下仍可被正确解析。

## 12. 分阶段实施建议

### Phase A：状态机正式化与依赖校验

最小交付：

- 在 `PluginScanErrorType` 中新增 `DependencyNotSatisfied` 和 `CircularDependency`。
- 在 `PluginDiscoveryService` 中增加依赖满足性校验步骤（兼容校验之后、程序集装载之前）。
- 在 `InstalledPluginRecord` 或配套类型中正式化状态定义，增加区分 `Installed` 与 `Disabled` 的标记。
- 补充对应单元测试。

验证：

- 后端构建通过。
- 依赖不满足的插件在宿主启动日志中出现 `DependencyNotSatisfied` 记录。
- 现有无依赖声明的内建模块行为不变。

### Phase B：卸载能力

最小交付：

- 在 `UnifiedPluginPackageOperations` 中实现 `Uninstall` 方法。
- 在 `PluginOps` CLI 中注册 `uninstall` 命令。
- 在管理脚本 `manage-unified-plugin.*` 中注册 `uninstall` 动作。
- 在插件运维中心 API 中增加 `DELETE /api/plugin-ops/modules/{moduleId}` 端点。
- 补充对应单元测试。

验证：

- 后端构建通过。
- 对 Disabled 状态插件执行 uninstall 后，安装目录、状态文件和运行时投影均已清理。
- 对 Enabled 状态插件执行 uninstall 被拒绝。

### Phase C：启用阶段依赖强校验

最小交付：

- 在 `enable` 动作中增加依赖满足性校验：目标版本声明的所有依赖模块必须处于已启用状态并能进入运行时投影视图。
- 在 `install` 动作中增加依赖预检警告。
- 补充对应单元测试。

验证：

- 启用时依赖模块未启用或不可装载的插件被拒绝。
- install 阶段只做依赖预检警告，安装仍然成功。
- enable 阶段才对未启用或不可装载的依赖做强校验并拒绝启用。

### Phase D：版本清理策略

最小交付：

- 实现可选的版本保留策略（默认保留最近 2 个版本）。
- 在 install 或 enable 时触发旧版本清理。
- 补充对应单元测试。

验证：

- 安装第 3 个版本后，最旧版本的包目录被自动清理。
- 当前启用版本和可回滚版本不被清理。

## 13. Host 与 Shell 接入边界声明

本节明确重申：治理增强不改变 Host 和 Shell 的接入边界。

### Host 侧

- Host 继续只负责启动、组合、模块加载和宿主级关切。
- 依赖校验和状态校验作为 `PluginDiscoveryService` 的内部步骤执行，不向 Host 入口暴露新的装配分支。
- 卸载、版本清理等运维动作通过 `PluginOps` CLI 或运维 API 执行，不在 Host 启动路径中引入卸载逻辑。

### Shell 侧

- Shell 继续只负责布局、Provider、菜单聚合、路由聚合和受控注册。
- 前端插件资源的投影视图变更（卸载后移除、版本切换后更新）由后端运行时视图物化驱动，Shell 不直接参与插件包管理。
- 插件运维中心页面作为 Shell 内的管理入口，复用现有受控注册机制，不新增模块私有路由注入。

### 外部插件开发

- 外部插件仍通过 `Contracts / SDK / templates / controlled entry points` 进行开发和接入。
- 治理增强不为任何内建或外部模块开放绕过清单声明、依赖校验或状态机约束的直接通道。
