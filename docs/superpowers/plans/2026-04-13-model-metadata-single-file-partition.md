# 单文件分区模型元数据 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为平台现有 `ModelDefinition` 路线新增“单文件分区模型元数据” authoring 入口，先打通 `storage` 分区到 `ModelDefinition` 与 migration 的主链路，并用 `ConfigCenter` 作为首个试点模块验证可行性。

**Architecture:** 本轮不替换现有代码内核，而是在 `server/platform-modeling` 中新增一层模型元数据文档、校验器和投影器。元数据文件固定采用 `storage / form / view` 分区，并为字段增加稳定的 `uiType / uiOptions` 声明；其中只有 `storage` 能投影为现有 `ModelDefinition`，`form / view / uiType / uiOptions` 先纳入契约和校验，但首轮不进入前端消费实现。模块侧采用“元数据文件作为 authoring 源 + 代码投影为执行期强类型对象”的并存策略，优先在 `ConfigCenter` 落地。

**Tech Stack:** .NET 10, xUnit, JSON, System.Text.Json, EF Core 现有持久化链路, PowerShell

---

## File Structure

### Modeling foundation

- Create: `server/platform-modeling/Metadata/ModelMetadataDocument.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataField.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataStorageSection.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataFormSection.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataViewSection.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataUiOptionsSection.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataIndex.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataScenarioRule.cs`
- Create: `server/platform-modeling/Metadata/IModelMetadataDocumentLoader.cs`
- Create: `server/platform-modeling/Metadata/JsonModelMetadataDocumentLoader.cs`
- Create: `server/platform-modeling/Projection/ModelMetadataProjector.cs`
- Create: `server/platform-modeling/Validation/ModelMetadataValidator.cs`
- Modify: `server/platform-modeling/README.md`

### Tests

- Create: `tests/Modeling.Tests/ModelMetadataValidatorTests.cs`
- Create: `tests/Modeling.Tests/ModelMetadataProjectorTests.cs`
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
- Create: `docs/verification/model-metadata-single-file-partition-verification.md`
- Modify: `docs/current-status.md`

### Responsibility notes

- `Metadata/*` 只定义 authoring 文档模型，不承载运行时执行逻辑。
- `Validation/ModelMetadataValidator.cs` 负责分区边界、字段引用和结构完整性校验。
- `Projection/ModelMetadataProjector.cs` 只消费 `storage` 分区，明确忽略 `form / view / uiType / uiOptions`。
- `ConfigCenterModelMetadataProvider.cs` 负责从模块元数据文件加载并投影，不把文件读取逻辑散落回模块定义类。

### Task 1: 建立元数据文档模型与分区校验器

**Files:**
- Create: `server/platform-modeling/Metadata/ModelMetadataDocument.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataField.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataStorageSection.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataFormSection.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataViewSection.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataUiOptionsSection.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataIndex.cs`
- Create: `server/platform-modeling/Metadata/ModelMetadataScenarioRule.cs`
- Create: `server/platform-modeling/Validation/ModelMetadataValidator.cs`
- Create: `tests/Modeling.Tests/ModelMetadataValidatorTests.cs`
- Modify: `tests/Modeling.Tests/Modeling.Tests.csproj`

- [ ] **Step 1: 先写分区校验 failing test**

```csharp
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Validation;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class ModelMetadataValidatorTests
{
    [Fact]
    public void Validate_ThrowsWhenStorageSectionMissing()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "ConfigCenter",
            Model = new ModelMetadataModel
            {
                Name = "ConfigItem",
                Table = "config_items",
                Kind = "Entity",
                Fields =
                [
                    new ModelMetadataField
                    {
                        Name = "Key",
                        Form = new ModelMetadataFormSection
                        {
                            Create = new ModelMetadataScenarioRule { Included = true, Editable = true }
                        }
                    }
                ]
            }
        };

        var validator = new ModelMetadataValidator();

        var ex = Assert.Throws<InvalidOperationException>(() => validator.Validate(document));
        Assert.Contains("storage", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj --filter ModelMetadataValidatorTests -nr:false -v minimal`

