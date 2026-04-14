# 单文件分区模型元数据 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为平台现有 `ModelDefinition` 路线新增“单文件分区模型元数据” authoring 入口，先打通 `storage` 到 `ModelDefinition` 与 migration 的主链路，同时固化 `Entity / Link` 两类模型、固定字段、`linkedTypes`、`storage-types.json` 类型映射和后端 schema API 的边界。

**Architecture:** 本轮不替换现有代码内核，而是在 `server/platform-modeling` 中新增一层模型元数据文档、类型映射、校验器、投影器与 schema 输出契约。模型元数据文件固定采用 `storage / layout / form / view` 分区，字段级支持 `uiType / dataSource`，但只有 `storage`、模型类型、固定字段、`linkedTypes` 和 `indexes` 能影响数据库投影。业务前端不直接读取 `*.model.json`，统一通过后端 schema API 消费解析结果。首轮优先在 `ConfigCenter` 落地 `Entity` 试点，`Link` 与 `storage-types.json` 以建模基础能力先落地。

**Tech Stack:** .NET 10, xUnit, JSON, System.Text.Json, EF Core 现有持久化链路, PowerShell

---

## File Structure

### Modeling foundation

- Create: `server/platform-modeling/Metadata/ModelMetadataDocument.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataModel.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataField.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataStorageSection.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataLayoutSection.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataFormSection.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataFormModeRule.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataViewSection.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataSection.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataDataSource.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataIndex.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataLinkedType.cs`
- Create: `server/platform-modeling/Metadata/StorageTypeMapDocument.cs`
- Create: `server/platform-modeling/Metadata/IModelMetadataDocumentLoader.cs`
- Create: `server/platform-modeling/Metadata/JsonModelMetadataDocumentLoader.cs`
- Create: `server/platform-modeling/Metadata/IStorageTypeMapLoader.cs`
- Create: `server/platform-modeling/Metadata/JsonStorageTypeMapLoader.cs`
- Create: `server/platform-modeling/Validation/ModelMetadataValidator.cs`
- Create: `server/platform-modeling/Projection/ModelMetadataProjector.cs`
- Create: `server/platform-modeling/Schema/ModelSchemaDescriptor.cs`
- Create: `server/platform-modeling/Schema/ModelSchemaBuilder.cs`
- Create: `server/platform-modeling/metadata/storage-types.json`
- Modify: `server/platform-modeling/README.md`

### Tests

- Create: `tests/Modeling.Tests/ModelMetadataValidatorTests.cs`
- Create: `tests/Modeling.Tests/ModelMetadataProjectorTests.cs`
- Create: `tests/Modeling.Tests/StorageTypeMapLoaderTests.cs`
- Create: `tests/Modeling.Tests/ModelSchemaBuilderTests.cs`
- Modify: `tests/Modeling.Tests/Modeling.Tests.csproj`

### Pilot module

- Create: `server/modules/ConfigCenter/metadata/models/ConfigItem.model.json`
- Create: `server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelMetadataProvider.cs`
- Modify: `server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelDefinitions.cs`
- Modify: `server/modules/ConfigCenter/ConfigCenter.csproj`
- Modify: `tests/ConfigCenter.Tests/ConfigCenterModelDefinitionTests.cs`
- Create: `tests/ConfigCenter.Tests/ConfigCenterModelMetadataProviderTests.cs`

### Docs and verification

- Modify: `docs/designs/model-metadata-single-file-partition-design.md`
- Modify: `docs/superpowers/plans/2026-04-13-model-metadata-single-file-partition.md`
- Create: `docs/verification/model-metadata-single-file-partition-verification.md`
- Modify: `docs/current-status.md`

### Responsibility notes

