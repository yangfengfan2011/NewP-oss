# 用户可见文本多语言基础（第二包：后端用户消息契约）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立共享的后端用户消息契约基座，并以 `ConfigCenter` 值列表链路为试点，完成 `messageKey + messageArgs + level` 从后端到前端翻译展示的最小正式闭环。

**Architecture:** 共享契约类型放在 `server/sdk` 与 `frontend/packages/sdk`，保证模块和 Host 都能复用；前端格式化能力放在 `frontend/packages/core`，直接复用现有 `LocalizationProvider`。本包只改造 `ConfigCenter` 值列表的创建、编辑、删除失败链路，并补齐 `modules/ConfigCenter/metadata/dictionaries`、测试、打包验证和状态文档，不扩展到全仓库历史接口或“后端直接返回已翻译文本”路径。

**Tech Stack:** .NET 10 / ASP.NET Core / C# / React / TypeScript / Vitest / xUnit / PowerShell

---

## 文件结构与职责

**Create:**

- `server/sdk/UserMessages/UserMessageLevel.cs`
  共享用户消息级别枚举及序列化辅助；统一 `info / success / warning / error` 线缆值。
- `server/sdk/UserMessages/UserMessageDto.cs`
  后端返回给前端展示的用户消息 DTO；承载 `success / level / messageKey / messageArgs / fallbackMessage / code / fieldKey / actionKey / statusKey`。
- `server/sdk/UserMessages/UserMessageException.cs`
  服务层抛出的用户消息异常；额外承载 HTTP 状态码，便于 Controller 统一落成 DTO。
- `frontend/packages/sdk/src/userMessage.ts`
  前端共享用户消息类型、类型守卫和响应读取辅助。
- `frontend/packages/core/src/localization/userMessage.ts`
  基于 `LocalizationProvider` 的用户消息格式化能力；负责 `messageKey` 查词和 `{arg}` 占位替换。
- `frontend/packages/shell/src/__tests__/user-message.test.ts`
  覆盖前端消息格式化、`fallbackMessage` 回退和参数替换。
- `frontend/packages/shell/src/__tests__/value-list-management-message-contract.test.tsx`
  覆盖值列表页消费后端用户消息契约并翻译展示。
- `tests/Kernel.Tests/UserMessageContractTests.cs`
  覆盖后端 DTO 与异常到 DTO 的最小契约测试。
- `tests/Kernel.Tests/ValueListControllerMessageContractTests.cs`
  覆盖值列表控制器返回 `messageKey + messageArgs + level` 的 HTTP 契约。
- `tests/Kernel.Tests/RuntimeViewMaterializerTests.cs`
  覆盖 `ConfigCenter` 值列表词条进入 runtime `localization manifest`。
- `modules/ConfigCenter/metadata/dictionaries/ValueLists.dic.json`
  值列表试点消息词条；作为 `ConfigCenter` 模块 authoring 真源。
- `docs/verification/user-visible-text-localization-foundation-package-2-verification.md`
  第二包验证记录。

**Modify:**

- `frontend/packages/sdk/src/index.ts`
  导出共享用户消息类型。
- `frontend/packages/core/src/localization/index.ts`
  导出用户消息格式化入口。
- `frontend/packages/core/src/index.ts`
  对外暴露 `formatUserMessage` 等能力。
- `server/modules/ConfigCenter/Services/ValueListService.cs`
  从 `InvalidOperationException + 中文字符串` 切换到 `UserMessageException`。
- `server/modules/ConfigCenter/Controllers/ValueListController.cs`
  统一捕获 `UserMessageException` 并返回 DTO 契约。
- `tests/ConfigCenter.Tests/ValueListServiceTests.cs`
  改成断言 `UserMessageException` 的 key、args、statusCode。
- `tests/Kernel.Tests/UnifiedPluginPackagingScriptsTests.cs`
  增加 `ConfigCenter` 字典文件进入统一插件包的打包断言。
- `tests/Kernel.Tests/RuntimeViewMaterializerTests.cs`
  增加值列表词条进入 runtime `localization manifest` 的断言。
- `frontend/modules/config-center/src/pages/ValueListManagement.tsx`
  解析后端用户消息 DTO，并通过 `LocalizationProvider` 翻译展示。
- `docs/current-status.md`
  增补第二包范围、现状和边界。

**Do Not Modify In This Plan:**

- `apps/host-api/Program.cs`
- `frontend/packages/shell/src/App.tsx`
- `apps/host-api/PluginOperations/**`
- `server/modules/Authorization/**`
- `server/modules/Organization/**`

---

### Task 1: 共享用户消息契约基座