Expected: FAIL，提示 `ModelMetadataDocument`、`ModelMetadataValidator` 等类型尚不存在。

- [ ] **Step 3: 创建元数据文档模型**

```csharp
namespace ICP.Platform.Modeling.Metadata;

public sealed class ModelMetadataDocument
{
    public string SchemaVersion { get; init; } = "1.0";
    public string ModuleId { get; init; } = string.Empty;
    public required ModelMetadataModel Model { get; init; }
}

public sealed class ModelMetadataModel
{
    public string Name { get; init; } = string.Empty;
    public string Table { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public List<ModelMetadataField> Fields { get; init; } = [];
    public List<ModelMetadataIndex> Indexes { get; init; } = [];
}
```

```csharp
namespace ICP.Platform.Modeling.Metadata;

public sealed class ModelMetadataField
{
    public string Name { get; init; } = string.Empty;
    public ModelMetadataStorageSection? Storage { get; init; }
    public string? UiType { get; init; }
    public ModelMetadataUiOptionsSection? UiOptions { get; init; }
    public ModelMetadataFormSection? Form { get; init; }
    public ModelMetadataViewSection? View { get; init; }
}

public sealed class ModelMetadataStorageSection
{
    public string Column { get; init; } = string.Empty;
    public string ValueType { get; init; } = string.Empty;
    public string StoreType { get; init; } = string.Empty;
    public bool Required { get; init; }
    public bool PrimaryKey { get; init; }
    public bool Audit { get; init; }
    public int? MaxLength { get; init; }
}
```

```csharp
namespace ICP.Platform.Modeling.Metadata;

public sealed class ModelMetadataFormSection
{
    public ModelMetadataScenarioRule? Create { get; init; }
    public ModelMetadataScenarioRule? Edit { get; init; }
}

public sealed class ModelMetadataViewSection
{
    public bool Visible { get; init; }
}

public sealed class ModelMetadataUiOptionsSection
{
    public string? Format { get; init; }
    public string? PickerMode { get; init; }
}

public sealed class ModelMetadataScenarioRule
{
    public bool Included { get; init; }
    public bool Editable { get; init; }
    public int? Order { get; init; }
}
```

```csharp
namespace ICP.Platform.Modeling.Metadata;

public sealed class ModelMetadataIndex
{
    public string Name { get; init; } = string.Empty;
    public List<string> Fields { get; init; } = [];
    public bool Unique { get; init; }
}
```

- [ ] **Step 4: 实现最小校验器**

```csharp
using ICP.Platform.Modeling.Metadata;

namespace ICP.Platform.Modeling.Validation;

public sealed class ModelMetadataValidator
{
    public void Validate(ModelMetadataDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.ModuleId))
        {
            throw new InvalidOperationException("Model metadata document must define moduleId.");
        }

        if (document.Model.Fields.Count == 0)
        {
            throw new InvalidOperationException($"Model '{document.Model.Name}' must define at least one field.");
        }

        foreach (var field in document.Model.Fields)
        {
            if (field.Storage is null)
            {
                throw new InvalidOperationException($"Field '{field.Name}' must define storage section.");
            }
        }

        if (!document.Model.Fields.Any(static x => x.Storage?.PrimaryKey == true))
        {
            throw new InvalidOperationException($"Model '{document.Model.Name}' must define a primary key field.");
        }
    }
}
```

- [ ] **Step 5: 补充字段引用与索引引用校验**

```csharp
var duplicateField = document.Model.Fields
    .GroupBy(static x => x.Name, StringComparer.OrdinalIgnoreCase)
    .FirstOrDefault(static x => x.Count() > 1);

if (duplicateField is not null)
{
    throw new InvalidOperationException($"Model '{document.Model.Name}' has duplicate field '{duplicateField.Key}'.");
}

var fieldNames = document.Model.Fields
    .Select(static x => x.Name)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

foreach (var index in document.Model.Indexes)
{
    var missingField = index.Fields.FirstOrDefault(field => !fieldNames.Contains(field));
    if (missingField is not null)
    {
        throw new InvalidOperationException($"Index '{index.Name}' references unknown field '{missingField}'.");
    }
}
```

- [ ] **Step 6: 回跑测试验证通过**