- `Metadata/*` 只定义 authoring 文档模型与类型映射文档，不承载运行时流程。
- `Validation/ModelMetadataValidator.cs` 负责 `kind`、`builtinFields`、`linkedTypes`、布局、字段引用和结构完整性校验。
- `Projection/ModelMetadataProjector.cs` 只消费 `storage`、模型类型、固定字段、`linkedTypes` 和 `indexes`，明确忽略 `layout / form / view / uiType / dataSource` 对数据库结构的影响。
- `Schema/ModelSchemaBuilder.cs` 负责组装后端受控 schema API 输出，供业务前端消费。
- `storage-types.json` 是平台级类型能力与 provider 映射真源，不把数据库方言散落到各模块模型文件中。

## Task 1: 建立类型映射文件与基础文档模型

**Files:**
- Create: `server/platform-modeling/Metadata/StorageTypeMapDocument.cs`
- Create: `server/platform-modeling/Metadata/IStorageTypeMapLoader.cs`
- Create: `server/platform-modeling/Metadata/JsonStorageTypeMapLoader.cs`
- Create: `server/platform-modeling/metadata/storage-types.json`
- Create: `tests/Modeling.Tests/StorageTypeMapLoaderTests.cs`
- Modify: `tests/Modeling.Tests/Modeling.Tests.csproj`

- [ ] **Step 1: 先写 storage type map failing test**

```csharp
using ICP.Platform.Modeling.Metadata;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class StorageTypeMapLoaderTests
{
    [Fact]
    public void Load_ReturnsAllConfiguredProvidersForDatetime()
    {
        var loader = new JsonStorageTypeMapLoader();

        var document = loader.LoadFromFile("server/platform-modeling/metadata/storage-types.json");

        var datetime = Assert.Contains("datetime", document.Types.Keys);
        Assert.True(document.Types[datetime].Providers.ContainsKey("SqlServer"));
        Assert.True(document.Types[datetime].Providers.ContainsKey("Oracle"));
        Assert.True(document.Types[datetime].Providers.ContainsKey("MySql"));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj --filter StorageTypeMapLoaderTests -nr:false -v minimal`

Expected: FAIL，提示 loader 或文档类型尚不存在。

- [ ] **Step 3: 创建类型映射文档与 JSON 加载器**

最小模型需覆盖：

- `SchemaVersion`
- `Types`
- `Capabilities`
- `Providers`

- [ ] **Step 4: 创建 `storage-types.json` 首轮真源**

首轮至少覆盖：

- `string`
- `text`
- `int`
- `long`
- `decimal`
- `bool`
- `date`
- `datetime`
- `guid`
- `json`

每种类型必须同时提供：

- `SqlServer`
- `Oracle`
- `MySql`

- [ ] **Step 5: 回跑测试验证通过**