**Files:**
- Create: `server/sdk/UserMessages/UserMessageLevel.cs`
- Create: `server/sdk/UserMessages/UserMessageDto.cs`
- Create: `server/sdk/UserMessages/UserMessageException.cs`
- Create: `frontend/packages/sdk/src/userMessage.ts`
- Create: `frontend/packages/core/src/localization/userMessage.ts`
- Create: `tests/Kernel.Tests/UserMessageContractTests.cs`
- Create: `frontend/packages/shell/src/__tests__/user-message.test.ts`
- Modify: `frontend/packages/sdk/src/index.ts`
- Modify: `frontend/packages/core/src/localization/index.ts`
- Modify: `frontend/packages/core/src/index.ts`

- [ ] **Step 1: 先补后端与前端 failing tests**

```csharp
using ICP.Platform.Sdk.UserMessages;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class UserMessageContractTests
{
    [Fact]
    public void FailureDto_SerializesExpectedContractShape()
    {
        var dto = UserMessageDto.Failure(
            level: UserMessageLevel.Error,
            messageKey: "ValueLists.CodeAlreadyExists",
            messageArgs: new Dictionary<string, object?> { ["code"] = "STATUS_ACTIVE" },
            fallbackMessage: "值列表编码 'STATUS_ACTIVE' 已存在。",
            code: "CONFIGCENTER_VALUE_LIST_CODE_DUPLICATE");

        var json = JsonSerializer.Serialize(dto);

        Assert.Contains("\"success\":false", json, StringComparison.Ordinal);
        Assert.Contains("\"level\":\"error\"", json, StringComparison.Ordinal);
        Assert.Contains("\"messageKey\":\"ValueLists.CodeAlreadyExists\"", json, StringComparison.Ordinal);
        Assert.Contains("\"code\":\"STATUS_ACTIVE\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void UserMessageException_ToDto_PreservesStatusCodeAndArgs()
    {
        var exception = new UserMessageException(
            statusCode: StatusCodes.Status404NotFound,
            level: UserMessageLevel.Error,
            messageKey: "ValueLists.ItemNotFound",
            messageArgs: new Dictionary<string, object?> { ["id"] = "missing-id" },
            fallbackMessage: "值列表项 'missing-id' 不存在。");

        var dto = exception.ToDto();

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
        Assert.Equal("ValueLists.ItemNotFound", dto.MessageKey);
        Assert.Equal("missing-id", dto.MessageArgs?["id"]?.ToString());
        Assert.Equal("error", dto.Level);
    }
}
```

```ts
import { describe, expect, it } from 'vitest';
import { formatUserMessage } from '@icp-platform/core';
import type { UserMessageDto } from '@icp-platform/sdk';

describe('formatUserMessage', () => {
  it('formats localized template with message args and fallback', () => {
    const message: UserMessageDto = {
      success: false,
      level: 'error',
      messageKey: 'ValueLists.CodeAlreadyExists',
      messageArgs: { code: 'STATUS_ACTIVE' },
      fallbackMessage: "值列表编码 'STATUS_ACTIVE' 已存在。",
    };

    const translated = formatUserMessage(
      message,
      (key, fallback) => key === 'ValueLists.CodeAlreadyExists'
        ? '值列表编码 {code} 已存在。'
        : fallback ?? key,
    );

    expect(translated).toBe('值列表编码 STATUS_ACTIVE 已存在。');
  });

  it('falls back to fallbackMessage when catalog misses the key', () => {
    const message: UserMessageDto = {
      success: false,
      level: 'error',
      messageKey: 'ValueLists.ItemNotFound',
      messageArgs: { id: 'missing-id' },
      fallbackMessage: "值列表项 'missing-id' 不存在。",
    };

    const translated = formatUserMessage(message, (key, fallback) => fallback ?? key);

    expect(translated).toBe("值列表项 'missing-id' 不存在。");
  });
});
```

- [ ] **Step 2: 跑定向测试，确认当前失败**

Run:

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~UserMessageContractTests -nr:false -v minimal
pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/user-message.test.ts
```

Expected:

```text
FAIL
... UserMessageDto / UserMessageException / formatUserMessage not found ...
```

- [ ] **Step 3: 实现共享后端类型**

```csharp
namespace ICP.Platform.Sdk.UserMessages;

public enum UserMessageLevel
{
    Info,
    Success,
    Warning,
    Error
}

public static class UserMessageLevelExtensions
{
    public static string ToWireValue(this UserMessageLevel level) => level switch
    {
        UserMessageLevel.Info => "info",
        UserMessageLevel.Success => "success",
        UserMessageLevel.Warning => "warning",
        _ => "error"
    };
}
```

```csharp
using System.Text.Json.Serialization;