Run: `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj --filter ModelMetadataValidatorTests -nr:false -v minimal`

Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add server/platform-modeling/Metadata server/platform-modeling/Validation/ModelMetadataValidator.cs tests/Modeling.Tests
git commit -m "功能：新增模型元数据文档与分区校验器"
```

### Task 2: 新增 JSON 加载器与 storage 到 ModelDefinition 的投影器

**Files:**
- Create: `server/platform-modeling/Metadata/IModelMetadataDocumentLoader.cs`
- Create: `server/platform-modeling/Metadata/JsonModelMetadataDocumentLoader.cs`
- Create: `server/platform-modeling/Projection/ModelMetadataProjector.cs`
- Create: `tests/Modeling.Tests/ModelMetadataProjectorTests.cs`
- Modify: `server/platform-modeling/README.md`

- [ ] **Step 1: 先写 projector failing test**

```csharp
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class ModelMetadataProjectorTests
{
    [Fact]
    public void Project_UsesStorageSectionAndIgnoresFormView()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "ConfigCenter",
            Model = new ModelMetadataModel
            {
                Name = "ConfigItem",
                Table = "config_items",
                Kind = "Entity",
                Fields =
                [
                    new ModelMetadataField
                    {
                        Name = "Key",
                        Storage = new ModelMetadataStorageSection
                        {
                            Column = "config_key",
                            ValueType = "Text",
                            StoreType = "string",
                            Required = true
                        },
                        UiType = "text",
                        Form = new ModelMetadataFormSection
                        {
                            Create = new ModelMetadataScenarioRule { Included = true, Editable = false }
                        },
                        View = new ModelMetadataViewSection { Visible = true }
                    }
                ],
                Indexes = []
            }
        };

        var definition = new ModelMetadataProjector().Project(document);

        Assert.Equal("ConfigItem", definition.Name);
        Assert.Equal("config_items", definition.TableName);
        Assert.Contains(definition.Fields, static x => x.Name == "Key" && x.StoreName == "config_key" && x.Required);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj --filter ModelMetadataProjectorTests -nr:false -v minimal`

Expected: FAIL，提示 `ModelMetadataProjector` 或加载器尚不存在。

- [ ] **Step 3: 实现 JSON 加载器**

```csharp
using System.Text.Json;

namespace ICP.Platform.Modeling.Metadata;

public interface IModelMetadataDocumentLoader
{
    ModelMetadataDocument LoadFromFile(string path);
}

public sealed class JsonModelMetadataDocumentLoader : IModelMetadataDocumentLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public ModelMetadataDocument LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        var document = JsonSerializer.Deserialize<ModelMetadataDocument>(json, Options)
            ?? throw new InvalidOperationException($"Failed to deserialize model metadata: {path}");

        return document;
    }
}
```

- [ ] **Step 4: 实现 storage 分区投影器**

```csharp
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;

namespace ICP.Platform.Modeling.Projection;

public sealed class ModelMetadataProjector
{
    public ModelDefinition Project(ModelMetadataDocument document)
    {
        var fields = document.Model.Fields
            .Select(ProjectField)
            .ToArray();

        var indexes = document.Model.Indexes
            .Select(index => new IndexDefinition(index.Name, index.Fields, index.Unique))
            .ToArray();

        return new ModelDefinition(
            document.Model.Name,
            document.Model.Table,
            Enum.Parse<ModelKind>(document.Model.Kind, ignoreCase: true),
            fields,
            indexes);
    }

    private static FieldDefinition ProjectField(ModelMetadataField field)
    {
        var storage = field.Storage ?? throw new InvalidOperationException($"Field '{field.Name}' must define storage section.");

        return new FieldDefinition(
            field.Name,
            storage.Column,
            storage.Audit ? FieldKind.Audit : FieldKind.Scalar,
            Enum.Parse<FieldValueType>(storage.ValueType, ignoreCase: true),
            storage.StoreType,
            storage.Required,
            storage.PrimaryKey,
            storage.Audit);
    }
}
```

- [ ] **Step 5: 在 README 中固定边界说明**

```md
## Model Metadata Partition Rules

