# 单文件分区模型元数据验证记录

## 1. 验证范围

本次验证只覆盖当前仓库已经真实存在的“模型元数据暂存”链路，不把设计目标中的远期能力纳入已验证范围。

本次实际验证范围包括：

- 模型元数据 JSON 文档加载
- 平台 `storage-types.json` 类型映射加载
- `ModelMetadataValidator` 当前已实现的最小语义校验
- `ModelMetadataProjector` 当前已实现的最小数据库投影
- `ModelSchemaBuilder` 当前已实现的最小字段级 schema 输出
- `ConfigCenterModelMetadataProvider` 对 `ConfigItem.model.json` 的试点读取、校验与投影
- `ConfigCenter` 现有静态模型定义未被试点代码破坏

不在本次验证范围内的内容：

- validator 的 section、position、`dataSource.type` 等更完整核心契约校验
- projector 的更完整 `storage.type` 语义映射
- schema builder 的固定字段补全、section 列表与更完整默认值输出
- `ConfigCenterModelDefinitions.ConfigItem` 正式切换为 metadata provider
- `ValueListItem` 接入同一条元数据链路
- migration 生成
- EF Core 模型差异对比
- 数据库 smoke
- 前端模块级真实接入

## 2. 验证对象

本轮实际涉及的关键文件如下：

- [server/platform-modeling/metadata/ModelMetadataDocument.cs](/D:/Github/ICP-X/NewP/server/platform-modeling/metadata/ModelMetadataDocument.cs)
- [server/platform-modeling/metadata/JsonModelMetadataDocumentLoader.cs](/D:/Github/ICP-X/NewP/server/platform-modeling/metadata/JsonModelMetadataDocumentLoader.cs)
- [server/platform-modeling/metadata/JsonStorageTypeMapLoader.cs](/D:/Github/ICP-X/NewP/server/platform-modeling/metadata/JsonStorageTypeMapLoader.cs)
- [server/platform-modeling/metadata/storage-types.json](/D:/Github/ICP-X/NewP/server/platform-modeling/metadata/storage-types.json)
- [server/platform-modeling/Validation/ModelMetadataValidator.cs](/D:/Github/ICP-X/NewP/server/platform-modeling/Validation/ModelMetadataValidator.cs)
- [server/platform-modeling/Projection/ModelMetadataProjector.cs](/D:/Github/ICP-X/NewP/server/platform-modeling/Projection/ModelMetadataProjector.cs)
- [server/platform-modeling/Schema/ModelSchemaBuilder.cs](/D:/Github/ICP-X/NewP/server/platform-modeling/Schema/ModelSchemaBuilder.cs)
- [server/modules/ConfigCenter/metadata/models/ConfigItem.model.json](/D:/Github/ICP-X/NewP/server/modules/ConfigCenter/metadata/models/ConfigItem.model.json)
- [server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelMetadataProvider.cs](/D:/Github/ICP-X/NewP/server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelMetadataProvider.cs)

## 3. 已执行命令

### 3.1 建模侧定向测试

```powershell
dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj -nr:false -v minimal
```

实际结果：

- 通过 `16` 个测试
- 失败 `0`
- 跳过 `0`

### 3.2 ConfigCenter 定向测试

```powershell
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "ConfigCenterModelDefinitionTests|ConfigCenterModelMetadataProviderTests" -nr:false -v minimal
```

实际结果：

- 通过 `3` 个测试
- 失败 `0`
- 跳过 `0`

## 4. 当前测试覆盖结论

### 4.1 `tests/Modeling.Tests` 当前证明了什么

当前测试已经证明：

1. `JsonModelMetadataDocumentLoader` 能读取最小模型元数据文档。
2. `JsonStorageTypeMapLoader` 能读取 `storage-types.json` 中的 provider 映射。
3. `ModelMetadataValidator` 当前至少会拦截：
   - `builtinFields` 与 `kind` 不匹配
   - 未知 `storage.type`
   - `Link` 缺失 `linkedTypes`
   - 元数据字段名重复
   - 索引引用不存在字段
   - 固定字段或 builtin 字段重写 `storage`