namespace ICP.Platform.Sdk.UserMessages;

public sealed class UserMessageDto
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("level")]
    public string Level { get; init; } = UserMessageLevel.Error.ToWireValue();

    [JsonPropertyName("messageKey")]
    public string MessageKey { get; init; } = string.Empty;

    [JsonPropertyName("messageArgs")]
    public IReadOnlyDictionary<string, object?>? MessageArgs { get; init; }

    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("fallbackMessage")]
    public string? FallbackMessage { get; init; }

    [JsonPropertyName("fieldKey")]
    public string? FieldKey { get; init; }

    [JsonPropertyName("actionKey")]
    public string? ActionKey { get; init; }

    [JsonPropertyName("statusKey")]
    public string? StatusKey { get; init; }

    public static UserMessageDto Failure(
        UserMessageLevel level,
        string messageKey,
        IReadOnlyDictionary<string, object?>? messageArgs = null,
        string? fallbackMessage = null,
        string? code = null,
        string? fieldKey = null,
        string? actionKey = null,
        string? statusKey = null) =>
        new()
        {
            Success = false,
            Level = level.ToWireValue(),
            MessageKey = messageKey,
            MessageArgs = messageArgs,
            FallbackMessage = fallbackMessage,
            Code = code,
            FieldKey = fieldKey,
            ActionKey = actionKey,
            StatusKey = statusKey
        };
}
```

```csharp
namespace ICP.Platform.Sdk.UserMessages;

public sealed class UserMessageException : Exception
{
    public int StatusCode { get; }
    public UserMessageLevel Level { get; }
    public string MessageKey { get; }
    public IReadOnlyDictionary<string, object?>? MessageArgs { get; }
    public string? Code { get; }
    public string? FallbackMessage { get; }
    public string? FieldKey { get; }
    public string? ActionKey { get; }
    public string? StatusKey { get; }

    public UserMessageException(
        int statusCode,
        UserMessageLevel level,
        string messageKey,
        IReadOnlyDictionary<string, object?>? messageArgs = null,
        string? fallbackMessage = null,
        string? code = null,
        string? fieldKey = null,
        string? actionKey = null,
        string? statusKey = null,
        Exception? innerException = null)
        : base(fallbackMessage ?? messageKey, innerException)
    {
        StatusCode = statusCode;
        Level = level;
        MessageKey = messageKey;
        MessageArgs = messageArgs;
        FallbackMessage = fallbackMessage;
        Code = code;
        FieldKey = fieldKey;
        ActionKey = actionKey;
        StatusKey = statusKey;
    }

    public UserMessageDto ToDto() =>
        UserMessageDto.Failure(Level, MessageKey, MessageArgs, FallbackMessage, Code, FieldKey, ActionKey, StatusKey);
}
```

- [ ] **Step 4: 实现前端共享类型与格式化辅助**

```ts
export type UserMessageLevel = 'info' | 'success' | 'warning' | 'error';

export interface UserMessageDto {
  success: boolean;
  level: UserMessageLevel;
  messageKey: string;
  messageArgs?: Record<string, unknown>;
  code?: string;
  fallbackMessage?: string;
  fieldKey?: string;
  actionKey?: string;
  statusKey?: string;
}

export function isUserMessageDto(value: unknown): value is UserMessageDto {
  if (!value || typeof value !== 'object') {
    return false;
  }

  const candidate = value as Partial<UserMessageDto>;
  return typeof candidate.success === 'boolean'
    && typeof candidate.level === 'string'
    && typeof candidate.messageKey === 'string';
}
```

```ts
import type { UserMessageDto } from '@icp-platform/sdk';

function stringifyArg(value: unknown): string {
  if (value === null || value === undefined) {
    return '';
  }

  return String(value);
}

export function formatUserMessage(
  message: UserMessageDto,
  t: (key: string, fallback?: string) => string,
): string {
  const template = t(message.messageKey, message.fallbackMessage ?? message.messageKey);
  return template.replace(/\{(\w+)\}/g, (_, key: string) => stringifyArg(message.messageArgs?.[key]));
}
```

- [ ] **Step 5: 重新运行定向测试，确认通过**

Run:

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~UserMessageContractTests -nr:false -v minimal
pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/user-message.test.ts
```

Expected:

```text
Passed!
```

- [ ] **Step 6: 提交**

```bash
git add server/sdk/UserMessages/UserMessageLevel.cs server/sdk/UserMessages/UserMessageDto.cs server/sdk/UserMessages/UserMessageException.cs frontend/packages/sdk/src/userMessage.ts frontend/packages/sdk/src/index.ts frontend/packages/core/src/localization/userMessage.ts frontend/packages/core/src/localization/index.ts frontend/packages/core/src/index.ts tests/Kernel.Tests/UserMessageContractTests.cs frontend/packages/shell/src/__tests__/user-message.test.ts
git commit -m "功能：增加用户消息契约基座"
```