- `storage` 是唯一允许投影到 `ModelDefinition` 与 migration 的分区
- `form`、`view`、`uiType` 和 `uiOptions` 只用于 UI/schema authoring，不参与数据库投影
- `view` 在模型字段层只控制详情页是否显示该属性；列表列由独立 `.view` 元数据决定
- 单文件允许共存三类语义，但校验器必须阻止跨区越界
```

- [ ] **Step 6: 回跑测试验证通过**

Run: `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj --filter "ModelMetadataProjectorTests|ModelMetadataValidatorTests" -nr:false -v minimal`

Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add server/platform-modeling/Metadata server/platform-modeling/Projection server/platform-modeling/README.md tests/Modeling.Tests
git commit -m "功能：新增模型元数据 JSON 加载与 storage 投影器"
```

### Task 3: 用 ConfigCenter 试点元数据文件并保持现有测试口径

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
        Assert.Contains(definition.Fields, static x => x.Name == "Key" && x.StoreName == "config_key");
        Assert.Contains(definition.Indexes, static x => x.Name == "UX_ConfigItem_Key_Environment_Tenant" && x.IsUnique);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "ConfigCenterModelDefinitionTests|ConfigCenterModelMetadataProviderTests" -nr:false -v minimal`

Expected: FAIL，提示 provider 或元数据文件尚不存在。

- [ ] **Step 3: 创建试点元数据文件**

```json
{
  "schemaVersion": "1.0",
  "moduleId": "ConfigCenter",
  "model": {
    "name": "ConfigItem",
    "table": "config_items",
    "kind": "Entity",
    "fields": [
      {
        "name": "Id",
        "storage": {
          "column": "id",
          "valueType": "ObjectId",
          "storeType": "string",
          "required": true,
          "primaryKey": true,
          "audit": false
        },
        "uiType": "object-id",
        "form": {
          "create": { "included": false, "editable": false, "order": 0 },
          "edit": { "included": false, "editable": false, "order": 0 }
        },
        "view": { "visible": false }
      },
      {
        "name": "Key",
        "storage": {
          "column": "config_key",
          "valueType": "Text",
          "storeType": "string",
          "required": true,
          "primaryKey": false,
          "audit": false,
          "maxLength": 200
        },
        "uiType": "text",
        "form": {
          "create": { "included": true, "editable": true, "order": 10 },
          "edit": { "included": true, "editable": false, "order": 10 }
        },
        "view": { "visible": true }
      }
    ],
    "indexes": [
      {
        "name": "UX_ConfigItem_Key_Environment_Tenant",
        "fields": ["Key", "Environment", "TenantId"],
        "unique": true
      },
      {
        "name": "IX_ConfigItem_Module",
        "fields": ["ModuleId"],
        "unique": false
      }
    ]
  }
}
```

- [ ] **Step 4: 实现试点 provider**

```csharp
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;

namespace ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;

public sealed class ConfigCenterModelMetadataProvider
{
    private static readonly string ConfigItemPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "server", "modules", "ConfigCenter", "metadata", "models", "ConfigItem.model.json"));

    private readonly IModelMetadataDocumentLoader _loader = new JsonModelMetadataDocumentLoader();
    private readonly ModelMetadataValidator _validator = new();
    private readonly ModelMetadataProjector _projector = new();

    public ModelDefinition LoadConfigItem()
    {
        var document = _loader.LoadFromFile(ConfigItemPath);
        _validator.Validate(document);
        return _projector.Project(document);
    }
}
```

- [ ] **Step 5: 将现有定义类改为走 provider**

```csharp
namespace ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;

public static class ConfigCenterModelDefinitions
{
    private static readonly Lazy<ModelDefinition> ConfigItemDefinition = new(() =>
        new ConfigCenterModelMetadataProvider().LoadConfigItem());

