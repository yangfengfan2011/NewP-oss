# 开发态数据库结构同步工具高风险确认执行设计

## 1. 背景

当前 `dev-schema-sync` 工具已经支持 `--sql` 输出 SQL 预览、`--apply` 执行安全结构变更，并且执行器内部已经预留整体确认与 Confirm 风险确认两个入口。

实际使用时存在一个断点：当计划包含 `Confirm` 级操作时，`DevelopmentSchemaSyncCoordinator.PrepareAsync` 会提前返回 `ConfirmOperationsDetected`，导致 CLI 即使使用 `--sql --apply` 也只能看到 SQL，无法在查看 SQL 后进行显式确认执行。

这影响本地开发库快速对齐模型的效率，但不能弱化 Host 开发启动链路对高风险变更的保护。

## 2. 目标

- CLI 文本模式下允许用户通过 `--sql --apply` 查看 SQL 后确认执行包含 `Confirm` 操作的计划。
- 高风险操作必须有二次确认，且确认文本要比普通 `y` 更强，避免误触。
- Host 开发启动自动同步继续阻断 `Confirm` 操作，不得因为本次 CLI 能力而自动执行高风险变更。
- JSON 模式继续只用于计划消费，不支持交互执行。
- 不生成 migration，不修改 `ModelSnapshot`，不写 `__EFMigrationsHistory`。

## 3. 非目标

- 不改变正式 EF Core migration 发布链路。
- 不放开 Host 自动启动执行高风险变更。
- 不新增 MySQL / Oracle 执行支持。
- 不引入批量脚本绕过确认。
- 不处理复杂数据迁移、数据修复或删表治理。

## 4. 设计方案

### 4.1 共享内核选项

在 `DevelopmentSchemaSyncOptions` 中新增两个受控输入：

- `AllowConfirmOperations`
  - 默认 `false`。
  - 为 `false` 时，`PrepareAsync` 保持现状：检测到 `Confirm` 操作即返回 `ConfirmOperationsDetected`。
  - CLI 文本 `--apply` 路径设置为 `true`。
  - Host 开发启动路径保持 `false`。

- `ConfirmRisk`
  - 类型为 `Func<IReadOnlyList<SchemaOperation>, bool>?`。
  - 仅当计划包含 `Confirm` 操作并进入执行阶段时使用。
  - 未提供时默认拒绝，避免非交互调用误执行风险。

### 4.2 CLI 行为

CLI 文本模式行为如下：

1. `--sql`：输出计划和 SQL，不执行。
2. `--apply`：输出计划和 SQL，先进行整体确认；`--sql --apply` 是推荐写法，`--apply` 本身也会强制输出 SQL Preview。
3. 若计划包含 `Confirm` 操作，整体确认通过后继续输出高风险摘要，并要求输入固定确认短语 `APPLY RISK`。
4. 用户拒绝任一确认时返回取消，不执行任何 SQL。
5. `--json --apply` 继续返回 `JsonApplyNotSupported`。

整体确认当前接受 `y` 或 `yes`；高风险确认只接受精确的 `APPLY RISK`，不接受 `y` / `yes`。

### 4.3 Host 行为

Host 的 `BuildDevelopmentSchemaSyncOptions` 不设置 `AllowConfirmOperations` 和 `ConfirmRisk`，因此仍保持：

- Safe 计划可自动执行；
- Confirm 计划在 `PrepareAsync` 阶段阻断启动；
- 非开发环境或非 SqlServer provider 继续回落到 migration runner。

### 4.4 失败信号

- 用户拒绝整体确认：`ApplyCancelled`，退出码 `4`。
- 用户拒绝风险确认：`ApplyCancelled`，退出码 `4`。
- SQL 缺失或执行失败：`ApplyFailed`，退出码 `4`。
- Host 自动路径遇到高风险：`ConfirmOperationsDetected`，退出码 `3`。

## 5. 测试方案

- 增加 coordinator 测试：`AllowConfirmOperations=true` 时，Confirm 计划不在 `PrepareAsync` 阶段阻断，并能进入执行确认。
- 保留/调整现有 coordinator 测试：默认行为仍阻断 Confirm 计划，保护 Host 和非交互调用。
- 增加 CLI 测试：
  - `--sql --apply` 在 Confirm 计划下先输出 SQL，再整体确认，再风险确认；
  - 输入 `y` 和 `APPLY RISK` 后执行；
  - 风险确认拒绝时不执行。
- 运行 `tests/DevSchemaSync.Tests` 和相关持久化测试。

## 6. 回滚思路

- 移除 `DevelopmentSchemaSyncOptions` 中新增的 `AllowConfirmOperations` 与 `ConfirmRisk`。
- 恢复 coordinator 对 `Confirm` 操作的无条件阻断。
- 删除 CLI 风险确认提示与对应测试。

## 7. 一致性 Review

- 设计边界：只改变 CLI 交互执行路径，不改变正式 migration 链路。
- Host 边界：Host 自动启动不启用 `AllowConfirmOperations`，仍阻断高风险。
- 元数据边界：不新增或改变 `model` authoring 语义。
- 执行边界：执行仍收口在 `SchemaApplyExecutor`，CLI 只提供确认策略。
- 目录位置：文档位于 `docs/designs/`，符合项目详细设计文档要求。
