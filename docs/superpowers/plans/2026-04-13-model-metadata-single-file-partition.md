# 单文件分区模型元数据实施计划（本阶段已收口）

## 1. 当前目标

本工作包不再重做设计讨论，默认以 [docs/designs/model-metadata-single-file-partition-design.md](/D:/Github/ICP-X/NewP/docs/designs/model-metadata-single-file-partition-design.md) 作为目标设计真源。

本阶段实施目标已完成收口：

- 先把状态文档与仓库真实代码对齐。
- 再在“模型元数据暂存”代码基础上，按最小增量继续补 projector、validator、schema builder。

当前结论是：

- 本阶段计划已经完成并可关闭。
- 后续若继续推进，应以新的阶段工作包处理 `ConfigCenter / ValueListItem` 契约对齐与更完整规则扩展。

本文件只描述当前仓库已实现内容、测试覆盖与后续顺序，不把未实现能力写成已完成。

## 2. 当前代码状态

### 2.1 已落地入口

当前仓库已经存在以下最小入口：

- [server/platform-modeling/metadata/ModelMetadataDocument.cs](/D:/Github/ICP-X/NewP/server/platform-modeling/metadata/ModelMetadataDocument.cs)
- [server/platform-modeling/metadata/JsonModelMetadataDocumentLoader.cs](/D:/Github/ICP-X/NewP/server/platform-modeling/metadata/JsonModelMetadataDocumentLoader.cs)
- [server/platform-modeling/metadata/JsonStorageTypeMapLoader.cs](/D:/Github/ICP-X/NewP/server/platform-modeling/metadata/JsonStorageTypeMapLoader.cs)
- [server/platform-modeling/metadata/storage-types.json](/D:/Github/ICP-X/NewP/server/platform-modeling/metadata/storage-types.json)
- [server/platform-modeling/Validation/ModelMetadataValidator.cs](/D:/Github/ICP-X/NewP/server/platform-modeling/Validation/ModelMetadataValidator.cs)
- [server/platform-modeling/Projection/ModelMetadataProjector.cs](/D:/Github/ICP-X/NewP/server/platform-modeling/Projection/ModelMetadataProjector.cs)
- [server/platform-modeling/Schema/ModelSchemaBuilder.cs](/D:/Github/ICP-X/NewP/server/platform-modeling/Schema/ModelSchemaBuilder.cs)
- [server/modules/ConfigCenter/metadata/models/ConfigItem.model.json](/D:/Github/ICP-X/NewP/server/modules/ConfigCenter/metadata/models/ConfigItem.model.json)
- [server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelMetadataProvider.cs](/D:/Github/ICP-X/NewP/server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelMetadataProvider.cs)

### 2.2 当前真实能力

当前“模型元数据暂存”代码已经具备的能力只有：

- `*.model.json` 根文档、模型、字段、`storage / layout / form / view` 的最小对象模型。
- JSON 文档加载。
- 平台 `storage-types.json` 加载。
- `validator` 的最小校验：
  - `moduleId` 必填
  - `model.name` 必填
  - `kind` 只允许 `Entity / Link`
  - `builtinFields` 必须和 `kind` 匹配
  - `Link` 必须声明 `linkedTypes`
  - 元数据字段名不能重复
  - 索引引用的字段必须存在
  - 固定字段和 builtin 字段不能在元数据里重写 `storage`
  - 在传入类型映射时，`storage.type` 必须存在于类型映射
- `projector` 的最小投影：
  - `Entity` 自动补 `EID`
  - `Entity` 自动补 `CreatedBy / CreatedAt / UpdatedBy / UpdatedAt`
  - `Entity` 可按 `builtinFields` 补 `ENAME / ECODE`
  - `Link` 自动补 `LID / PEID / CEID`
  - `Link` 自动补 `LCreatedBy / LCreatedAt / LUpdatedBy / LUpdatedAt`
  - `Link` 可按 `builtinFields` 补 `LSequence`
  - `Entity / Link` 自动补最小内建索引
  - 固定字段允许只在 authoring 中补 UI/schema 信息，不要求重复声明 `storage`
  - 自定义字段当前已补最小类型映射：
    - `string -> Text`
    - `text/json -> Description`
    - `bool -> Boolean`
    - `date/datetime -> DateTime`
    - `guid -> ObjectId`
  - provider store type 仅在传入类型映射和 provider 时按映射文件回填
- `schema builder` 的最小输出：
  - `ModelName`
  - 字段 `Name`
  - 字段 `UiType`
  - 字段 `LabelKey`
  - 字段 `Layout`
  - 字段 `Form`
  - 字段 `View`
  - 字段 `DataSource`
- `ConfigCenterModelMetadataProvider` 已能完成：
  - 元数据文件读取
  - 类型映射读取
  - 最小校验
  - `ConfigItem` 的最小投影

### 2.3 当前尚未实现的关键项

以下能力当前还没有实现，不能写成已完成：

- `Entity` 审计固定字段与 `ECODE` 的完整补入。
- `Link` 的审计固定字段与 `LSequence` 补入。
- 字段分区边界、`position` 唯一、section 连续性、`dataSource` 类型白名单等更完整的 validator 规则。
- `storage.type` 到 `FieldValueType` 的更完整映射，尤其是 `int / long / decimal` 等类型的明确语义。
- schema builder 的固定字段补全、section 列表输出与更完整默认值策略。
- `ValueListItem` 接入同一条模型元数据链路。
- migration 生成、EF Core 差异对比、数据库 smoke。