    public static ModelDefinition ConfigItem => ConfigItemDefinition.Value;
}
```

- [ ] **Step 6: 回跑模块测试验证通过**

Run:
- `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "ConfigCenterModelDefinitionTests|ConfigCenterModelMetadataProviderTests" -nr:false -v minimal`
- `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj --filter "ModelMetadataProjectorTests|ModelMetadataValidatorTests" -nr:false -v minimal`

Expected:
- `ConfigCenter` 新旧模型定义测试都通过
- `Modeling.Tests` 继续通过

- [ ] **Step 7: Commit**

```bash
git add server/modules/ConfigCenter tests/ConfigCenter.Tests
git commit -m "功能：用 ConfigCenter 试点单文件分区模型元数据"
```

### Task 4: 固定“form/view/uiType 不影响数据库”的回归测试与最小验证记录

**Files:**
- Modify: `tests/Modeling.Tests/ModelMetadataProjectorTests.cs`
- Create: `docs/verification/model-metadata-single-file-partition-verification.md`
- Modify: `docs/current-status.md`
- Modify: `docs/designs/model-metadata-single-file-partition-design.md`

- [ ] **Step 1: 补一条越界保护测试**

```csharp
[Fact]
public void Project_DoesNotUseUiTypeOrFormFlagsToMutateStorageDefinition()
{
    var document = new ModelMetadataDocument
    {
        SchemaVersion = "1.0",
        ModuleId = "ConfigCenter",
        Model = new ModelMetadataModel
        {
            Name = "ConfigItem",
            Table = "config_items",
            Kind = "Entity",
            Fields =
            [
                new ModelMetadataField
                {
                    Name = "Value",
                    Storage = new ModelMetadataStorageSection
                    {
                        Column = "config_value",
                        ValueType = "Description",
                        StoreType = "string",
                        Required = false
                    },
                    UiType = "ticks-date",
                    UiOptions = new ModelMetadataUiOptionsSection { Format = "yyyy-MM-dd" },
                    Form = new ModelMetadataFormSection
                    {
                        Create = new ModelMetadataScenarioRule { Editable = false, Included = true }
                    },
                    View = new ModelMetadataViewSection { Visible = true }
                }
            ],
            Indexes = []
        }
    };

    var field = new ModelMetadataProjector().Project(document).Fields.Single();

    Assert.Equal("config_value", field.StoreName);
    Assert.Equal(FieldValueType.Description, field.ValueType);
    Assert.False(field.Required);
}
```

- [ ] **Step 2: 运行建模与模块定向测试**

Run:
- `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj -nr:false -v minimal`
- `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "ConfigCenterModelMetadataProviderTests|ConfigCenterModelDefinitionTests" -nr:false -v minimal`

Expected:
- `Modeling.Tests` 全绿
- `ConfigCenter` 相关模型测试全绿

- [ ] **Step 3: 补验证文档**

```md
## 验证范围
- 模型元数据文档结构校验
- storage 到 ModelDefinition 投影
- ConfigCenter 试点模块从 JSON 加载定义
- form/view/uiType 不影响数据库结构

## 验证命令
- dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj -nr:false -v minimal
- dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "ConfigCenterModelMetadataProviderTests|ConfigCenterModelDefinitionTests" -nr:false -v minimal
```

- [ ] **Step 4: 更新设计文档和当前状态**

```md
### 首轮落地状态
- 已完成 storage 主链路试点
- ConfigCenter 已从单文件分区模型元数据投影出现有 ModelDefinition
- form/view/uiType 已进入契约，但尚未进入前端消费实现
```

- [ ] **Step 5: Commit**

```bash
git add tests/Modeling.Tests docs/verification/model-metadata-single-file-partition-verification.md docs/current-status.md docs/designs/model-metadata-single-file-partition-design.md
git commit -m "文档：收口单文件分区模型元数据首轮验证"
```

## Self-Review Checklist

- [ ] 规格覆盖：
  - 单文件分区契约：Task 1
  - `storage` 到 `ModelDefinition` 投影：Task 2
  - `ConfigCenter` 首个试点：Task 3
  - `form/view/uiType` 不影响数据库结构：Task 4
- [ ] 占位词扫描：计划中未使用 `TODO`、`TBD`、`implement later` 等占位描述。
- [ ] 类型一致性：
  - 文档模型统一使用 `ModelMetadataDocument / ModelMetadataField / ModelMetadataStorageSection`
  - 投影入口统一使用 `ModelMetadataProjector`
  - 试点模块统一使用 `ConfigCenterModelMetadataProvider`
