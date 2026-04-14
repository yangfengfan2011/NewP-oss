# Setting 导入 POC 实施计划

> **面向代理执行：** 必选子技能：使用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans` 按任务逐项执行本计划。步骤统一使用复选框 `- [ ]` 语法跟踪。

**目标：** 完成 `Phase 5` 的首个旧平台 `.setting` 导入概念验证，把旧平台 `AppSet` 配置项导入到 `ConfigCenter`。

**架构：** 保持 POC 最小闭环。在 `ConfigCenter` 内新增一个专用的旧 `.setting` XML 解析器和导入服务，暴露一个受控的后端导入入口，并通过现有配置查询 API 和当前配置管理页验证导入结果。当前不建设通用迁移平台，也不保留旧 XML 文件的运行时兼容加载。

**技术栈：** ASP.NET Core、C#、xUnit、现有 `ConfigCenter` 模块、现有 React 配置管理页

---

## 文件结构

### 需要修改的现有文件

- `server\modules\ConfigCenter\ConfigCenterModule.cs`
  注册新的导入解析器与导入服务。
- `server\modules\ConfigCenter\Controllers\ConfigController.cs`
  增加一个旧 `.setting` 文件的显式导入接口。
- `server\modules\ConfigCenter\Services\IConfigService.cs`
  只有在导入流程确实需要通过既有服务抽象时才修改，否则保持不变。
- `server\modules\ConfigCenter\Services\ConfigService.cs`
  复用当前创建、更新、查询逻辑。只有在导入 upsert 需要小型辅助方法时才修改。
- `tests\ConfigCenter.Tests\ConfigServiceTests.cs`
  保持现有 CRUD 和优先级测试不变，不在此文件里混入导入专项测试。
- `docs\designs\phase-5-first-import-poc-design.md`
  在实现落地后补齐最终样本与实现说明。
- `docs\current-status.md`
  POC 完成并验证后，更新 `Phase 5` 状态。

### 需要新增的文件

- `server\modules\ConfigCenter\Imports\LegacySettingImportItem.cs`
  表达单个旧 `AppSet` 节点的强类型模型。
- `server\modules\ConfigCenter\Imports\LegacySettingImportRequest.cs`
  受控导入请求模型。
- `server\modules\ConfigCenter\Imports\LegacySettingImportResult.cs`
  导入结果模型，记录创建、更新、跳过、失败及导入键列表。
- `server\modules\ConfigCenter\Imports\ILegacySettingParser.cs`
  旧 `.setting` 解析接口。
- `server\modules\ConfigCenter\Imports\LegacySettingXmlParser.cs`
  `root/AppSet` 的 XML 解析实现。
- `server\modules\ConfigCenter\Imports\ILegacySettingImportService.cs`
  导入编排接口。
- `server\modules\ConfigCenter\Imports\LegacySettingImportService.cs`
  负责导入执行和按 `Key` 覆盖。
- `tests\ConfigCenter.Tests\LegacySettingXmlParserTests.cs`
  解析器测试。
- `tests\ConfigCenter.Tests\LegacySettingImportServiceTests.cs`
  导入服务测试。
- `docs\migration\setting-import-poc.md`
  样本来源、映射规则、接口形式、当前限制说明。
- `docs\verification\setting-import-poc-verification.md`
  POC 验证记录。

### 当前 POC 样本假设

- 主样本：`D:\Github\ICP-X\OldP\Runtime\1_Server\Config\CompanyRuleSetting.setting`
- 次样本：`D:\Github\ICP-X\OldP\Runtime\1_Server\CommandServiceManagement\Config\CommandService.setting`

这两个文件小而清晰，适合作为机制验证样本。当前不要从 `Base.setting` 或 `Web.setting` 起步。

---

### 任务 1：解析旧 `.setting` XML 为导入模型

**文件：**
- 新增：`server\modules\ConfigCenter\Imports\LegacySettingImportItem.cs`
- 新增：`server\modules\ConfigCenter\Imports\ILegacySettingParser.cs`
- 新增：`server\modules\ConfigCenter\Imports\LegacySettingXmlParser.cs`
- 测试：`tests\ConfigCenter.Tests\LegacySettingXmlParserTests.cs`

- [ ] **步骤 1：先写失败测试**

```csharp
using ICP.Platform.Modules.ConfigCenter.Imports;
using Xunit;