## 3. 当前测试覆盖

### 3.1 `tests/Modeling.Tests`

当前 `tests/Modeling.Tests` 已覆盖：

- `JsonModelMetadataDocumentLoaderTests`
  - JSON 文档可读取 `moduleId / model.name / kind`
- `StorageTypeMapLoaderTests`
  - `storage-types.json` 可读取 `datetime` 的三种 provider 映射
- `ModelMetadataValidatorTests`
  - `builtinFields` 与 `kind` 不匹配时报错
  - 未知 `storage.type` 报错
  - `Link` 缺少 `linkedTypes` 报错
  - 字段名重复时报错
  - 索引引用不存在字段时报错
  - builtin 字段重写 `storage` 时报错
- `ModelMetadataProjectorTests`
  - `Entity` 固定字段与 optional builtin 字段补入
  - `layout` 不影响数据库投影
  - `storage.type = string` 可通过类型映射得到 provider `storeType`
  - `bool / json / datetime / guid` 等类型的最小 `FieldValueType` 映射
  - `Link` 固定字段与 optional builtin 字段补入
  - storage 为空的 builtin 字段 authoring 不影响投影
  - 内建索引与自定义索引同时存在
- `ModelSchemaBuilderTests`
  - schema builder 当前验证 `UiType / LabelKey / Layout / Form / View / DataSource`
- `ModelDefinitionValidatorTests`
  - 只覆盖通用 `ModelDefinition` 的重复字段校验，不是单文件分区元数据专属测试

当前已执行结果：

- `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj -nr:false -v minimal`
- 通过 `16`，失败 `0`

### 3.2 `tests/ConfigCenter.Tests`

当前与本工作包直接相关的覆盖只有：

- `ConfigCenterModelMetadataProviderTests`
  - `LoadConfigItem()` 能从 `ConfigItem.model.json` 读取并投影最小定义
- `ConfigCenterModelDefinitionTests`
  - 验证 `ConfigCenterModelDefinitions.ConfigItem` 已通过 metadata provider 暴露
  - 仍在验证静态 `ValueListItem`

这意味着：

- 当前只证明 metadata provider 试点可用。
- 当前已经证明 `ConfigCenterModelDefinitions.ConfigItem` 通过 metadata provider 暴露。
- `ValueListItem` 仍完全不在本条元数据链路里。

当前已执行结果：

- `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "ConfigCenterModelDefinitionTests|ConfigCenterModelMetadataProviderTests" -nr:false -v minimal`
- 通过 `3`，失败 `0`

## 4. 文档与代码已确认的不一致点

本次核对已确认以下内容此前和当前代码不一致：

- 计划文档曾把 projector 写成已补齐更完整的固定字段和类型映射，但当前代码只覆盖 `EID / ENAME / LID / PEID / CEID` 与 `string / text` 两类最小映射。
- 现在上述三点已进入第一轮补齐，但仍未到设计目标的完整实现，尤其还缺 section、数据源白名单与更完整类型映射。
- 验证文档曾把“最小闭环已完成”表述得过宽，容易误解为 `ConfigCenter` 已正式切换到 metadata provider。
- [docs/current-status.md](/D:/Github/ICP-X/NewP/docs/current-status.md) 中此前同时存在“已完成最小闭环并完成文档收口”和“已启动并形成最小闭环”两种口径，且前者对实现成熟度描述偏满。

## 5. 后续实施顺序

当前优先序的前三项已完成第一轮最小实现，后续建议顺序为：

1. 评估是否需要把 `ConfigItem` 试点元数据扩到更接近真实业务字段，或继续保持最小样本冻结。
2. 评估 `ValueListItem` 是否值得纳入下一阶段元数据链路。
3. 若继续推进，再补：
   - validator 的 section、position、`dataSource.type` 规则
   - projector 的更完整 `storage.type` 映射
   - schema builder 的固定字段补全与 section 输出
4. 在上述边界稳定后，再决定是否进入 ConfigCenter 扩样本或更大范围模块接入

当前评估结论：

- 本轮先不继续扩 `ConfigCenter` 或 `ValueListItem`。
- 原因是 `ConfigCenterModelDefinitions.ConfigItem` 虽已切到 metadata provider，但当前试点文件仍只覆盖 `EID / ENAME / Value` 这条最小链路。
- 现有 `EfConfigRepository` 与 `ConfigItem` 领域模型仍按 `Id / Key / Type / ModuleId / DisplayName / ...` 契约工作。
- 若继续把 `ConfigCenter` 正式接到这条链路，需要先单独收口“试点元数据模型”和“现有仓储/领域模型”之间的契约差异，已经超出本轮最小收口范围。

## 6. 实施约束

- 所有实现必须以当前已合并的“模型元数据暂存”代码为准，不回到已回滚实现。
- 所有源码注释统一使用中文注释。
- 按 TDD 推进：先补失败测试，再写最小实现，再跑相关测试。
- 每完成一小步都运行相关测试，不做最后一次性集中验证。