### Task 2: `ConfigCenter` 值列表后端消息契约试点

**Files:**
- Create: `modules/ConfigCenter/metadata/dictionaries/ValueLists.dic.json`
- Create: `tests/Kernel.Tests/ValueListControllerMessageContractTests.cs`
- Modify: `server/modules/ConfigCenter/Services/ValueListService.cs`
- Modify: `server/modules/ConfigCenter/Controllers/ValueListController.cs`
- Modify: `tests/ConfigCenter.Tests/ValueListServiceTests.cs`
- Modify: `tests/Kernel.Tests/UnifiedPluginPackagingScriptsTests.cs`
- Modify: `tests/Kernel.Tests/RuntimeViewMaterializerTests.cs`

- [ ] **Step 1: 先补 failing tests，锁定服务层和 HTTP 契约**

```csharp
[Fact]
public async Task CreateAsync_DuplicateCode_ThrowsUserMessageException()
{
    await _service.CreateAsync(new ValueListItemDto
    {
        Code = "STATUS_ACTIVE",
        Name = "启用"
    });

    var error = await Assert.ThrowsAsync<UserMessageException>(() => _service.CreateAsync(
        new ValueListItemDto
        {
            Code = "STATUS_ACTIVE",
            Name = "重复"
        }));

    Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
    Assert.Equal("ValueLists.CodeAlreadyExists", error.MessageKey);
    Assert.Equal("STATUS_ACTIVE", error.MessageArgs?["code"]?.ToString());
}

[Fact]
public async Task DeleteAsync_ItemNotFound_ThrowsUserMessageException()
{
    var error = await Assert.ThrowsAsync<UserMessageException>(() => _service.DeleteAsync("missing-id"));

    Assert.Equal(StatusCodes.Status404NotFound, error.StatusCode);
    Assert.Equal("ValueLists.ItemNotFound", error.MessageKey);
    Assert.Equal("missing-id", error.MessageArgs?["id"]?.ToString());
}
```

```csharp
public class ValueListControllerMessageContractTests : IClassFixture<ValueListControllerMessageContractTests.TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ValueListControllerMessageContractTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_WhenServiceThrowsUserMessageException_ReturnsBadRequestContract()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/value-lists", new
        {
            code = "STATUS_ACTIVE",
            name = "重复"
        });
        var body = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("error", document.RootElement.GetProperty("level").GetString());
        Assert.Equal("ValueLists.CodeAlreadyExists", document.RootElement.GetProperty("messageKey").GetString());
        Assert.Equal("STATUS_ACTIVE", document.RootElement.GetProperty("messageArgs").GetProperty("code").GetString());
    }

    public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Platform:SkipLocalAuthSeeding", "true");
            builder.UseSetting("Persistence:AutoMigrate", "false");
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IValueListService>();
                services.AddSingleton<IValueListService>(_ =>
                    new ThrowingValueListService(new UserMessageException(
                        statusCode: StatusCodes.Status400BadRequest,
                        level: UserMessageLevel.Error,
                        messageKey: "ValueLists.CodeAlreadyExists",
                        messageArgs: new Dictionary<string, object?> { ["code"] = "STATUS_ACTIVE" },
                        fallbackMessage: "值列表编码 'STATUS_ACTIVE' 已存在。")));
            });
        }
    }

    private sealed class ThrowingValueListService : IValueListService
    {
        private readonly Exception _exception;

        public ThrowingValueListService(Exception exception)
        {
            _exception = exception;
        }

        public Task<List<ValueListItemDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<ValueListItemDto>());

        public Task<ValueListItemDto> CreateAsync(ValueListItemDto dto, string? updatedBy = null, CancellationToken cancellationToken = default) =>
            Task.FromException<ValueListItemDto>(_exception);

        public Task<ValueListItemDto> UpdateAsync(string id, ValueListItemDto dto, string? updatedBy = null, CancellationToken cancellationToken = default) =>
            Task.FromException<ValueListItemDto>(_exception);

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromException(_exception);
    }
}
```