namespace ICP.Platform.Modules.ConfigCenter.Tests;

public class LegacySettingXmlParserTests
{
    [Fact]
    public void Parse_ValidSettingXml_ReturnsAppSets()
    {
        const string xml = """
        <?xml version="1.0"?>
        <root>
          <AppSet Key="OtherCompanyCanOperate" Value="false" ToClient="false" Description="desc" />
          <AppSet Key="ActionOfOtherCompanyCanOperate" Value="BROWSE" ToClient="false" Description="desc2" />
        </root>
        """;

        var parser = new LegacySettingXmlParser();

        var items = parser.Parse(xml, "CompanyRuleSetting.setting");

        Assert.Equal(2, items.Count);
        Assert.Equal("OtherCompanyCanOperate", items[0].Key);
        Assert.Equal("false", items[0].Value);
        Assert.False(items[0].ToClient);
        Assert.Equal("CompanyRuleSetting", items[0].Group);
    }

    [Fact]
    public void Parse_MissingKey_ThrowsInvalidOperationException()
    {
        const string xml = """
        <?xml version="1.0"?>
        <root>
          <AppSet Value="false" ToClient="false" Description="desc" />
        </root>
        """;

        var parser = new LegacySettingXmlParser();

        var ex = Assert.Throws<InvalidOperationException>(() => parser.Parse(xml, "Broken.setting"));
        Assert.Contains("Key", ex.Message);
    }
}
```

- [ ] **步骤 2：运行测试，确认当前失败**

运行：

```powershell
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter LegacySettingXmlParserTests
```

预期：FAIL，因为解析器相关类型尚未实现。

- [ ] **步骤 3：补充导入模型与解析接口**

```csharp
namespace ICP.Platform.Modules.ConfigCenter.Imports;

public class LegacySettingImportItem
{
    public required string Key { get; init; }
    public string? Value { get; init; }
    public bool ToClient { get; init; }
    public string? Description { get; init; }
    public required string Group { get; init; }
    public required string SourceFile { get; init; }
}
```

```csharp
namespace ICP.Platform.Modules.ConfigCenter.Imports;

public interface ILegacySettingParser
{
    IReadOnlyList<LegacySettingImportItem> Parse(string xml, string sourceFileName);
}
```

- [ ] **步骤 4：实现 `root/AppSet` XML 解析器**

```csharp
using System.Xml.Linq;

namespace ICP.Platform.Modules.ConfigCenter.Imports;

public class LegacySettingXmlParser : ILegacySettingParser
{
    public IReadOnlyList<LegacySettingImportItem> Parse(string xml, string sourceFileName)
    {
        var document = XDocument.Parse(xml);
        var group = Path.GetFileNameWithoutExtension(sourceFileName);

        return document.Root?
            .Elements("AppSet")
            .Select(element =>
            {
                var key = (string?)element.Attribute("Key");
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException($"Legacy setting item in '{sourceFileName}' is missing Key.");
                }

                return new LegacySettingImportItem
                {
                    Key = key,
                    Value = (string?)element.Attribute("Value"),
                    ToClient = bool.TryParse((string?)element.Attribute("ToClient"), out var toClient) && toClient,
                    Description = (string?)element.Attribute("Description"),
                    Group = group,
                    SourceFile = sourceFileName
                };
            })
            .ToList()
            ?? [];
    }
}
```

- [ ] **步骤 5：重新运行解析器测试**

运行：

```powershell
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter LegacySettingXmlParserTests
```

预期：PASS，2 个测试都通过。

- [ ] **步骤 6：提交解析器阶段代码**

```powershell
git add .\server\modules\ConfigCenter\Imports\LegacySettingImportItem.cs .\server\modules\ConfigCenter\Imports\ILegacySettingParser.cs .\server\modules\ConfigCenter\Imports\LegacySettingXmlParser.cs .\tests\ConfigCenter.Tests\LegacySettingXmlParserTests.cs
git commit -m "feat: add legacy setting xml parser"
```

---

### 任务 2：把 `.setting` 导入 `ConfigCenter`，并按 `Key` 覆盖

**文件：**
- 新增：`server\modules\ConfigCenter\Imports\LegacySettingImportRequest.cs`
- 新增：`server\modules\ConfigCenter\Imports\LegacySettingImportResult.cs`
- 新增：`server\modules\ConfigCenter\Imports\ILegacySettingImportService.cs`
- 新增：`server\modules\ConfigCenter\Imports\LegacySettingImportService.cs`
- 修改：`server\modules\ConfigCenter\ConfigCenterModule.cs`
- 修改：`server\modules\ConfigCenter\Controllers\ConfigController.cs`
- 测试：`tests\ConfigCenter.Tests\LegacySettingImportServiceTests.cs`

- [ ] **步骤 1：先写失败测试**

```csharp
using ICP.Platform.Modules.ConfigCenter.Imports;
using ICP.Platform.Modules.ConfigCenter.Repositories;
using Xunit;

