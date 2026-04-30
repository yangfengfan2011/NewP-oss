# 模型元数据协议字段对齐设计

## 1. 背景

当前 `ModelMetadataProjector` 已支持平台协议字段：

- 实体固定主键：`EID`
- 可选显示名：`ENAME`
- 可选编码：`ECODE`

但多数 `modules/**/metadata/models/*.model.json` 仍在 authoring 源中显式声明普通字段 `Name`、`Code` 或 `DisplayName`。这会导致开发态结构同步工具从元数据真源投影出的目标列仍是 `Name / Code / DisplayName`，而不是已经定义的平台协议字段 `ENAME / ECODE`。

这不是 SQL 生成器问题，而是 authoring 真源尚未对齐协议字段。

## 2. 目标

- 对齐首批内建模型元数据中的通用“编码/名称”语义到 `ECODE / ENAME`。
- 让开发态结构同步工具预览 SQL 时使用协议列名。
- 领域对象、服务和外部 DTO 仍可继续使用业务友好的 `Code / Name / DisplayName`，映射收口在 DTO 生成器和 EF 仓储中。
- Host、schema diff、SQL apply 仍只消费投影后的统一模型定义。
- 保持变更聚焦，不在本批次处理正式 migration 重建。

## 3. 非目标

- 不批量重写 EF migration 历史文件。
- 不改变业务服务层领域对象命名。
- 不把 `storage.columnName` 作为第二套现行物理名引入。
- 不把所有业务字段都强制改成协议字段；只有通用编码/名称语义对齐到协议字段。

## 4. 对齐规则

### 4.1 实体通用编码/名称

当模型中存在通用对象编码和显示名称时：

- `Code` -> `ECODE`
- `Name` 或 `DisplayName` -> `ENAME`

实现方式：

- 在模型上声明 `builtinFields: ["ENAME", "ECODE"]`
- 从 `fields` 中移除显式 `Code / Name / DisplayName`
- 索引字段改为引用 `ECODE`
- 页面 section 字段改为引用 `ENAME / ECODE`

### 4.2 只存在名称的实体

当模型只需要通用显示名、不需要通用编码时：

- 声明 `builtinFields: ["ENAME"]`
- 从 `fields` 中移除显式 `Name / DisplayName`

### 4.3 领域与 DTO 映射

- `ModelDtoGenerator` 已负责把 `EID -> Id`、`ECODE -> Code`、`ENAME -> DisplayName`。
- 为降低一次性 API 断裂，生成 DTO 对 `ENAME` 额外提供 `Name` 兼容别名；该别名只读写 `DisplayName`，不回流为元数据字段或数据库列。
- EF 仓储从字典实体读写时，应使用 `ECODE / ENAME`，再映射回领域模型的 `Code / Name / DisplayName`。

## 5. 范围

本批次覆盖当前已纳入 `PlatformPersistenceModelCatalog` 的内建模型：

- `Authorization`
  - `Role`: `Code / Name` -> `ECODE / ENAME`
  - `Permission`: `Code / DisplayName` -> `ECODE / ENAME`
- `ConfigCenter`
  - `ValueListItem`: `Code / Name` -> `ECODE / ENAME`
  - `ConfigItem`: 已有 `ENAME`，本批次不强行把 `Key` 改为 `ECODE`
- `Organization`
  - `Company`: `Code / Name` -> `ECODE / ENAME`
  - `Department`: `Code / Name` -> `ECODE / ENAME`
  - `Position`: `Code / Name` -> `ECODE / ENAME`
  - `Person`: `Name` -> `ENAME`；`PersonNo` 暂作为业务字段保留，不在本批次改为 `ECODE`

关系表/关系实体不引入 `ENAME / ECODE`。

## 6. 风险与回滚

风险：

- schema sync 会将旧 `Name / Code / DisplayName` 识别为删除列、新增协议列，属于高风险计划。
- 部分 API DTO 字段可能从 `Name` 变为 `DisplayName`，需要服务层最小兼容或前端同步。

回滚：

- 恢复对应 `*.model.json` 中的显式 `Code / Name / DisplayName` 字段和索引引用。
- 恢复 EF 仓储字典键读写。
- 重新运行 DTO 同步工具恢复生成 DTO。

## 7. 一致性 Review

- 设计符合“元数据只声明是什么、归谁、允许什么”的边界；执行和映射逻辑仍在代码内核。
- 不引入 `columnName/tableName` 第二套现行名称。
- 只触及当前模型元数据协议字段对齐，不混入新的业务功能。
- 文档位于 `docs/designs/`，满足正式实施前详细设计要求。