```csharp
[Fact]
public void PackUnifiedPlugin_IncludesConfigCenterValueListDictionaries()
{
    var repoRoot = FindRepositoryRoot();
    var outputRoot = Path.Combine(repoRoot, "artifacts", "plugin-packages", $"script-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(outputRoot);
    _tempRoots.Add(outputRoot);

    var result = RunPowerShellScript(
        repoRoot,
        Path.Combine(repoRoot, "scripts", "pack-unified-plugin.ps1"),
        [
            "-ModuleName", "ConfigCenter",
            "-Configuration", "Debug",
            "-OutputRoot", outputRoot,
            "-Force"
        ]);

    var dictionaryPath = Path.Combine(outputRoot, "ConfigCenter", "modules", "ConfigCenter", "metadata", "dictionaries", "ValueLists.dic.json");

    Assert.Equal(0, result.ExitCode);
    Assert.True(File.Exists(dictionaryPath), $"Expected value-list dictionary at {dictionaryPath}.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
}
```

```csharp
[Fact]
public void MaterializeEnabledPlugin_ProjectsValueListEntries_FromInstalledConfigCenterDictionary()
{
    var packageRoot = _testData.CreateValidPackage("ConfigCenter", "1.0.0");
    var dictionariesRoot = Path.Combine(packageRoot, "modules", "ConfigCenter", "metadata", "dictionaries");
    Directory.CreateDirectory(dictionariesRoot);
    File.WriteAllText(
        Path.Combine(dictionariesRoot, "ValueLists.dic.json"),
        """
        {
          "schemaVersion": "1.0",
          "type": "dic",
          "ValueLists": {
            "CodeAlreadyExists": {
              "zhs": "值列表编码 {code} 已存在。",
              "en": "Value list code {code} already exists."
            }
          }
        }
        """);

    var installRoot = CreateInstallRoot();
    EnableInstalledPackage(packageRoot, installRoot, "ConfigCenter");
    EnableInstalledPackage(CreatePlatformPackageWithLanguages(), installRoot, "Platform");

    var stateStore = new InstalledPluginStateStore(installRoot);
    var materializer = new RuntimeViewMaterializer(stateStore);
    materializer.Materialize(installRoot);

    using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(installRoot, "runtime", "localization-manifest.json")));
    Assert.Equal(
        "值列表编码 {code} 已存在。",
        manifest.RootElement.GetProperty("catalogs").GetProperty("zhs").GetProperty("ValueLists.CodeAlreadyExists").GetString());
}
```

- [ ] **Step 2: 跑定向测试，确认当前失败**

Run:

```powershell
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter FullyQualifiedName~ValueListServiceTests -nr:false -v minimal
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter "FullyQualifiedName~ValueListControllerMessageContractTests|FullyQualifiedName~RuntimeViewMaterializerTests|FullyQualifiedName~UnifiedPluginPackagingScriptsTests" -nr:false -v minimal
```

Expected:

```text
FAIL
... UserMessageException not thrown / messageKey not found / ValueLists.CodeAlreadyExists not projected / ValueLists.dic.json not found ...
```

- [ ] **Step 3: 新增值列表词条 authoring 真源**

```json
{
  "schemaVersion": "1.0",
  "type": "dic",
  "ValueLists": {
    "CodeRequired": {
      "zhs": "值列表编码不能为空。",
      "en": "Value list code is required."
    },
    "NameRequired": {
      "zhs": "值列表名称不能为空。",
      "en": "Value list name is required."
    },
    "CodeAlreadyExists": {
      "zhs": "值列表编码 {code} 已存在。",
      "en": "Value list code {code} already exists."
    },
    "ItemNotFound": {
      "zhs": "值列表项 {id} 不存在。",
      "en": "Value list item {id} does not exist."
    }
  }
}
```

- [ ] **Step 4: 把 `ValueListService` 从字符串异常改成 `UserMessageException`**

```csharp
public async Task<ValueListItemDto> CreateAsync(
    ValueListItemDto dto,
    string? updatedBy = null,
    CancellationToken cancellationToken = default)
{
    Validate(dto.Code, dto.Name);

    var normalizedCode = dto.Code.Trim();
    var existing = await _repository.GetByCodeAsync(normalizedCode, cancellationToken);
    if (existing is not null)
    {
        throw new UserMessageException(
            statusCode: StatusCodes.Status400BadRequest,
            level: UserMessageLevel.Error,
            messageKey: "ValueLists.CodeAlreadyExists",
            messageArgs: new Dictionary<string, object?> { ["code"] = normalizedCode },
            fallbackMessage: $"值列表编码 '{normalizedCode}' 已存在。",
            code: "CONFIGCENTER_VALUE_LIST_CODE_DUPLICATE");
    }

    var item = new ValueListItem
    {
        Code = normalizedCode,
        Name = dto.Name.Trim(),
        Description = dto.Description?.Trim(),
        UpdatedBy = updatedBy
    };

    await _repository.CreateAsync(item, cancellationToken);
    return MapToDto(item);
}

private static void Validate(string code, string name)
{
    if (string.IsNullOrWhiteSpace(code))
    {
        throw new UserMessageException(
            statusCode: StatusCodes.Status400BadRequest,
            level: UserMessageLevel.Error,
            messageKey: "ValueLists.CodeRequired",
            fallbackMessage: "值列表编码不能为空。",
            code: "CONFIGCENTER_VALUE_LIST_CODE_REQUIRED");
    }

    if (string.IsNullOrWhiteSpace(name))
    {
        throw new UserMessageException(
            statusCode: StatusCodes.Status400BadRequest,
            level: UserMessageLevel.Error,
            messageKey: "ValueLists.NameRequired",
            fallbackMessage: "值列表名称不能为空。",
            code: "CONFIGCENTER_VALUE_LIST_NAME_REQUIRED");
    }
}
```