namespace ICP.Platform.Modules.ConfigCenter.Tests;

public class LegacySettingImportServiceTests
{
    [Fact]
    public async Task ImportAsync_NewKeys_CreatesConfigItems()
    {
        var repository = new ConfigRepository();
        var parser = new LegacySettingXmlParser();
        var service = new LegacySettingImportService(repository, parser);

        const string xml = """
        <?xml version="1.0"?>
        <root>
          <AppSet Key="OtherCompanyCanOperate" Value="false" ToClient="false" Description="desc" />
        </root>
        """;

        var result = await service.ImportAsync(new LegacySettingImportRequest
        {
            ModuleId = "ConfigCenter",
            SourceFileName = "CompanyRuleSetting.setting",
            XmlContent = xml
        });

        Assert.Equal(1, result.CreatedCount);
        var saved = await repository.GetByKeyAsync("OtherCompanyCanOperate");
        Assert.NotNull(saved);
        Assert.Equal("ConfigCenter", saved.ModuleId);
        Assert.Equal("CompanyRuleSetting", saved.Group);
        Assert.Equal("Bool", saved.Type);
    }

    [Fact]
    public async Task ImportAsync_SameKey_OverwritesExistingValue()
    {
        var repository = new ConfigRepository();
        await repository.CreateAsync(new Models.ConfigItem
        {
            Key = "OtherCompanyCanOperate",
            Value = "true",
            Type = "Bool",
            ModuleId = "ConfigCenter",
            DisplayName = "OtherCompanyCanOperate",
            Group = "CompanyRuleSetting"
        });

        var parser = new LegacySettingXmlParser();
        var service = new LegacySettingImportService(repository, parser);

        const string xml = """
        <?xml version="1.0"?>
        <root>
          <AppSet Key="OtherCompanyCanOperate" Value="false" ToClient="false" Description="desc" />
        </root>
        """;

        var result = await service.ImportAsync(new LegacySettingImportRequest
        {
            ModuleId = "ConfigCenter",
            SourceFileName = "CompanyRuleSetting.setting",
            XmlContent = xml
        });

        Assert.Equal(1, result.UpdatedCount);
        var saved = await repository.GetByKeyAsync("OtherCompanyCanOperate");
        Assert.NotNull(saved);
        Assert.Equal("false", saved.Value);
    }
}
```

- [ ] **步骤 2：运行测试，确认当前失败**

运行：

```powershell
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter LegacySettingImportServiceTests
```

预期：FAIL，因为导入请求、结果和服务尚未实现。

- [ ] **步骤 3：补充导入请求与结果模型**

```csharp
namespace ICP.Platform.Modules.ConfigCenter.Imports;

public class LegacySettingImportRequest
{
    public required string ModuleId { get; init; }
    public required string SourceFileName { get; init; }
    public required string XmlContent { get; init; }
}
```

```csharp
namespace ICP.Platform.Modules.ConfigCenter.Imports;

public class LegacySettingImportResult
{
    public int CreatedCount { get; init; }
    public int UpdatedCount { get; init; }
    public int FailedCount { get; init; }
    public List<string> ImportedKeys { get; init; } = new();
}
```

- [ ] **步骤 4：实现按 `Key` 覆盖的导入服务**

```csharp
using ICP.Platform.Modules.ConfigCenter.Models;
using ICP.Platform.Modules.ConfigCenter.Repositories;