4. `ModelMetadataProjector` 当前至少会：
   - 给 `Entity` 补 `EID`
   - 给 `Entity` 补 `CreatedBy / CreatedAt / UpdatedBy / UpdatedAt`
   - 在声明 builtin 时补 `ENAME / ECODE`
   - 给 `Link` 补 `LID / PEID / CEID`
   - 给 `Link` 补 `LCreatedBy / LCreatedAt / LUpdatedBy / LUpdatedAt`
   - 在声明 builtin 时补 `LSequence`
   - 忽略 `layout` 对数据库投影的影响
   - 合并内建索引与自定义索引
   - 在已知映射时输出 provider `storeType`
   - 对 `bool / json / date / datetime / guid` 做最小 `FieldValueType` 映射
   - 允许 builtin 字段只声明 UI/schema 信息而不重写 `storage`
5. `ModelSchemaBuilder` 当前能输出字段级 `UiType / LabelKey / Layout / Form / View / DataSource`。

### 4.2 `tests/ConfigCenter.Tests` 当前证明了什么

当前定向测试只证明：

1. `ConfigCenterModelMetadataProvider` 可以从 `ConfigItem.model.json` 读取并投影最小 `ConfigItem` 定义。
2. `ConfigCenterModelDefinitions.ConfigItem` 已通过 metadata provider 对外暴露且当前仍然有效。
3. 现有静态 `ValueListItem` 仍然有效。

当前定向测试没有证明：

- `ConfigCenter` 除 `ConfigItem` 之外的其他模型也已经切到 metadata provider。
- `ValueListItem` 已经有对应的 `*.model.json`。
- `layout / form / view / dataSource` 的输出契约已经可供真实前端消费。

## 5. 当前试点样本口径

当前试点文件为：

- [server/modules/ConfigCenter/metadata/models/ConfigItem.model.json](/D:/Github/ICP-X/NewP/server/modules/ConfigCenter/metadata/models/ConfigItem.model.json)

其当前最小样本口径为：

- `model.name = ConfigItem`
- `kind = Entity`
- `builtinFields = ["ENAME"]`
- 自定义字段只包含 `Value`
- `Value.storage.type = text`
- `Value.storage.nullable = true`

## 6. 已知限制

- 当前 projector 还没有补齐设计里要求的全部 `storage.type` 语义映射，`int / long / decimal` 等类型仍未形成明确语义。
- 当前 validator 还没有 section 连续性、`position` 唯一与 `dataSource.type` 白名单等更完整规则。
- 当前 schema builder 虽已输出字段级 `layout / form / view / dataSource`，但还没有固定字段补全与 section 列表输出。
- `ConfigCenterModelMetadataProvider` 已可用，且 `ConfigCenterModelDefinitions.ConfigItem` 已经切到 provider 输出。
- `ValueListItem` 仍未纳入这条模型元数据链路。
- 当前全量 `ConfigCenter.Tests` 仍不能视为通过：`EfConfigRepositoryTests.GetByKeyAsync_ReturnsTenantAndEnvironmentMatchFirst` 会因 `ConfigItem` 试点元数据只覆盖 `EID / ENAME / Value`，而现有仓储仍按 `Id / Key / Type / ModuleId / DisplayName / ...` 契约读写，导致 EF 共享实体主键字段不一致而失败。

## 7. 验证结论

本轮可以确认：

- 当前仓库已经存在一条可运行、可测试的“模型元数据暂存”最小链路。
- 这条链路目前已经推进到“文档加载 -> 第一轮核心校验 -> 第一轮 projector 补齐 -> 最小 schema 输出 -> ConfigCenter 试点 provider -> 定向测试”的阶段。
- 它还不能被描述成“设计目标已完成”或“ConfigCenter 已全面切换到单文件分区模型元数据”。

因此当前正确口径应为：

- 最小暂存链路已存在并已通过定向测试。
- projector、validator、schema builder 的第一轮最小补齐已完成。
- `ConfigCenter` 或 `ValueListItem` 若要继续推进，应另起下一阶段工作包先处理试点元数据与现有模块契约不一致的问题。