```csharp
public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
{
    var item = await _repository.GetByIdAsync(id, cancellationToken);
    if (item is null)
    {
        throw new UserMessageException(
            statusCode: StatusCodes.Status404NotFound,
            level: UserMessageLevel.Error,
            messageKey: "ValueLists.ItemNotFound",
            messageArgs: new Dictionary<string, object?> { ["id"] = id },
            fallbackMessage: $"值列表项 '{id}' 不存在。",
            code: "CONFIGCENTER_VALUE_LIST_NOT_FOUND");
    }

    await _repository.DeleteAsync(id, cancellationToken);
}
```

- [ ] **Step 5: 在 `ValueListController` 中统一返回契约**

```csharp
[HttpPost]
public async Task<ActionResult<ValueListItemDto>> Create(
    [FromBody] ValueListItemDto dto,
    CancellationToken cancellationToken = default)
{
    try
    {
        var item = await _service.CreateAsync(dto, User?.Identity?.Name, cancellationToken);
        return Ok(item);
    }
    catch (UserMessageException ex)
    {
        return StatusCode(ex.StatusCode, ex.ToDto());
    }
}

[HttpPut("{id}")]
public async Task<ActionResult<ValueListItemDto>> Update(
    string id,
    [FromBody] ValueListItemDto dto,
    CancellationToken cancellationToken = default)
{
    try
    {
        var item = await _service.UpdateAsync(id, dto, User?.Identity?.Name, cancellationToken);
        return Ok(item);
    }
    catch (UserMessageException ex)
    {
        return StatusCode(ex.StatusCode, ex.ToDto());
    }
}

[HttpDelete("{id}")]
public async Task<ActionResult> Delete(string id, CancellationToken cancellationToken = default)
{
    try
    {
        await _service.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
    catch (UserMessageException ex)
    {
        return StatusCode(ex.StatusCode, ex.ToDto());
    }
}
```

- [ ] **Step 6: 重新运行定向测试，确认通过**

Run:

```powershell
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter FullyQualifiedName~ValueListServiceTests -nr:false -v minimal
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter "FullyQualifiedName~ValueListControllerMessageContractTests|FullyQualifiedName~RuntimeViewMaterializerTests|FullyQualifiedName~UnifiedPluginPackagingScriptsTests" -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 7: 提交**

```bash
git add modules/ConfigCenter/metadata/dictionaries/ValueLists.dic.json server/modules/ConfigCenter/Services/ValueListService.cs server/modules/ConfigCenter/Controllers/ValueListController.cs tests/ConfigCenter.Tests/ValueListServiceTests.cs tests/Kernel.Tests/ValueListControllerMessageContractTests.cs tests/Kernel.Tests/UnifiedPluginPackagingScriptsTests.cs
git commit -m "功能：接入值列表后端用户消息契约"
```

### Task 3: 值列表前端翻译消费闭环

**Files:**
- Create: `frontend/packages/shell/src/__tests__/value-list-management-message-contract.test.tsx`
- Modify: `frontend/modules/config-center/src/pages/ValueListManagement.tsx`

- [ ] **Step 1: 先补 failing test，锁定“后端 DTO -> 前端翻译文案”闭环**

```tsx
// @vitest-environment jsdom

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { LocalizationProvider } from '@icp-platform/core';
import ValueListManagement from '../../../../modules/config-center/src/pages/ValueListManagement';

vi.mock('@icp-platform/core', async () => {
  const actual = await vi.importActual<typeof import('@icp-platform/core')>('@icp-platform/core');
  return {
    ...actual,
    usePermission: () => true,
  };
});