namespace ICP.Platform.Modules.ConfigCenter.Imports;

public interface ILegacySettingImportService
{
    Task<LegacySettingImportResult> ImportAsync(LegacySettingImportRequest request, CancellationToken cancellationToken = default);
}

public class LegacySettingImportService : ILegacySettingImportService
{
    private readonly IConfigRepository _repository;
    private readonly ILegacySettingParser _parser;

    public LegacySettingImportService(IConfigRepository repository, ILegacySettingParser parser)
    {
        _repository = repository;
        _parser = parser;
    }

    public async Task<LegacySettingImportResult> ImportAsync(LegacySettingImportRequest request, CancellationToken cancellationToken = default)
    {
        var parsedItems = _parser.Parse(request.XmlContent, request.SourceFileName);
        var importedKeys = new List<string>();
        var created = 0;
        var updated = 0;

        foreach (var item in parsedItems)
        {
            var existing = await _repository.GetByKeyAsync(item.Key, cancellationToken: cancellationToken);
            if (existing == null)
            {
                await _repository.CreateAsync(new ConfigItem
                {
                    Key = item.Key,
                    Value = item.Value,
                    Type = InferType(item.Value),
                    ModuleId = request.ModuleId,
                    Group = item.Group,
                    DisplayName = item.Key,
                    Description = item.Description,
                    IsSensitive = IsSensitiveKey(item.Key)
                }, cancellationToken);

                created++;
            }
            else
            {
                existing.Value = item.Value;
                existing.Type = InferType(item.Value);
                existing.ModuleId = request.ModuleId;
                existing.Group = item.Group;
                existing.Description = item.Description;
                existing.DisplayName = existing.DisplayName ?? item.Key;
                existing.IsSensitive = IsSensitiveKey(item.Key);
                existing.UpdatedAt = DateTime.UtcNow;
                await _repository.UpdateAsync(existing, cancellationToken);

                updated++;
            }

            importedKeys.Add(item.Key);
        }

        return new LegacySettingImportResult
        {
            CreatedCount = created,
            UpdatedCount = updated,
            FailedCount = 0,
            ImportedKeys = importedKeys
        };
    }

    private static string InferType(string? value)
    {
        if (bool.TryParse(value, out _)) return "Bool";
        if (int.TryParse(value, out _)) return "Int";
        return "String";
    }