Run: `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj --filter StorageTypeMapLoaderTests -nr:false -v minimal`

Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add server/platform-modeling/Metadata server/platform-modeling/metadata/storage-types.json tests/Modeling.Tests
git commit -m "功能：新增模型存储类型映射文档"
```

## Task 2: 建立模型元数据文档模型与校验器

**Files:**
- Create: `server/platform-modeling/Metadata/ModelMetadataDocument.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataModel.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataField.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataStorageSection.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataLayoutSection.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataFormSection.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataFormModeRule.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataViewSection.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataSection.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataDataSource.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataIndex.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataLinkedType.cs`
- Create: `server/platform-modeling/Validation/ModelMetadataValidator.cs`
- Create: `tests/Modeling.Tests/ModelMetadataValidatorTests.cs`

- [ ] **Step 1: 先写校验 failing test**

```csharp
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Validation;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class ModelMetadataValidatorTests
{
    [Fact]
    public void Validate_ThrowsWhenEntityBuiltinFieldDoesNotMatchKind()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "ConfigCenter",
            Model = new ModelMetadataModel
            {
                Name = "ConfigItem",
                Kind = "Entity",
                BuiltinFields = ["LSequence"],
                Fields = []
            }
        };

        var validator = new ModelMetadataValidator();

        var ex = Assert.Throws<InvalidOperationException>(() => validator.Validate(document));
        Assert.Contains("builtin", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj --filter ModelMetadataValidatorTests -nr:false -v minimal`

Expected: FAIL

- [ ] **Step 3: 创建元数据文档模型**

最小属性需覆盖：

- `Model.Name`
- `Model.Kind`
- `Model.BuiltinFields`
- `Model.Sections`
- `Model.LinkedTypes`
- `Field.Storage`
- `Field.Layout`
- `Field.Form`
- `Field.View`
- `Field.UiType`
- `Field.DataSource`

- [ ] **Step 4: 实现最小校验器**

至少校验：

- `ModuleId / Model.Name / Kind` 必填
- `Kind` 仅允许 `Entity / Link`
- `builtinFields` 与 `kind` 匹配
- `Link` 必须定义至少一条 `linkedTypes`
- `position` 唯一
- section 引用存在且字段连续
- `storage.type` 在 `storage-types.json` 中存在
- `dataSource.type` 仅允许 `enum / model`
- `reference` 首轮只允许单选语义

- [ ] **Step 5: 补充固定字段、索引、分区边界校验**

包括：

- `Entity` 固定字段与 `Link` 固定字段自动补全前的规则约束
- 不允许 UI 专属属性进入 `storage`
- 索引字段引用必须存在

- [ ] **Step 6: 回跑测试验证通过**

Run: `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj --filter ModelMetadataValidatorTests -nr:false -v minimal`

Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add server/platform-modeling/Metadata server/platform-modeling/Validation tests/Modeling.Tests
git commit -m "功能：新增模型元数据文档与校验器"
```

## Task 3: 打通 storage 到 ModelDefinition 的投影器

**Files:**
- Create: `server/platform-modeling/Metadata/IModelMetadataDocumentLoader.cs`
- Create: `server/platform-modeling/Metadata/JsonModelMetadataDocumentLoader.cs`
- Create: `server/platform-modeling/Projection/ModelMetadataProjector.cs`
- Create: `tests/Modeling.Tests/ModelMetadataProjectorTests.cs`
- Modify: `server/platform-modeling/README.md`

- [ ] **Step 1: 先写 projector failing test**

```csharp
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class ModelMetadataProjectorTests
{
    [Fact]
    public void Project_AddsEntityBuiltinFieldsAndIgnoresLayout()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "ConfigCenter",
            Model = new ModelMetadataModel
            {
                Name = "ConfigItem",
                Kind = "Entity",
                BuiltinFields = ["ENAME"],
                Fields =
                [
                    new ModelMetadataField
                    {
                        Name = "Value",
                        Storage = new ModelMetadataStorageSection
                        {
                            Type = "text",
                            Nullable = true
                        },
                        Layout = new ModelMetadataLayoutSection
                        {
                            Position = "010",
                            Width = 100
                        }
                    }
                ]
            }
        };

        var definition = new ModelMetadataProjector().Project(document);

        Assert.Contains(definition.Fields, static x => x.Name == "EID");
        Assert.Contains(definition.Fields, static x => x.Name == "ENAME");
        Assert.Contains(definition.Fields, static x => x.Name == "Value");
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj --filter ModelMetadataProjectorTests -nr:false -v minimal`

Expected: FAIL

- [ ] **Step 3: 实现 JSON 模型元数据加载器**

- [ ] **Step 4: 实现投影器**

投影器至少要做到：

- 根据 `kind` 自动补入固定字段
- 根据 `builtinFields` 自动补入可选固定字段
- 只消费 `storage`、`linkedTypes`、`indexes`
- 使用 `storage-types.json` 完成平台类型到后端定义的映射
- 自动补入内建索引：
  - `Entity`：`EID` 唯一索引
  - `Link`：`LID` 唯一索引、`PEID` 普通索引、`CEID` 普通索引

- [ ] **Step 5: 在 README 中固定边界说明**

至少写清：

- `storage` 是唯一允许投影到 `ModelDefinition` 与 migration 的字段分区
- `layout / form / view / uiType / dataSource` 不参与数据库投影
- `builtinFields` 与固定字段由内核收口
- `Link` 的 `linkedTypes` 只表达类型组合，不表达删除策略

- [ ] **Step 6: 回跑测试验证通过**

Run: `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj --filter "ModelMetadataProjectorTests|ModelMetadataValidatorTests|StorageTypeMapLoaderTests" -nr:false -v minimal`

Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add server/platform-modeling tests/Modeling.Tests
git commit -m "功能：新增模型元数据投影主链路"
```

## Task 4: 建立后端 schema API 输出契约

**Files:**
- Create: `server/platform-modeling/Schema/ModelSchemaDescriptor.cs`
- Create: `server/platform-modeling/Schema/ModelSchemaBuilder.cs`
- Create: `tests/Modeling.Tests/ModelSchemaBuilderTests.cs`

- [ ] **Step 1: 先写 schema builder failing test**

```csharp
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Schema;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class ModelSchemaBuilderTests
{
    [Fact]
    public void Build_ReturnsResolvedUiTypeAndFormModes()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "ConfigCenter",
            Model = new ModelMetadataModel
            {
                Name = "ConfigItem",
                Kind = "Entity",
                BuiltinFields = ["ENAME"],
                Fields =
                [
                    new ModelMetadataField
                    {
                        Name = "ENAME",
                        UiType = "text",
                        Layout = new ModelMetadataLayoutSection { Position = "010", Width = 50 },
                        Form = new ModelMetadataFormSection
                        {
                            Create = new ModelMetadataFormModeRule { Mode = "editable" },
                            Edit = new ModelMetadataFormModeRule { Mode = "editable" }
                        },
                        View = new ModelMetadataViewSection { Visible = true }
                    }
                ]
            }
        };

        var schema = new ModelSchemaBuilder().Build(document);

        Assert.Equal("ConfigItem", schema.ModelName);
        Assert.Contains(schema.Fields, static x => x.Name == "ENAME" && x.UiType == "text");
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj --filter ModelSchemaBuilderTests -nr:false -v minimal`

Expected: FAIL

- [ ] **Step 3: 实现 schema builder**

至少覆盖：

- 固定字段与 `builtinFields` 补全后的 UI/schema 结果
- 默认 `uiType` 推导
- `form.create.mode / form.edit.mode`
- `view.visible`
- `layout / sections`
- `dataSource`
- 多语言 key 生成规则

- [ ] **Step 4: 回跑测试验证通过**

Run: `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj --filter "ModelSchemaBuilderTests|ModelMetadataValidatorTests|ModelMetadataProjectorTests" -nr:false -v minimal`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add server/platform-modeling/Schema tests/Modeling.Tests
git commit -m "功能：新增模型 schema 输出契约"
```

## Task 5: 用 ConfigCenter 试点元数据文件并保持现有测试口径

**Files:**
- Create: `server/modules/ConfigCenter/metadata/models/ConfigItem.model.json`
- Create: `server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelMetadataProvider.cs`
- Modify: `server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelDefinitions.cs`
- Modify: `server/modules/ConfigCenter/ConfigCenter.csproj`
- Create: `tests/ConfigCenter.Tests/ConfigCenterModelMetadataProviderTests.cs`
- Modify: `tests/ConfigCenter.Tests/ConfigCenterModelDefinitionTests.cs`

- [ ] **Step 1: 先写试点模块 failing test**

```csharp
using ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;
using Xunit;

namespace ICP.Platform.Modules.ConfigCenter.Tests;

public class ConfigCenterModelMetadataProviderTests
{
    [Fact]
    public void LoadConfigItem_ReturnsDefinitionFromMetadataFile()
    {
        var provider = new ConfigCenterModelMetadataProvider();

        var definition = provider.LoadConfigItem();

        Assert.Equal("ConfigItem", definition.Name);
        Assert.Contains(definition.Fields, static x => x.Name == "EID");
        Assert.Contains(definition.Fields, static x => x.Name == "ENAME");
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "ConfigCenterModelDefinitionTests|ConfigCenterModelMetadataProviderTests" -nr:false -v minimal`

Expected: FAIL

- [ ] **Step 3: 创建试点元数据文件**

要求至少体现：

- `kind = Entity`
- `builtinFields = ["ENAME", "ECODE"]`
- 一个真实业务字段示例
- `layout`
- `form.create.mode / form.edit.mode`
- `view.visible`
- 业务唯一索引

- [ ] **Step 4: 实现试点 provider**

provider 负责：

- 加载模型元数据文件
- 加载 `storage-types.json`
- 执行校验
- 投影为 `ModelDefinition`

- [ ] **Step 5: 将现有定义类改为走 provider**

- [ ] **Step 6: 回跑模块测试验证通过**

Run:
- `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "ConfigCenterModelDefinitionTests|ConfigCenterModelMetadataProviderTests" -nr:false -v minimal`
- `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj -nr:false -v minimal`

Expected:
- `ConfigCenter` 相关测试通过
- `Modeling.Tests` 全绿

- [ ] **Step 7: Commit**

```bash
git add server/modules/ConfigCenter tests/ConfigCenter.Tests
git commit -m "功能：用 ConfigCenter 试点单文件分区模型元数据"
```

## Task 6: 补验证文档与当前状态

**Files:**
- Create: `docs/verification/model-metadata-single-file-partition-verification.md`
- Modify: `docs/current-status.md`
- Modify: `docs/designs/model-metadata-single-file-partition-design.md`
- Modify: `docs/superpowers/plans/2026-04-13-model-metadata-single-file-partition.md`

- [ ] **Step 1: 运行最小验证集**

Run:
- `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj -nr:false -v minimal`
- `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "ConfigCenterModelDefinitionTests|ConfigCenterModelMetadataProviderTests" -nr:false -v minimal`

Expected:
- 建模测试通过
- 试点模块测试通过

- [ ] **Step 2: 补验证文档**

至少记录：

- 类型映射文件加载与校验
- `storage` 到 `ModelDefinition` 投影
- `ConfigCenter` 试点模块加载结果
- `layout / form / view / uiType / dataSource` 不影响数据库结构
- 后端 schema API 输出契约验证

- [ ] **Step 3: 更新当前状态**

至少写清：

- 已完成 `storage` 主链路试点
- 已固化 `Entity / Link / builtinFields / linkedTypes / storage-types.json`
- 业务前端统一走后端 schema API 的边界已确定

- [ ] **Step 4: Commit**

```bash
git add docs/verification docs/current-status.md docs/designs/model-metadata-single-file-partition-design.md docs/superpowers/plans/2026-04-13-model-metadata-single-file-partition.md
git commit -m "文档：收口单文件分区模型元数据实施计划"
```

## Self-Review Checklist

- [ ] 规格覆盖：
  - 类型映射与 provider 默认值：Task 1
  - 单文件模型文档与校验：Task 2
  - `storage` 到 `ModelDefinition` 投影：Task 3
  - 后端 schema API 输出契约：Task 4
  - `ConfigCenter` 首个试点：Task 5
  - 文档与验证收口：Task 6
- [ ] 占位词扫描：计划中未使用 `TODO`、`TBD`、`implement later` 等占位描述。
- [ ] 类型一致性：
  - 文档模型统一使用 `ModelMetadata*`
  - 类型映射统一使用 `StorageTypeMap*`
  - 投影入口统一使用 `ModelMetadataProjector`
  - schema 输出统一使用 `ModelSchemaBuilder`
  - 试点模块统一使用 `ConfigCenterModelMetadataProvider`