describe('ValueListManagement user message contract', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    globalThis.IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.appendChild(container);
    vi.stubGlobal('fetch', vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify([]), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        success: false,
        level: 'error',
        messageKey: 'ValueLists.CodeAlreadyExists',
        messageArgs: { code: 'STATUS_ACTIVE' },
        fallbackMessage: "值列表编码 'STATUS_ACTIVE' 已存在。",
      }), { status: 400, headers: { 'Content-Type': 'application/json' } })));
  });

  afterEach(async () => {
    if (root) {
      await act(async () => {
        root.unmount();
      });
    }
    container.remove();
    vi.unstubAllGlobals();
    globalThis.IS_REACT_ACT_ENVIRONMENT = false;
  });

  it('renders translated backend user message after save failure', async () => {
    await act(async () => {
      root = createRoot(container);
      root.render(
        <LocalizationProvider
          defaultLanguage="zhs"
          supportedLanguages={[
            { code: 'zhs', displayName: '简体中文' },
            { code: 'en', displayName: 'English' },
          ]}
          catalogs={{
            zhs: {
              'ValueLists.CodeAlreadyExists': '值列表编码 {code} 已存在。',
            },
            en: {
              'ValueLists.CodeAlreadyExists': 'Value list code {code} already exists.',
            },
          }}
        >
          <ValueListManagement />
        </LocalizationProvider>,
      );
    });

    await act(async () => {
      await Promise.resolve();
    });

    const createButton = Array.from(container.querySelectorAll('button')).find(button => button.textContent === '新建');
    await act(async () => {
      createButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    const codeInput = container.querySelector('input[id=\"编码\"]') as HTMLInputElement | null;
    const nameInput = container.querySelector('input[id=\"名称\"]') as HTMLInputElement | null;
    await act(async () => {
      codeInput!.value = 'STATUS_ACTIVE';
      codeInput?.dispatchEvent(new Event('change', { bubbles: true }));
      nameInput!.value = '启用';
      nameInput?.dispatchEvent(new Event('change', { bubbles: true }));
    });

    const saveButton = Array.from(container.querySelectorAll('button')).find(button => button.textContent === '保存');
    await act(async () => {
      saveButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    await act(async () => {
      await Promise.resolve();
    });

    expect(container.textContent).toContain('值列表编码 STATUS_ACTIVE 已存在。');
  });
});
```

- [ ] **Step 2: 跑前端定向测试，确认当前失败**

Run:

```powershell
pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/value-list-management-message-contract.test.tsx
```

Expected:

```text
FAIL
... 页面仍然只读取 message 字段 / 未使用 LocalizationProvider 翻译后端 DTO ...
```

- [ ] **Step 3: 在值列表页中消费契约并翻译**

```tsx
import { useEffect, useMemo, useState } from 'react';
import { useLocalization, usePermission, formatUserMessage } from '@icp-platform/core';
import { isUserMessageDto } from '@icp-platform/sdk';

export default function ValueListManagement() {
  const { t } = useLocalization();
  const canView = usePermission('ConfigCenter.Config.View');
  const canCreate = usePermission('ConfigCenter.Config.Create');
  const canEdit = usePermission('ConfigCenter.Config.Edit');
  const canDelete = usePermission('ConfigCenter.Config.Delete');

  async function readFailureText(response: Response, fallback: string): Promise<string> {
    try {
      const payload = await response.json();
      if (isUserMessageDto(payload)) {
        return formatUserMessage(payload, t);
      }

      if (payload && typeof payload === 'object' && typeof (payload as { message?: string }).message === 'string') {
        return (payload as { message: string }).message;
      }
    } catch {
    }

    return fallback;
  }

  async function submitForm() {
    if (!form.code.trim() || !form.name.trim()) {
      setFormError('编码和名称不能为空。');
      return;
    }

    try {
      setSubmitting(true);
      setFormError(null);

      const payload = {
        code: form.code.trim(),
        name: form.name.trim(),
        description: form.description.trim() || undefined
      };

      const response = await fetch(
        mode === 'create' ? '/api/value-lists' : `/api/value-lists/${selectedItem?.id}`,
        {
          method: mode === 'create' ? 'POST' : 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload)
        }
      );

      if (!response.ok) {
        throw new Error(await readFailureText(response, '值列表保存失败'));
      }

      await loadItems();
      closeEditor();
    } catch (submitError) {
      setFormError(submitError instanceof Error ? submitError.message : '值列表保存失败');
    } finally {
      setSubmitting(false);
    }
  }

  async function removeSelected() {
    if (!selectedItem) {
      return;
    }

    try {
      setSubmitting(true);
      setError(null);
      setConfirmOpen(false);

      const response = await fetch(`/api/value-lists/${selectedItem.id}`, {
        method: 'DELETE'
      });

      if (!response.ok) {
        throw new Error(await readFailureText(response, '值列表删除失败'));
      }

      setSelectedId(null);
      await loadItems();
      closeEditor();
    } catch (deleteError) {
      setError(deleteError instanceof Error ? deleteError.message : '值列表删除失败');
    } finally {
      setSubmitting(false);
    }
  }
}
```

- [ ] **Step 4: 重新运行前端定向测试，确认通过**

Run:

```powershell
pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/user-message.test.ts src/__tests__/value-list-management-message-contract.test.tsx
```

Expected:

```text
Passed!
```

- [ ] **Step 5: 提交**

```bash
git add frontend/modules/config-center/src/pages/ValueListManagement.tsx frontend/packages/shell/src/__tests__/value-list-management-message-contract.test.tsx
git commit -m "功能：前端消费值列表用户消息契约"
```

### Task 4: 验证、状态与范围收口

**Files:**
- Create: `docs/verification/user-visible-text-localization-foundation-package-2-verification.md`
- Modify: `docs/current-status.md`

- [ ] **Step 1: 执行完整验证命令**

Run:

```powershell
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter FullyQualifiedName~ValueListServiceTests -nr:false -v minimal
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter "FullyQualifiedName~UserMessageContractTests|FullyQualifiedName~ValueListControllerMessageContractTests|FullyQualifiedName~RuntimeViewMaterializerTests|FullyQualifiedName~UnifiedPluginPackagingScriptsTests" -nr:false -v minimal
pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/user-message.test.ts src/__tests__/value-list-management-message-contract.test.tsx
dotnet build .\ICP-Platform.sln -nr:false -v minimal
pnpm --dir .\frontend build
git status --short
```

Expected:

```text
Passed!
... git status 仅包含本次任务相关文件 ...
```

- [ ] **Step 2: 更新验证文档与当前状态**

```md
# 用户可见文本多语言基础第二包验证

## 范围

- 共享 `messageKey + messageArgs + level` 契约基座
- `ConfigCenter` 值列表后端异常契约
- 值列表前端翻译消费
- `ConfigCenter` 字典资源打包与 runtime manifest 投影验证

## 执行命令

1. `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter FullyQualifiedName~ValueListServiceTests -nr:false -v minimal`
2. `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter "FullyQualifiedName~UserMessageContractTests|FullyQualifiedName~ValueListControllerMessageContractTests|FullyQualifiedName~RuntimeViewMaterializerTests|FullyQualifiedName~UnifiedPluginPackagingScriptsTests" -nr:false -v minimal`
3. `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/user-message.test.ts src/__tests__/value-list-management-message-contract.test.tsx`
4. `dotnet build .\ICP-Platform.sln -nr:false -v minimal`
5. `pnpm --dir .\frontend build`

## 结果

- 所有定向测试通过
- 后端与前端构建通过
- `ConfigCenter` 字典资源已进入统一插件包，并出现在宿主 runtime `localization manifest`

## 已知限制

- 本包只试点 `ConfigCenter` 值列表，不代表全仓库历史接口已全部迁移
- `fieldKey / actionKey / statusKey` 只在类型层保留，本轮未在 UI 中展开独立消费
```

```md
### Post-Phase-6：用户可见文本多语言基础（第二包：后端用户消息契约）

状态：计划中 / 实施后更新为已完成

本包收口范围：

- 共享 `messageKey + messageArgs + level` 契约基座
- `ConfigCenter` 值列表创建、编辑、删除失败链路
- 前端值列表页按 runtime localization catalogs 翻译后端消息

明确不在本包范围：

- `PluginOps` 等 Host 管理模块消息契约迁移
- `Organization`、`Authorization` 等历史接口批量改造
- 后端默认直接返回已翻译文本
```

- [ ] **Step 3: 提交并推送**

```bash
git add docs/current-status.md docs/verification/user-visible-text-localization-foundation-package-2-verification.md
git commit -m "验证：收口后端用户消息契约第二包文档"
git push origin HEAD
```

## Self-Review

- **Spec coverage:** 本计划覆盖设计文档中的后端用户消息主契约：共享 `messageKey + messageArgs + level` 类型、前端按当前语言翻译展示、`fallbackMessage` 兼容字段保留、模块自有字典 authoring 真源、测试和验证收口。未纳入 `PluginOps`、`Organization`、`Authorization` 等历史接口的批量迁移，避免把第二包扩成全仓库重写。
- **Placeholder scan:** 计划中每个任务都给出明确文件、代码片段、命令和提交信息，没有占位式描述。
- **Type consistency:** 全文统一使用 `UserMessageDto`、`UserMessageException`、`formatUserMessage`、`ValueLists.CodeAlreadyExists` 这些命名；前后端都使用同一组字段名：`success`、`level`、`messageKey`、`messageArgs`、`fallbackMessage`、`code`、`fieldKey`、`actionKey`、`statusKey`。