    private static bool IsSensitiveKey(string key)
    {
        return key.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Key", StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **步骤 5：注册服务并新增受控导入接口**

```csharp
services.AddScoped<ILegacySettingParser, LegacySettingXmlParser>();
services.AddScoped<ILegacySettingImportService, LegacySettingImportService>();
```

```csharp
[HttpPost("import/setting")]
public async Task<ActionResult<LegacySettingImportResult>> ImportLegacySetting(
    [FromBody] LegacySettingImportRequest request,
    [FromServices] ILegacySettingImportService importService,
    CancellationToken cancellationToken = default)
{
    var result = await importService.ImportAsync(request, cancellationToken);
    return Ok(result);
}
```

- [ ] **步骤 6：运行导入专项测试和现有模块测试**

运行：

```powershell
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "LegacySettingImportServiceTests|ConfigServiceTests"
```

预期：PASS，导入专项测试和原有 `ConfigService` 测试都通过。

- [ ] **步骤 7：构建后端模块**

运行：

```powershell
dotnet build .\NewP\server\modules\ConfigCenter\ConfigCenter.csproj
```

预期：`BUILD SUCCEEDED`。

- [ ] **步骤 8：提交导入后端实现**

```powershell
git add .\NewP\server\modules\ConfigCenter\ConfigCenterModule.cs .\NewP\server\modules\ConfigCenter\Controllers\ConfigController.cs .\NewP\server\modules\ConfigCenter\Imports .\NewP\tests\ConfigCenter.Tests\LegacySettingImportServiceTests.cs
git commit -m "feat: add legacy setting import poc backend"
```

---

### 任务 3：补齐迁移说明、验证记录和阶段状态

**文件：**
- 新增：`docs\migration\setting-import-poc.md`
- 新增：`docs\verification\setting-import-poc-verification.md`
- 修改：`docs\designs\phase-5-first-import-poc-design.md`
- 修改：`docs\current-status.md`

- [ ] **步骤 1：写迁移说明文档，明确真实样本与映射规则**

```markdown
# Setting 导入 POC

## 样本来源

- `OldP/Runtime/1_Server/Config/CompanyRuleSetting.setting`
- `OldP/Runtime/1_Server/CommandServiceManagement/Config/CommandService.setting`

## 旧结构

- XML 根节点：`root`
- 配置项节点：`AppSet`
- 属性：`Key`、`Value`、`ToClient`、`Description`

## 映射规则

- `AppSet.Key` -> `ConfigItem.Key`
- `AppSet.Value` -> `ConfigItem.Value`
- 导入请求中的 `ModuleId` -> `ConfigItem.ModuleId`
- 源文件名（去后缀） -> `ConfigItem.Group`
- `AppSet.Description` -> `ConfigItem.Description`
- `AppSet.Key` -> `ConfigItem.DisplayName`

## 覆盖规则

- 相同 `Key` 视为同一目标配置项
- 后导入值覆盖先前值
```

- [ ] **步骤 2：通过 API 做一次本地烟雾导入**

运行：

```powershell
$body = @{
  moduleId = "ConfigCenter"
  sourceFileName = "CompanyRuleSetting.setting"
  xmlContent = [System.IO.File]::ReadAllText("D:\Github\ICP-X\OldP\Runtime\1_Server\Config\CompanyRuleSetting.setting")
} | ConvertTo-Json -Depth 5

Invoke-RestMethod -Method Post -Uri "http://localhost:3000/api/config/import/setting" -ContentType "application/json" -Body $body
```

预期：首次执行时 `createdCount` 大于 `0`；重复执行时 `updatedCount` 大于 `0`。

- [ ] **步骤 3：通过现有 API 验证导入结果**

运行：

```powershell
Invoke-RestMethod -Method Get -Uri "http://localhost:3000/api/config/OtherCompanyCanOperate"
Invoke-RestMethod -Method Get -Uri "http://localhost:3000/api/config/grouped"
```

预期：

- 单键查询返回导入后的值。
- 分组查询中能看到 `ConfigCenter / CompanyRuleSetting`。

- [ ] **步骤 4：在现有配置页验证可见性**

手工检查：

- 打开 `http://localhost:3000/config/items`
- 确认 `ConfigCenter / CompanyRuleSetting` 分组出现
- 确认 `OtherCompanyCanOperate` 可见且可编辑

预期：不新增前端代码也能看到导入数据。

- [ ] **步骤 5：写验证记录并更新状态文档**

```markdown
## 验证命令

- `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "LegacySettingXmlParserTests|LegacySettingImportServiceTests|ConfigServiceTests"`
- `dotnet build .\NewP\server\modules\ConfigCenter\ConfigCenter.csproj`
- `POST /api/config/import/setting` 导入 `CompanyRuleSetting.setting`
- `GET /api/config/OtherCompanyCanOperate`
- `GET /api/config/grouped`

## 结果摘要

- 解析器通过
- 导入服务通过
- 首次导入成功创建配置项
- 重复导入按 `Key` 更新已有项
- 配置页可见导入结果
```

- [ ] **步骤 6：提交文档与状态更新**

```powershell
git add .\NewP\docs\migration\setting-import-poc.md .\NewP\docs\verification\setting-import-poc-verification.md .\NewP\docs\designs\phase-5-first-import-poc-design.md .\NewP\docs\current-status.md
git commit -m "docs: record setting import poc"
```

---

## 自检

- 规格覆盖：本计划覆盖了 `.setting` 解析、受控导入、按 `Key` 覆盖、API 验证、现有配置页可见性验证，以及文档收口。
- 占位项扫描：文中没有 `TODO`、`TBD` 或“后续补充”这类空泛占位。
- 类型一致性：统一使用 `LegacySettingImportItem`、`LegacySettingImportRequest`、`LegacySettingImportResult` 和 `LegacySettingImportService`。

## 执行交接

计划已保存到 `docs/superpowers/plans/2026-04-05-setting-import-poc.md`。

后续执行模式已确定：**Codex 负责统筹和审查，Claude 负责按任务包实现代码**。真正开始动手时，使用 Codex/Claude 协同流程，一次只派发一个边界清晰的任务给 Claude。
