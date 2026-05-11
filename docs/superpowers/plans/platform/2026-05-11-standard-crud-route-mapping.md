# 标准 CRUD 路由自动映射 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让标准 CRUD 资源通过 descriptor 自动暴露模块语义路由，并退场人员中心后端旧 Controller/Service/Repository 样板。

**Architecture:** 平台新增标准 CRUD 资源注册表和 `MapStandardCrudResources()` 路由映射器，`StandardCrudDescriptor.Route` 成为标准 API 路由唯一真源。Organization 只注册 Person descriptor，标准 CRUD 路由由平台自动映射，人员账号绑定拆为专属 Controller 与 Service。

**Tech Stack:** .NET 10 / ASP.NET Core Minimal API / Microsoft.Extensions.DependencyInjection / xUnit / React + TypeScript + Vite

---

## 文件结构与职责

**Create**

- `server/platform-crud/Abstractions/IStandardCrudResourceRegistry.cs`  
  标准 CRUD 资源只读注册表接口。
- `server/platform-crud/Runtime/StandardCrudResourceRegistry.cs`  
  聚合 DI 中注册的 descriptor，执行 descriptor、resourceKey、route 静态校验。
- `server/platform-crud/Extensions/StandardCrudEndpointRouteBuilderExtensions.cs`  
  将注册表中的 descriptor 映射为标准 CRUD Minimal API route handlers。
- `tests/PlatformCrud.Tests/StandardCrudResourceRegistryTests.cs`  
  覆盖资源注册、重复 resourceKey、重复 route 与 route 查找。
- `tests/PlatformCrud.Tests/StandardCrudEndpointRouteBuilderTests.cs`  
  覆盖自动映射后的 list/detail/create/update/disable/delete/options HTTP 行为。
- `server/modules/Organization/Services/IPersonUserLinkService.cs`  
  人员账号绑定专属服务接口。
- `server/modules/Organization/Services/PersonUserLinkService.cs`  
  承接人员账号绑定写入规则，不承接人员标准 CRUD。
- `server/modules/Organization/Controllers/PersonUserLinksController.cs`  
  只暴露 `user-options` 与 `{id}/user-link` 非标准人员能力。
- `tests/Organization.Tests/PersonUserLinkServiceTests.cs`  
  覆盖绑定账号专属规则。
- `tests/Organization.Tests/PersonStandardCrudResourceTests.cs`  
  覆盖 Person descriptor、扩展点和 Organization 注册表接入。
- `tests/Organization.Tests/PersonUserLinksControllerTests.cs`  
  覆盖人员账号绑定专属 Controller 路由。

**Modify**

- `server/platform-crud/Extensions/StandardCrudServiceCollectionExtensions.cs`  
  增加 `AddStandardCrudResource()`，并注册 `IStandardCrudResourceRegistry`。
- `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`  
  增加 `Microsoft.AspNetCore.TestHost` 以验证 Minimal API route mapping。
- `apps/host-api/Program.cs`  
  调用 `app.MapStandardCrudResources()`。
- `apps/host-api/HostApi.csproj`  
  显式引用 `PlatformCrud.csproj`，避免 Host 通过 Organization 的传递引用访问平台路由扩展。
- `server/modules/Organization/OrganizationModule.cs`  
  注册 Person descriptor，移除人员标准 CRUD service/repository，注册人员账号绑定服务。
- `frontend/modules/organization/src/services/organizationService.ts`  
  人员标准 CRUD 客户端改为标准返回模型，不再依赖旧 DTO 兼容层。
- `frontend/modules/organization/src/standard-list/PersonListStandardListEntry.tsx`
- `frontend/modules/organization/src/pages/PersonCreatePage.tsx`
- `frontend/modules/organization/src/pages/PersonDetailPage.tsx`
- `frontend/modules/organization/src/components/PersonEditorForm.tsx`
- `frontend/modules/organization/src/components/PersonUserLinkCard.tsx`
- `frontend/modules/organization/src/components/AuthorizationSummaryCard.tsx`  
  更新前端类型名和保存/停用返回模型消费。
- `docs/verification/organization-module-verification.md`  
  记录标准 CRUD 自动映射与人员旧样板退场验证。

**Delete**

- `server/modules/Organization/Controllers/PersonsController.cs`
- `server/modules/Organization/Services/IPersonService.cs`
- `server/modules/Organization/Services/PersonService.cs`
- `server/modules/Organization/Repositories/IPersonRepository.cs`
- `server/modules/Organization/Persistence/Ef/EfPersonRepository.cs`
- `server/modules/Organization/DTOs/PersonListItemDto.cs`
- `server/modules/Organization/DTOs/PersonDetailDto.cs`
- `server/modules/Organization/DTOs/PersonSummaryBlockDto.cs`
- `server/modules/Organization/Models/Person.cs`
- `tests/Organization.Tests/PersonListContractTests.cs`
- `tests/Organization.Tests/PersonServiceTests.cs`

---

### Task 1: 平台标准 CRUD 资源注册表

**Files:**
- Create: `server/platform-crud/Abstractions/IStandardCrudResourceRegistry.cs`
- Create: `server/platform-crud/Runtime/StandardCrudResourceRegistry.cs`
- Modify: `server/platform-crud/Extensions/StandardCrudServiceCollectionExtensions.cs`
- Create: `tests/PlatformCrud.Tests/StandardCrudResourceRegistryTests.cs`
- Test: `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`

- [x] **Step 1: 写资源注册表失败测试**

Create `tests/PlatformCrud.Tests/StandardCrudResourceRegistryTests.cs`:

```csharp
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Runtime;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudResourceRegistryTests
{
    [Fact]
    public void GetAll_ReturnsRegisteredDescriptors()
    {
        var descriptor = Descriptor();
        var registry = new StandardCrudResourceRegistry([descriptor]);

        var registered = Assert.Single(registry.GetAll());

        Assert.Same(descriptor, registered);
        Assert.Same(descriptor, registry.FindByResourceKey("organization.person"));
        Assert.Same(descriptor, registry.FindByRoute("api/organization/persons"));
        Assert.Same(descriptor, registry.FindByRoute("/api/organization/persons/"));
    }

    [Fact]
    public void Constructor_WhenResourceKeyDuplicated_ThrowsStableMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new StandardCrudResourceRegistry(
            [
                Descriptor(),
                Descriptor(route: "api/organization/persons-copy")
            ]));

        Assert.Equal("标准 CRUD 资源 organization.person 重复注册。", ex.Message);
    }

    [Fact]
    public void Constructor_WhenRouteDuplicated_ThrowsStableMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new StandardCrudResourceRegistry(
            [
                Descriptor(resourceKey: "organization.person"),
                Descriptor(resourceKey: "organization.employee", route: "/api/organization/persons/")
            ]));

        Assert.Equal("标准 CRUD 路由 api/organization/persons 被 organization.person 与 organization.employee 重复声明。", ex.Message);
    }

    private static StandardCrudDescriptor Descriptor(
        string resourceKey = "organization.person",
        string route = "api/organization/persons") => new()
    {
        ResourceKey = resourceKey,
        ModuleId = "Organization",
        ModelName = "Person",
        Route = route,
        IdField = "EID",
        DisplayField = "ENAME",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "PersonNo", "ENAME"],
            DetailFields: ["EID", "PersonNo", "ENAME"],
            CreateFields: ["PersonNo", "ENAME"],
            UpdateFields: ["ENAME"]),
        DeletePolicy = StandardCrudDeletePolicy.None
    };
}
```

- [x] **Step 2: 运行注册表测试确认失败**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrudResourceRegistryTests -nr:false -v minimal
```

Expected: FAIL，提示 `StandardCrudResourceRegistry` 或 `IStandardCrudResourceRegistry` 类型不存在。

- [x] **Step 3: 新增资源注册表接口**

Create `server/platform-crud/Abstractions/IStandardCrudResourceRegistry.cs`:

```csharp
using ICP.Platform.Crud.Descriptors;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudResourceRegistry
{
    /// <summary>
    /// 返回所有已显式注册的标准 CRUD 资源；返回集合只读，避免运行期修改路由真源。
    /// </summary>
    IReadOnlyList<StandardCrudDescriptor> GetAll();

    /// <summary>
    /// 按稳定资源 key 查找 descriptor；未注册时返回 null，由调用方决定是否 fail closed。
    /// </summary>
    StandardCrudDescriptor? FindByResourceKey(string resourceKey);

    /// <summary>
    /// 按标准 CRUD 业务路由查找 descriptor；route 会先规范化，避免斜杠差异形成双真源。
    /// </summary>
    StandardCrudDescriptor? FindByRoute(string route);
}
```

- [x] **Step 4: 新增资源注册表实现**

Create `server/platform-crud/Runtime/StandardCrudResourceRegistry.cs`:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;

namespace ICP.Platform.Crud.Runtime;

public sealed class StandardCrudResourceRegistry : IStandardCrudResourceRegistry
{
    private readonly IReadOnlyList<StandardCrudDescriptor> _descriptors;
    private readonly IReadOnlyDictionary<string, StandardCrudDescriptor> _byResourceKey;
    private readonly IReadOnlyDictionary<string, StandardCrudDescriptor> _byRoute;

    public StandardCrudResourceRegistry(IEnumerable<StandardCrudDescriptor> descriptors)
    {
        var descriptorList = descriptors.ToList();
        var byResourceKey = new Dictionary<string, StandardCrudDescriptor>(StringComparer.Ordinal);
        var byRoute = new Dictionary<string, StandardCrudDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in descriptorList)
        {
            StandardCrudDescriptorValidator.Validate(descriptor);

            if (byResourceKey.ContainsKey(descriptor.ResourceKey))
            {
                throw new InvalidOperationException($"标准 CRUD 资源 {descriptor.ResourceKey} 重复注册。");
            }

            var normalizedRoute = NormalizeRoute(descriptor.Route);
            if (byRoute.TryGetValue(normalizedRoute, out var existing))
            {
                throw new InvalidOperationException(
                    $"标准 CRUD 路由 {normalizedRoute} 被 {existing.ResourceKey} 与 {descriptor.ResourceKey} 重复声明。");
            }

            byResourceKey[descriptor.ResourceKey] = descriptor;
            byRoute[normalizedRoute] = descriptor;
        }

        _descriptors = descriptorList;
        _byResourceKey = byResourceKey;
        _byRoute = byRoute;
    }

    public IReadOnlyList<StandardCrudDescriptor> GetAll() => _descriptors;

    public StandardCrudDescriptor? FindByResourceKey(string resourceKey) =>
        _byResourceKey.TryGetValue(resourceKey, out var descriptor) ? descriptor : null;

    public StandardCrudDescriptor? FindByRoute(string route)
    {
        var normalizedRoute = NormalizeRoute(route);
        return _byRoute.TryGetValue(normalizedRoute, out var descriptor) ? descriptor : null;
    }

    private static string NormalizeRoute(string route)
    {
        var normalized = route.Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("标准 CRUD 路由不能为空。");
        }

        return normalized;
    }
}
```

- [x] **Step 5: 增加 DI 注册扩展**

Modify `server/platform-crud/Extensions/StandardCrudServiceCollectionExtensions.cs`:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Crud.Persistence;
using ICP.Platform.Crud.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ICP.Platform.Crud.Extensions;

public static class StandardCrudServiceCollectionExtensions
{
    /// <summary>
    /// 注册平台对象 ID 生成能力；该能力独立于标准 CRUD，供模块 service 写入 EID 时复用。
    /// </summary>
    public static IServiceCollection AddPlatformObjectIdGeneration(this IServiceCollection services)
    {
        services.TryAddSingleton<IPlatformObjectIdGenerator, PlatformObjectIdGenerator>();
        return services;
    }

    /// <summary>
    /// 注册标准 CRUD 内核基础服务；模块扩展点仍由各模块显式注册，避免平台默认耦合具体业务。
    /// </summary>
    public static IServiceCollection AddStandardCrudKernel(this IServiceCollection services)
    {
        services.AddPlatformObjectIdGeneration();
        services.TryAddSingleton<IStandardCrudResourceRegistry, StandardCrudResourceRegistry>();
        services.AddScoped<IStandardCrudRepository, EfStandardCrudRepository>();
        services.AddScoped<IStandardCrudService, StandardCrudService>();
        services.AddScoped<StandardCrudEndpoint>();
        return services;
    }

    /// <summary>
    /// 显式注册一个标准 CRUD 资源；descriptor 是标准路由和字段边界的唯一真源。
    /// </summary>
    public static IServiceCollection AddStandardCrudResource(
        this IServiceCollection services,
        StandardCrudDescriptor descriptor)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
        services.TryAddSingleton<IStandardCrudResourceRegistry, StandardCrudResourceRegistry>();
        services.AddSingleton(descriptor);
        return services;
    }
}
```

- [x] **Step 6: 运行注册表测试确认通过**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrudResourceRegistryTests -nr:false -v minimal
```

Expected: PASS。

- [x] **Step 7: 提交**

```powershell
git add .\server\platform-crud\Abstractions\IStandardCrudResourceRegistry.cs .\server\platform-crud\Runtime\StandardCrudResourceRegistry.cs .\server\platform-crud\Extensions\StandardCrudServiceCollectionExtensions.cs .\tests\PlatformCrud.Tests\StandardCrudResourceRegistryTests.cs
git commit -m "功能：新增标准CRUD资源注册表"
```

---

### Task 2: 标准 CRUD 自动路由映射器

**Files:**
- Modify: `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`
- Create: `tests/PlatformCrud.Tests/StandardCrudEndpointRouteBuilderTests.cs`
- Create: `server/platform-crud/Extensions/StandardCrudEndpointRouteBuilderExtensions.cs`
- Test: `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`

- [ ] **Step 1: 增加 TestHost 包引用**

Modify `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj` package references:

```xml
<PackageReference Include="Microsoft.AspNetCore.TestHost" Version="10.0.1" />
```

- [ ] **Step 2: 写自动路由映射失败测试**

Create `tests/PlatformCrud.Tests/StandardCrudEndpointRouteBuilderTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudEndpointRouteBuilderTests
{
    [Fact]
    public async Task MapStandardCrudResources_MapsListRouteFromDescriptor()
    {
        var recording = new RecordingStandardCrudService();
        await using var app = BuildApp(recording);
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/api/organization/persons?page=2&pageSize=5&keyword=张");

        response.EnsureSuccessStatusCode();
        Assert.Equal(2, recording.LastListQuery!.Page);
        Assert.Equal(5, recording.LastListQuery.PageSize);
        Assert.Equal("张", recording.LastListQuery.Filters!["keyword"]);
    }

    [Fact]
    public async Task MapStandardCrudResources_MapsReadOnlyStandardRoutes()
    {
        var recording = new RecordingStandardCrudService();
        await using var app = BuildApp(recording);
        await app.StartAsync();
        var client = app.GetTestClient();

        var detailResponse = await client.GetAsync("/api/organization/persons/Person_00000000000000000000000000000001");
        var optionsResponse = await client.GetAsync("/api/organization/persons/options?pageSize=7&keyword=张");

        detailResponse.EnsureSuccessStatusCode();
        optionsResponse.EnsureSuccessStatusCode();
        Assert.Equal("Person_00000000000000000000000000000001", recording.LastDetailId);
        Assert.Equal(7, recording.LastOptionsQuery!.PageSize);
        Assert.Equal("张", recording.LastOptionsQuery.Filters!["keyword"]);
    }

    [Fact]
    public async Task MapStandardCrudResources_CreateReturnsDetail()
    {
        var recording = new RecordingStandardCrudService
        {
            MutationResult = StandardCrudMutationResult.Success(Detail("Person_00000000000000000000000000000001"))
        };
        await using var app = BuildApp(recording);
        await app.StartAsync();

        var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/organization/persons",
            new Dictionary<string, object?> { ["PersonNo"] = "P001", ["ENAME"] = "张三" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(nameof(IStandardCrudService.CreateAsync), recording.LastMutation);
        Assert.Equal("P001", recording.LastMutationValues!["PersonNo"]!.ToString());
    }

    [Fact]
    public async Task MapStandardCrudResources_MapsUpdateDisableAndDeleteRoutes()
    {
        var recording = new RecordingStandardCrudService();
        await using var app = BuildApp(recording);
        await app.StartAsync();
        var client = app.GetTestClient();

        var updateResponse = await client.PutAsJsonAsync(
            "/api/organization/persons/Person_00000000000000000000000000000001",
            new Dictionary<string, object?> { ["ENAME"] = "李四" });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(nameof(IStandardCrudService.UpdateAsync), recording.LastMutation);
        Assert.Equal("李四", recording.LastMutationValues!["ENAME"]!.ToString());

        var disableResponse = await client.PostAsync(
            "/api/organization/persons/Person_00000000000000000000000000000001/disable",
            content: null);

        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);
        Assert.Equal(nameof(IStandardCrudService.DisableAsync), recording.LastMutation);

        var deleteResponse = await client.DeleteAsync("/api/organization/persons/Person_00000000000000000000000000000001");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(nameof(IStandardCrudService.DeleteAsync), recording.LastMutation);
    }

    [Fact]
    public async Task MapStandardCrudResources_WhenMutationFails_ReturnsStandardErrorStatusCode()
    {
        var recording = new RecordingStandardCrudService
        {
            MutationResult = StandardCrudMutationResult.Failure(new StandardCrudError
            {
                StatusCode = 409,
                Code = "Organization.Person.PersonNoDuplicated",
                Message = "人员工号必须唯一。",
                ReasonKey = "organization.person.personNoDuplicated"
            })
        };
        await using var app = BuildApp(recording);
        await app.StartAsync();

        var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/organization/persons",
            new Dictionary<string, object?> { ["PersonNo"] = "P001", ["ENAME"] = "张三" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private static WebApplication BuildApp(RecordingStandardCrudService recording)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IStandardCrudService>(recording);
        builder.Services.AddScoped<StandardCrudEndpoint>();
        builder.Services.AddStandardCrudResource(Descriptor());

        var app = builder.Build();
        app.MapStandardCrudResources();
        return app;
    }

    private static StandardCrudDescriptor Descriptor() => new()
    {
        ResourceKey = "organization.person",
        ModuleId = "Organization",
        ModelName = "Person",
        Route = "api/organization/persons",
        IdField = "EID",
        DisplayField = "ENAME",
        EnabledField = "IsEnabled",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "PersonNo", "ENAME", "IsEnabled"],
            DetailFields: ["EID", "PersonNo", "ENAME", "IsEnabled"],
            CreateFields: ["PersonNo", "ENAME", "IsEnabled"],
            UpdateFields: ["ENAME", "IsEnabled"]),
        OptionLabelFields = ["PersonNo", "ENAME"],
        DeletePolicy = StandardCrudDeletePolicy.Conditional
    };

    private static StandardCrudDetail Detail(string id) => new()
    {
        Fields = new Dictionary<string, object?>
        {
            ["EID"] = id,
            ["PersonNo"] = "P001",
            ["ENAME"] = "张三",
            ["IsEnabled"] = true
        }
    };

    private sealed class RecordingStandardCrudService : IStandardCrudService
    {
        public StandardCrudListQuery? LastListQuery { get; private set; }
        public StandardCrudListQuery? LastOptionsQuery { get; private set; }
        public string? LastDetailId { get; private set; }
        public string? LastMutation { get; private set; }
        public IReadOnlyDictionary<string, object?>? LastMutationValues { get; private set; }
        public StandardCrudMutationResult MutationResult { get; init; } = StandardCrudMutationResult.Success(Detail("Person_00000000000000000000000000000001"));

        public Task<StandardCrudListResult> ListAsync(StandardCrudDescriptor descriptor, StandardCrudListQuery query, CancellationToken cancellationToken = default)
        {
            LastListQuery = query;
            return Task.FromResult(new StandardCrudListResult([], 0, query.Page, query.PageSize, query.SortBy, query.SortDirection));
        }

        public Task<StandardCrudDetail?> GetDetailAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default)
        {
            LastDetailId = id;
            return Task.FromResult<StandardCrudDetail?>(Detail(id));
        }

        public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(StandardCrudDescriptor descriptor, StandardCrudListQuery query, CancellationToken cancellationToken = default)
        {
            LastOptionsQuery = query;
            return Task.FromResult<IReadOnlyList<StandardCrudOptionItem>>([]);
        }

        public Task<StandardCrudMutationResult> CreateAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
        {
            LastMutation = nameof(CreateAsync);
            LastMutationValues = values;
            return Task.FromResult(MutationResult);
        }

        public Task<StandardCrudMutationResult> UpdateAsync(StandardCrudDescriptor descriptor, string id, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
        {
            LastMutation = nameof(UpdateAsync);
            LastMutationValues = values;
            return Task.FromResult(MutationResult);
        }

        public Task<StandardCrudMutationResult> DisableAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default)
        {
            LastMutation = nameof(DisableAsync);
            return Task.FromResult(MutationResult);
        }

        public Task<StandardCrudMutationResult> DeleteAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default)
        {
            LastMutation = nameof(DeleteAsync);
            return Task.FromResult(StandardCrudMutationResult.Success());
        }
    }
}
```

- [ ] **Step 3: 运行路由映射测试确认失败**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrudEndpointRouteBuilderTests -nr:false -v minimal
```

Expected: FAIL，提示 `MapStandardCrudResources` 不存在。

- [ ] **Step 4: 新增自动路由映射器**

Create `server/platform-crud/Extensions/StandardCrudEndpointRouteBuilderExtensions.cs`:

```csharp
using System.Text.Json;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Crud.Extensions;

public static class StandardCrudEndpointRouteBuilderExtensions
{
    private static readonly HashSet<string> StandardQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "page",
        "pageSize",
        "sortBy",
        "sortDirection"
    };

    /// <summary>
    /// 按 descriptor.Route 自动映射标准 CRUD API；特殊业务动作仍应留在模块 Controller。
    /// </summary>
    public static IEndpointRouteBuilder MapStandardCrudResources(this IEndpointRouteBuilder endpoints)
    {
        var registry = endpoints.ServiceProvider.GetRequiredService<IStandardCrudResourceRegistry>();
        foreach (var descriptor in registry.GetAll())
        {
            MapResource(endpoints, descriptor);
        }

        return endpoints;
    }

    private static void MapResource(IEndpointRouteBuilder endpoints, StandardCrudDescriptor descriptor)
    {
        var route = "/" + descriptor.Route.Trim().Trim('/');

        endpoints.MapGet(route, async (
            HttpContext context,
            StandardCrudEndpoint endpoint,
            CancellationToken cancellationToken) =>
        {
            var result = await endpoint.ListAsync(descriptor, BuildListQuery(context.Request.Query), cancellationToken);
            return Results.Ok(result);
        });

        endpoints.MapGet(route + "/options", async (
            HttpContext context,
            StandardCrudEndpoint endpoint,
            CancellationToken cancellationToken) =>
        {
            var result = await endpoint.OptionsAsync(descriptor, BuildListQuery(context.Request.Query), cancellationToken);
            return Results.Ok(result);
        });

        endpoints.MapGet(route + "/{id}", async (
            string id,
            StandardCrudEndpoint endpoint,
            CancellationToken cancellationToken) =>
        {
            var detail = await endpoint.GetDetailAsync(descriptor, id, cancellationToken);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        endpoints.MapPost(route, async (
            HttpContext context,
            StandardCrudEndpoint endpoint,
            CancellationToken cancellationToken) =>
        {
            var values = await ReadValuesAsync(context, cancellationToken);
            var result = await endpoint.CreateAsync(descriptor, values, cancellationToken);
            return ToMutationResult(result, detail => Results.Created(BuildCreatedLocation(route, descriptor, detail), detail));
        });

        endpoints.MapPut(route + "/{id}", async (
            string id,
            HttpContext context,
            StandardCrudEndpoint endpoint,
            CancellationToken cancellationToken) =>
        {
            var values = await ReadValuesAsync(context, cancellationToken);
            var result = await endpoint.UpdateAsync(descriptor, id, values, cancellationToken);
            return ToMutationResult(result, Results.Ok);
        });

        endpoints.MapPost(route + "/{id}/disable", async (
            string id,
            StandardCrudEndpoint endpoint,
            CancellationToken cancellationToken) =>
        {
            var result = await endpoint.DisableAsync(descriptor, id, cancellationToken);
            return ToMutationResult(result, Results.Ok);
        });

        endpoints.MapDelete(route + "/{id}", async (
            string id,
            StandardCrudEndpoint endpoint,
            CancellationToken cancellationToken) =>
        {
            var result = await endpoint.DeleteAsync(descriptor, id, cancellationToken);
            return result.Succeeded
                ? Results.NoContent()
                : ToErrorResult(result.Error!);
        });
    }

    private static StandardCrudListQuery BuildListQuery(IQueryCollection query)
    {
        var filters = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var pair in query)
        {
            if (!StandardQueryKeys.Contains(pair.Key))
            {
                filters[pair.Key] = pair.Value.ToString();
            }
        }

        return new StandardCrudListQuery(
            Page: ReadInt(query, "page", 1),
            PageSize: ReadInt(query, "pageSize", 20),
            SortBy: ReadString(query, "sortBy"),
            SortDirection: ReadString(query, "sortDirection"),
            Filters: filters);
    }

    private static async Task<IReadOnlyDictionary<string, object?>> ReadValuesAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var values = await JsonSerializer.DeserializeAsync<Dictionary<string, JsonElement>>(
            context.Request.Body,
            cancellationToken: cancellationToken);

        return values is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : values.ToDictionary(pair => pair.Key, pair => NormalizeJsonValue(pair.Value), StringComparer.Ordinal);
    }

    private static object? NormalizeJsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when value.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => value.GetRawText()
        };
    }

    private static IResult ToMutationResult(
        StandardCrudMutationResult result,
        Func<StandardCrudDetail, IResult> success)
    {
        if (!result.Succeeded)
        {
            return ToErrorResult(result.Error!);
        }

        if (result.Detail is null)
        {
            return ToErrorResult(new StandardCrudError
            {
                StatusCode = StatusCodes.Status500InternalServerError,
                Code = "StandardCrud.MutationDetailMissing",
                Message = "标准 CRUD 写入成功但未返回详情。",
                ReasonKey = "standardCrud.mutationDetailMissing"
            });
        }

        return success(result.Detail);
    }

    private static IResult ToErrorResult(StandardCrudError error) =>
        Results.Json(error, statusCode: error.StatusCode);

    private static string BuildCreatedLocation(
        string route,
        StandardCrudDescriptor descriptor,
        StandardCrudDetail detail)
    {
        var id = detail.Fields.TryGetValue(descriptor.IdField, out var value)
            ? value?.ToString()
            : null;

        return string.IsNullOrWhiteSpace(id) ? route : $"{route}/{id}";
    }

    private static int ReadInt(IQueryCollection query, string key, int fallback) =>
        query.TryGetValue(key, out var value) && int.TryParse(value.ToString(), out var parsed)
            ? parsed
            : fallback;

    private static string? ReadString(IQueryCollection query, string key) =>
        query.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value.ToString())
            ? value.ToString()
            : null;
}
```

- [ ] **Step 5: 运行路由映射测试确认通过**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrudEndpointRouteBuilderTests -nr:false -v minimal
```

Expected: PASS。

- [ ] **Step 6: 运行平台 CRUD 全量测试**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
```

Expected: PASS。

- [ ] **Step 7: 提交**

```powershell
git add .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj .\tests\PlatformCrud.Tests\StandardCrudEndpointRouteBuilderTests.cs .\server\platform-crud\Extensions\StandardCrudEndpointRouteBuilderExtensions.cs
git commit -m "功能：新增标准CRUD自动路由映射"
```

---

### Task 3: 人员账号绑定专属服务

**Files:**
- Create: `server/modules/Organization/Services/IPersonUserLinkService.cs`
- Create: `server/modules/Organization/Services/PersonUserLinkService.cs`
- Create: `tests/Organization.Tests/PersonUserLinkServiceTests.cs`
- Test: `tests/Organization.Tests/Organization.Tests.csproj`

- [ ] **Step 1: 写人员账号绑定服务失败测试**

Create `tests/Organization.Tests/PersonUserLinkServiceTests.cs`:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Modules.Organization.Services;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class PersonUserLinkServiceTests
{
    [Fact]
    public async Task LinkUserAsync_WhenPersonExists_AssignsPersonUserLinkPrefixedId()
    {
        var service = BuildService(existingPersonIds: ["Person_00000000000000000000000000000001"]);

        var link = await service.LinkUserAsync(
            "Person_00000000000000000000000000000001",
            new LinkPersonUserRequest { UserId = "user-1", LinkedBy = "tester" });

        Assert.Equal("PersonUserLink_00000000000000000000000000000001", link.Id);
        Assert.Equal("Person_00000000000000000000000000000001", link.PersonId);
        Assert.Equal("user-1", link.UserId);
        Assert.Equal("tester", link.LinkedBy);
    }

    [Fact]
    public async Task LinkUserAsync_WhenPersonMissing_ThrowsStableError()
    {
        var service = BuildService(existingPersonIds: []);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LinkUserAsync(
                "Person_missing",
                new LinkPersonUserRequest { UserId = "user-1" }));

        Assert.Equal("人员 'Person_missing' 不存在。", ex.Message);
    }

    [Fact]
    public async Task LinkUserAsync_WhenUserAlreadyBound_ThrowsStableError()
    {
        var repository = new InMemoryPersonUserLinkRepository();
        await repository.UpsertAsync(new PersonUserLink
        {
            Id = "PersonUserLink_existing",
            PersonId = "Person_existing",
            UserId = "user-1",
            LinkedAt = DateTime.UtcNow,
            LinkedBy = "tester"
        });
        var service = BuildService(repository, ["Person_target"]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LinkUserAsync(
                "Person_target",
                new LinkPersonUserRequest { UserId = "user-1" }));

        Assert.Equal("该账号已绑定其他人员。", ex.Message);
    }

    private static IPersonUserLinkService BuildService(IReadOnlyList<string> existingPersonIds) =>
        BuildService(new InMemoryPersonUserLinkRepository(), existingPersonIds);

    private static IPersonUserLinkService BuildService(
        IPersonUserLinkRepository repository,
        IReadOnlyList<string> existingPersonIds) =>
        new PersonUserLinkService(
            repository,
            new FakeIdentityUserDirectory(),
            new FixedObjectIdGenerator("PersonUserLink_00000000000000000000000000000001"),
            new FakeStandardCrudResourceRegistry(),
            new FakeStandardCrudService(existingPersonIds));

    private sealed class FakeStandardCrudResourceRegistry : IStandardCrudResourceRegistry
    {
        public IReadOnlyList<StandardCrudDescriptor> GetAll() => [Descriptor()];
        public StandardCrudDescriptor? FindByResourceKey(string resourceKey) => resourceKey == "organization.person" ? Descriptor() : null;
        public StandardCrudDescriptor? FindByRoute(string route) => null;

        private static StandardCrudDescriptor Descriptor() => new()
        {
            ResourceKey = "organization.person",
            ModuleId = "Organization",
            ModelName = "Person",
            Route = "api/organization/persons",
            IdField = "EID",
            DisplayField = "ENAME",
            FieldSet = new StandardCrudFieldSet(
                ListFields: ["EID", "PersonNo", "ENAME"],
                DetailFields: ["EID", "PersonNo", "ENAME"],
                CreateFields: ["PersonNo", "ENAME"],
                UpdateFields: ["ENAME"])
        };
    }

    private sealed class FakeStandardCrudService : IStandardCrudService
    {
        private readonly IReadOnlySet<string> _existingPersonIds;

        public FakeStandardCrudService(IEnumerable<string> existingPersonIds)
        {
            _existingPersonIds = existingPersonIds.ToHashSet(StringComparer.Ordinal);
        }

        public Task<StandardCrudDetail?> GetDetailAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_existingPersonIds.Contains(id)
                ? new StandardCrudDetail { Fields = new Dictionary<string, object?> { ["EID"] = id } }
                : null);

        public Task<StandardCrudListResult> ListAsync(StandardCrudDescriptor descriptor, StandardCrudListQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(StandardCrudDescriptor descriptor, StandardCrudListQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StandardCrudMutationResult> CreateAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StandardCrudMutationResult> UpdateAsync(StandardCrudDescriptor descriptor, string id, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StandardCrudMutationResult> DisableAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StandardCrudMutationResult> DeleteAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeIdentityUserDirectory : IIdentityUserDirectory
    {
        public Task EnsureUserExistsAsync(string userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<IdentityUserOptionDto>> SearchAsync(string keyword, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IdentityUserOptionDto>>([]);
    }

    private sealed class FixedObjectIdGenerator : IPlatformObjectIdGenerator
    {
        private readonly string _id;

        public FixedObjectIdGenerator(string id)
        {
            _id = id;
        }

        public string NewId(string objectName) => _id;
    }

    private sealed class InMemoryPersonUserLinkRepository : IPersonUserLinkRepository
    {
        private readonly List<PersonUserLink> _links = [];

        public Task<PersonUserLink?> GetByPersonIdAsync(string personId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_links.FirstOrDefault(link => link.PersonId == personId));

        public Task<bool> ExistsByUserIdAsync(string userId, string? excludedPersonId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(_links.Any(link => link.UserId == userId && link.PersonId != excludedPersonId));

        public Task UpsertAsync(PersonUserLink link, CancellationToken cancellationToken = default)
        {
            _links.RemoveAll(existing => existing.PersonId == link.PersonId);
            _links.Add(link);
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 2: 运行人员账号绑定服务测试确认失败**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter PersonUserLinkServiceTests -nr:false -v minimal
```

Expected: FAIL，提示 `IPersonUserLinkService` 或 `PersonUserLinkService` 不存在。

- [ ] **Step 3: 新增人员账号绑定服务接口**

Create `server/modules/Organization/Services/IPersonUserLinkService.cs`:

```csharp
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;

namespace ICP.Platform.Modules.Organization.Services;

public interface IPersonUserLinkService
{
    /// <summary>
    /// 将人员绑定到身份账号；人员存在性通过标准 CRUD 资源校验，不依赖旧 Person 仓储。
    /// </summary>
    Task<PersonUserLinkDto> LinkUserAsync(
        string personId,
        LinkPersonUserRequest request,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: 新增人员账号绑定服务实现**

Create `server/modules/Organization/Services/PersonUserLinkService.cs`:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;

namespace ICP.Platform.Modules.Organization.Services;

public sealed class PersonUserLinkService : IPersonUserLinkService
{
    private const string PersonResourceKey = "organization.person";
    private readonly IPersonUserLinkRepository _personUserLinkRepository;
    private readonly IIdentityUserDirectory _identityUserDirectory;
    private readonly IPlatformObjectIdGenerator _objectIdGenerator;
    private readonly IStandardCrudResourceRegistry _crudResourceRegistry;
    private readonly IStandardCrudService _standardCrudService;

    public PersonUserLinkService(
        IPersonUserLinkRepository personUserLinkRepository,
        IIdentityUserDirectory identityUserDirectory,
        IPlatformObjectIdGenerator objectIdGenerator,
        IStandardCrudResourceRegistry crudResourceRegistry,
        IStandardCrudService standardCrudService)
    {
        _personUserLinkRepository = personUserLinkRepository;
        _identityUserDirectory = identityUserDirectory;
        _objectIdGenerator = objectIdGenerator;
        _crudResourceRegistry = crudResourceRegistry;
        _standardCrudService = standardCrudService;
    }

    public async Task<PersonUserLinkDto> LinkUserAsync(
        string personId,
        LinkPersonUserRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsurePersonExistsAsync(personId, cancellationToken);

        if (await _personUserLinkRepository.ExistsByUserIdAsync(request.UserId, personId, cancellationToken))
        {
            throw new InvalidOperationException("该账号已绑定其他人员。");
        }

        // 绑定前通过身份目录确认用户存在，避免组织模块保存悬空账号引用。
        await _identityUserDirectory.EnsureUserExistsAsync(request.UserId, cancellationToken);

        var existingLink = await _personUserLinkRepository.GetByPersonIdAsync(personId, cancellationToken);
        var link = new PersonUserLink
        {
            Id = string.IsNullOrWhiteSpace(existingLink?.Id)
                ? _objectIdGenerator.NewId(nameof(PersonUserLink))
                : existingLink.Id,
            PersonId = personId,
            UserId = request.UserId,
            LinkedAt = DateTime.UtcNow,
            LinkedBy = string.IsNullOrWhiteSpace(request.LinkedBy) ? "system" : request.LinkedBy
        };

        await _personUserLinkRepository.UpsertAsync(link, cancellationToken);
        return MapToDto(link);
    }

    private async Task EnsurePersonExistsAsync(string personId, CancellationToken cancellationToken)
    {
        var descriptor = _crudResourceRegistry.FindByResourceKey(PersonResourceKey)
            ?? throw new InvalidOperationException("人员标准 CRUD 资源未注册。");

        var detail = await _standardCrudService.GetDetailAsync(descriptor, personId, cancellationToken);
        if (detail is null)
        {
            throw new InvalidOperationException($"人员 '{personId}' 不存在。");
        }
    }

    private static PersonUserLinkDto MapToDto(PersonUserLink link) => new()
    {
        Id = link.Id,
        PersonId = link.PersonId,
        UserId = link.UserId,
        CreatedAt = link.LinkedAt,
        LinkedAt = link.LinkedAt,
        LinkedBy = string.IsNullOrWhiteSpace(link.LinkedBy) ? "system" : link.LinkedBy
    };
}
```

- [ ] **Step 5: 运行人员账号绑定服务测试确认通过**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter PersonUserLinkServiceTests -nr:false -v minimal
```

Expected: PASS。

- [ ] **Step 6: 提交**

```powershell
git add .\server\modules\Organization\Services\IPersonUserLinkService.cs .\server\modules\Organization\Services\PersonUserLinkService.cs .\tests\Organization.Tests\PersonUserLinkServiceTests.cs
git commit -m "功能：拆出人员账号绑定服务"
```

---

### Task 4: Organization 接入自动路由并删除人员后端旧样板

**Files:**
- Create: `server/modules/Organization/Controllers/PersonUserLinksController.cs`
- Modify: `server/modules/Organization/OrganizationModule.cs`
- Modify: `apps/host-api/Program.cs`
- Modify: `apps/host-api/HostApi.csproj`
- Delete: `server/modules/Organization/Controllers/PersonsController.cs`
- Delete: `server/modules/Organization/Services/IPersonService.cs`
- Delete: `server/modules/Organization/Services/PersonService.cs`
- Delete: `server/modules/Organization/Repositories/IPersonRepository.cs`
- Delete: `server/modules/Organization/Persistence/Ef/EfPersonRepository.cs`
- Delete: `server/modules/Organization/DTOs/PersonListItemDto.cs`
- Delete: `server/modules/Organization/DTOs/PersonDetailDto.cs`
- Delete: `server/modules/Organization/DTOs/PersonSummaryBlockDto.cs`
- Delete: `server/modules/Organization/Models/Person.cs`
- Create: `tests/Organization.Tests/PersonUserLinksControllerTests.cs`
- Create: `tests/Organization.Tests/PersonStandardCrudResourceTests.cs`
- Delete: `tests/Organization.Tests/PersonListContractTests.cs`
- Delete: `tests/Organization.Tests/PersonServiceTests.cs`
- Test: `tests/Organization.Tests/Organization.Tests.csproj`
- Build: `apps/host-api/HostApi.csproj`

- [ ] **Step 1: 写 Organization 注册与专属 Controller 测试**

Create `tests/Organization.Tests/PersonStandardCrudResourceTests.cs`:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Modules.Organization;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Modules.Organization.Crud;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class PersonStandardCrudResourceTests
{
    [Fact]
    public void OrganizationModule_RegistersPersonStandardCrudDescriptor()
    {
        var services = new ServiceCollection();
        new OrganizationModule().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IStandardCrudResourceRegistry>();
        var descriptor = registry.FindByResourceKey("organization.person");

        Assert.NotNull(descriptor);
        Assert.Equal("api/organization/persons", descriptor!.Route);
        Assert.Equal(StandardCrudDeletePolicy.Conditional, descriptor.DeletePolicy);
    }

    [Fact]
    public void PersonCrudDescriptor_DeclaresRouteSummaryDisplayAndUniqueConstraint()
    {
        var descriptor = PersonCrudDescriptorFactory.Create();

        Assert.Equal("organization.person", descriptor.ResourceKey);
        Assert.Equal("api/organization/persons", descriptor.Route);
        Assert.Contains("CompanyName", descriptor.DisplayValueFields);
        Assert.Contains("DepartmentName", descriptor.DisplayValueFields);
        Assert.Contains("PositionName", descriptor.DisplayValueFields);
        Assert.Contains("userLink", descriptor.SummaryBlockKeys);
        Assert.Contains("authorizationSummary", descriptor.SummaryBlockKeys);
        var constraint = Assert.Single(descriptor.UniqueConstraints);
        Assert.Equal("personNo", constraint.Key);
        Assert.Equal(["PersonNo"], constraint.Fields);
    }

    [Fact]
    public void LegacyPersonsController_IsRemoved()
    {
        var legacyType = typeof(OrganizationModule).Assembly.GetType(
            "ICP.Platform.Modules.Organization.Controllers.PersonsController");

        Assert.Null(legacyType);
        Assert.NotNull(typeof(PersonUserLinksController));
    }
}
```

Create `tests/Organization.Tests/PersonUserLinksControllerTests.cs`:

```csharp
using System.Reflection;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class PersonUserLinksControllerTests
{
    [Fact]
    public void Controller_DeclaresOnlyPersonUserLinkRoutes()
    {
        var controllerType = typeof(PersonUserLinksController);

        Assert.Contains(controllerType.GetCustomAttributes<RouteAttribute>(), route => route.Template == "api/organization/persons");
        Assert.Contains(controllerType.GetMethod(nameof(PersonUserLinksController.SearchUserOptions))!.GetCustomAttributes<HttpGetAttribute>(), route => route.Template == "user-options");
        Assert.Contains(controllerType.GetMethod(nameof(PersonUserLinksController.LinkUser))!.GetCustomAttributes<HttpPutAttribute>(), route => route.Template == "{id}/user-link");
    }

    [Fact]
    public async Task LinkUser_WhenServiceThrows_ReturnsBadRequest()
    {
        var controller = new PersonUserLinksController(
            new EmptyIdentityUserDirectory(),
            new FailingPersonUserLinkService());

        var response = await controller.LinkUser(
            "Person_missing",
            new LinkPersonUserRequest { UserId = "user-1" });

        var badRequest = Assert.IsType<BadRequestObjectResult>(response.Result);
        Assert.NotNull(badRequest.Value);
    }

    private sealed class EmptyIdentityUserDirectory : IIdentityUserDirectory
    {
        public Task EnsureUserExistsAsync(string userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<IdentityUserOptionDto>> SearchAsync(string keyword, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IdentityUserOptionDto>>([]);
    }

    private sealed class FailingPersonUserLinkService : IPersonUserLinkService
    {
        public Task<PersonUserLinkDto> LinkUserAsync(string personId, LinkPersonUserRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("人员不存在。");
    }
}
```

- [ ] **Step 2: 运行 Organization 定向测试确认失败**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter "PersonStandardCrudResourceTests|PersonUserLinksControllerTests" -nr:false -v minimal
```

Expected: FAIL，提示 `PersonUserLinksController` 不存在，或 OrganizationModule 尚未注册 Person descriptor。

- [ ] **Step 3: 新增人员账号绑定专属 Controller**

Create `server/modules/Organization/Controllers/PersonUserLinksController.cs`:

```csharp
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Organization.Controllers;

[ApiController]
[Route("api/organization/persons")]
public sealed class PersonUserLinksController : ControllerBase
{
    private readonly IIdentityUserDirectory _identityUserDirectory;
    private readonly IPersonUserLinkService _personUserLinkService;

    public PersonUserLinksController(
        IIdentityUserDirectory identityUserDirectory,
        IPersonUserLinkService personUserLinkService)
    {
        _identityUserDirectory = identityUserDirectory;
        _personUserLinkService = personUserLinkService;
    }

    [HttpGet("user-options")]
    public async Task<ActionResult<IReadOnlyList<IdentityUserOptionDto>>> SearchUserOptions(
        [FromQuery] string keyword,
        CancellationToken cancellationToken = default)
    {
        var options = await _identityUserDirectory.SearchAsync(keyword, cancellationToken);
        return Ok(options);
    }

    [HttpPut("{id}/user-link")]
    public async Task<ActionResult<PersonUserLinkDto>> LinkUser(
        string id,
        [FromBody] LinkPersonUserRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _personUserLinkService.LinkUserAsync(id, request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
```

- [ ] **Step 4: 更新 OrganizationModule 注册**

Modify `server/modules/Organization/OrganizationModule.cs` so `ConfigureServices` becomes:

```csharp
public override void ConfigureServices(IServiceCollection services)
{
    services.AddStandardCrudKernel();
    services.AddStandardCrudResource(PersonCrudDescriptorFactory.Create());
    services.AddScoped<ICompanyRepository, EfCompanyRepository>();
    services.AddScoped<IDepartmentRepository, EfDepartmentRepository>();
    services.AddScoped<IPositionRepository, EfPositionRepository>();
    services.AddScoped<IPersonUserLinkRepository, EfPersonUserLinkRepository>();
    services.AddScoped<ICompanyService, CompanyService>();
    services.AddScoped<IDepartmentService, DepartmentService>();
    services.AddScoped<IPositionService, PositionService>();
    services.AddScoped<IIdentityUserDirectory, NullIdentityUserDirectory>();
    services.AddScoped<IAuthorizationSummaryReader, NullAuthorizationSummaryReader>();
    services.AddScoped<IPersonUserLinkService, PersonUserLinkService>();
    services.AddScoped<IStandardCrudQueryCustomizer, PersonCrudQueryCustomizer>();
    services.AddScoped<IStandardCrudDisplayValueResolver, PersonCrudDisplayValueResolver>();
    services.AddScoped<IStandardCrudSummaryProvider, PersonUserLinkSummaryProvider>();
    services.AddScoped<IStandardCrudSummaryProvider, PersonAuthorizationSummaryProvider>();
    services.AddScoped<IStandardCrudValidator, PersonCrudValidator>();
    services.AddScoped<IStandardCrudDeleteGuard, PersonCrudDeleteGuard>();
}
```

- [ ] **Step 5: 在 Host 中挂载自动路由**

Modify `apps/host-api/HostApi.csproj`.

Add a direct platform CRUD project reference in the existing `ProjectReference` item group:

```xml
<ProjectReference Include="..\..\server\platform-crud\PlatformCrud.csproj" />
```

Modify `apps/host-api/Program.cs`.

Add using:

```csharp
using ICP.Platform.Crud.Extensions;
```

Replace the final mapping block:

```csharp
app.UseAuthentication();
app.UseAuditMiddleware();
app.UseAuthorization();
app.MapControllers();
app.MapStandardCrudResources();
```

- [ ] **Step 6: 删除人员中心后端旧样板文件**

Run:

```powershell
Remove-Item .\server\modules\Organization\Controllers\PersonsController.cs
Remove-Item .\server\modules\Organization\Services\IPersonService.cs
Remove-Item .\server\modules\Organization\Services\PersonService.cs
Remove-Item .\server\modules\Organization\Repositories\IPersonRepository.cs
Remove-Item .\server\modules\Organization\Persistence\Ef\EfPersonRepository.cs
Remove-Item .\server\modules\Organization\DTOs\PersonListItemDto.cs
Remove-Item .\server\modules\Organization\DTOs\PersonDetailDto.cs
Remove-Item .\server\modules\Organization\DTOs\PersonSummaryBlockDto.cs
Remove-Item .\server\modules\Organization\Models\Person.cs
Remove-Item .\tests\Organization.Tests\PersonListContractTests.cs
Remove-Item .\tests\Organization.Tests\PersonServiceTests.cs
```

- [ ] **Step 7: 扫描旧人员标准 CRUD 残留**

Run:

```powershell
rg -n "IPersonService|PersonService|IPersonRepository|EfPersonRepository|PersonsController|PersonListItemDto|PersonDetailDto|PersonSummaryBlockDto|Models\\\\Person\\.cs" server/modules/Organization tests/Organization.Tests
```

Expected: no matches for deleted backend standard CRUD types. Matches inside generated `PersonDto.g.cs`, metadata paths, and `PersonCrud*` files are allowed because they are not deleted standard CRUD samples.

- [ ] **Step 8: 运行 Organization 定向测试确认通过**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter "PersonStandardCrudResourceTests|PersonUserLinksControllerTests|PersonUserLinkServiceTests|OrganizationModuleTests" -nr:false -v minimal
```

Expected: PASS。

- [ ] **Step 9: 运行 Organization 全量测试**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
```

Expected: PASS。

- [ ] **Step 10: 构建 HostApi**

Run:

```powershell
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
```

Expected: build succeeds with 0 errors.

- [ ] **Step 11: 提交**

```powershell
git add .\apps\host-api\HostApi.csproj .\apps\host-api\Program.cs .\server\modules\Organization .\tests\Organization.Tests
git commit -m "重构：人员中心标准CRUD改由平台自动映射"
```

---

### Task 5: 人员中心前端消费标准 CRUD 模型

**Files:**
- Modify: `frontend/modules/organization/src/services/organizationService.ts`
- Modify: `frontend/modules/organization/src/standard-list/PersonListStandardListEntry.tsx`
- Modify: `frontend/modules/organization/src/pages/PersonCreatePage.tsx`
- Modify: `frontend/modules/organization/src/pages/PersonDetailPage.tsx`
- Modify: `frontend/modules/organization/src/components/PersonEditorForm.tsx`
- Modify: `frontend/modules/organization/src/components/PersonUserLinkCard.tsx`
- Modify: `frontend/modules/organization/src/components/AuthorizationSummaryCard.tsx`
- Test: `frontend/modules/organization/package.json`

- [ ] **Step 1: 更新前端标准 CRUD 类型与映射函数**

Modify `frontend/modules/organization/src/services/organizationService.ts`.

Replace person list/detail DTO types with:

```ts
export interface StandardCrudListResult<TItem> {
  items: TItem[];
  total: number;
  page: number;
  pageSize: number;
  sortBy?: string | null;
  sortDirection?: string | null;
}

export interface StandardCrudDetail {
  fields: Record<string, unknown>;
  summaries: Record<string, PersonSummaryBlockDto<unknown> | undefined>;
}

export interface PersonListRow {
  id: string;
  personNo: string;
  name: string;
  companyId: string;
  companyName: string;
  departmentId: string;
  departmentName: string;
  positionId?: string | null;
  positionName?: string | null;
  employmentStatus: string;
  employmentStatusLabel: string;
  isEnabled: boolean;
}

export interface PersonDetailView {
  id: string;
  personNo: string;
  name: string;
  companyId: string;
  departmentId: string;
  positionId?: string | null;
  employmentStatus: string;
  employmentStatusLabel: string;
  isEnabled: boolean;
  createdAt: string;
  companyName?: string | null;
  departmentName?: string | null;
  positionName?: string | null;
  userLinkSummary: PersonSummaryBlockDto<PersonUserLinkSummaryData>;
  authorizationSummary: PersonSummaryBlockDto<string[]>;
}
```

Replace person mapping helpers with standard-field mappers:

```ts
function normalizePersonListItem(value: unknown): PersonListRow {
  const row = isRecord(value) ? value : {};
  const employmentStatus = readString(row, ['EmploymentStatus'], 'FullTime');

  return {
    id: readString(row, ['EID']),
    personNo: readString(row, ['PersonNo']),
    name: readString(row, ['ENAME']),
    companyId: readString(row, ['CompanyId']),
    companyName: readString(row, ['CompanyName'], readString(row, ['CompanyId'])),
    departmentId: readString(row, ['DepartmentId']),
    departmentName: readString(row, ['DepartmentName'], readString(row, ['DepartmentId'])),
    positionId: readNullableString(row, ['PositionId']),
    positionName: readNullableString(row, ['PositionName']),
    employmentStatus,
    employmentStatusLabel: getEmploymentStatusLabel(employmentStatus),
    isEnabled: readBoolean(row, ['IsEnabled'], true),
  };
}

function normalizePersonDetail(value: unknown): PersonDetailView {
  const detail = isRecord(value) ? value : {};
  const fields = isRecord(detail['fields']) ? detail['fields'] : {};
  const summaries = isRecord(detail['summaries']) ? detail['summaries'] : {};
  const employmentStatus = readString(fields, ['EmploymentStatus'], 'FullTime');

  return {
    id: readString(fields, ['EID']),
    personNo: readString(fields, ['PersonNo']),
    name: readString(fields, ['ENAME']),
    companyId: readString(fields, ['CompanyId']),
    departmentId: readString(fields, ['DepartmentId']),
    positionId: readNullableString(fields, ['PositionId']),
    employmentStatus,
    employmentStatusLabel: getEmploymentStatusLabel(employmentStatus),
    isEnabled: readBoolean(fields, ['IsEnabled'], true),
    createdAt: readDateString(fields, ['CreatedAt']),
    companyName: readNullableString(fields, ['CompanyName']),
    departmentName: readNullableString(fields, ['DepartmentName']),
    positionName: readNullableString(fields, ['PositionName']),
    userLinkSummary: normalizeSummaryBlock(summaries['userLink'], (data) => {
      const link = isRecord(data) ? data : {};
      return {
        personId: readString(link, ['PersonId', 'personId']),
        userId: readString(link, ['UserId', 'userId']),
        linkedAt: readString(link, ['LinkedAt', 'linkedAt']),
        linkedBy: readString(link, ['LinkedBy', 'linkedBy']),
      };
    }),
    authorizationSummary: normalizeSummaryBlock(summaries['authorizationSummary'], (data) => {
      if (Array.isArray(data)) {
        return data.filter((item): item is string => typeof item === 'string');
      }

      return [];
    }),
  };
}

function normalizePersonListResponse(value: unknown): StandardCrudListResult<PersonListRow> {
  const result = isRecord(value) ? value : {};
  const rawItems = Array.isArray(result['items']) ? result['items'] : [];
  const items = rawItems.map(normalizePersonListItem);

  return {
    items,
    total: typeof result['total'] === 'number' ? result['total'] : items.length,
    page: typeof result['page'] === 'number' ? result['page'] : 1,
    pageSize: typeof result['pageSize'] === 'number' ? result['pageSize'] : items.length,
    sortBy: typeof result['sortBy'] === 'string' ? result['sortBy'] : null,
    sortDirection: typeof result['sortDirection'] === 'string' ? result['sortDirection'] : null,
  };
}
```

- [ ] **Step 2: 更新人员写入请求为标准字段名**

Modify `frontend/modules/organization/src/services/organizationService.ts`.

Replace `normalizePersonUpsertRequest` with:

```ts
function toStandardPersonValues(dto: PersonUpsertRequest): Record<string, unknown> {
  return {
    PersonNo: dto.personNo,
    ENAME: dto.name,
    CompanyId: dto.companyId && dto.companyId.trim().length > 0 ? dto.companyId : null,
    DepartmentId: dto.departmentId && dto.departmentId.trim().length > 0 ? dto.departmentId : null,
    PositionId: dto.positionId && dto.positionId.trim().length > 0 ? dto.positionId : null,
    EmploymentStatus: dto.employmentStatus && dto.employmentStatus.trim().length > 0 ? dto.employmentStatus : null,
    IsEnabled: dto.isEnabled,
  };
}
```

Replace create/update/disable person methods with:

```ts
export async function createPerson(dto: PersonUpsertRequest): Promise<PersonDetailView> {
  return normalizePersonDetail(
    await requestJson<unknown>(
      '/api/organization/persons',
      {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(toStandardPersonValues(dto)),
      },
      '新建人员失败',
    ),
  );
}

export async function updatePerson(personId: string, dto: PersonUpsertRequest): Promise<PersonDetailView> {
  return normalizePersonDetail(
    await requestJson<unknown>(
      `/api/organization/persons/${personId}`,
      {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(toStandardPersonValues(dto)),
      },
      '保存人员信息失败',
    ),
  );
}

export async function disablePerson(personId: string): Promise<PersonDetailView> {
  return normalizePersonDetail(
    await requestJson<unknown>(
      `/api/organization/persons/${personId}/disable`,
      {
        method: 'POST',
      },
      '停用人员失败',
    ),
  );
}
```

Update `getPersons` return type:

```ts
export async function getPersons(query: {
  keyword?: string;
  companyId?: string;
  departmentId?: string;
  employmentStatus?: string;
}): Promise<StandardCrudListResult<PersonListRow>> {
  const search = new URLSearchParams();

  if (query.keyword) {
    search.set('keyword', query.keyword);
  }

  if (query.companyId) {
    search.set('companyId', query.companyId);
  }

  if (query.departmentId) {
    search.set('departmentId', query.departmentId);
  }

  if (query.employmentStatus) {
    search.set('employmentStatus', query.employmentStatus);
  }

  const suffix = search.toString();
  const path = suffix.length > 0 ? `/api/organization/persons?${suffix}` : '/api/organization/persons';
  return normalizePersonListResponse(await fetchJson<unknown>(path, '加载人员列表失败'));
}
```

- [ ] **Step 3: 更新前端引用类型名**

Run replacements:

```powershell
rg -n "PersonListItemDto|PersonDetailDto|PersonListResultDto" frontend/modules/organization/src
```

Apply these replacements in the listed files:

```text
PersonListItemDto -> PersonListRow
PersonDetailDto -> PersonDetailView
PersonListResultDto -> StandardCrudListResult<PersonListRow>
```

Expected after edit:

```powershell
rg -n "PersonListItemDto|PersonDetailDto|PersonListResultDto|refreshPersonDetail|normalizePersonUpsertRequest" frontend/modules/organization/src
```

returns no matches.

- [ ] **Step 4: 运行 Organization 前端构建**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-modules/organization build
```

Expected: build succeeds.

- [ ] **Step 5: 提交**

```powershell
git add .\frontend\modules\organization\src
git commit -m "重构：人员中心前端消费标准CRUD模型"
```

---

### Task 6: 验证记录与全链路验证

**Files:**
- Modify: `docs/verification/organization-module-verification.md`
- Test: `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`
- Test: `tests/Organization.Tests/Organization.Tests.csproj`
- Build: `apps/host-api/HostApi.csproj`
- Build: `frontend`

- [ ] **Step 1: 扫描人员中心旧样板残留**

Run:

```powershell
rg -n "PersonsController|IPersonService|PersonService|IPersonRepository|EfPersonRepository|PersonListItemDto|PersonDetailDto|PersonSummaryBlockDto" server/modules/Organization tests/Organization.Tests frontend/modules/organization/src
```

Expected: no matches. If matches remain in `docs/` they are documentation history and do not block code cleanup.

- [ ] **Step 2: 扫描标准 CRUD 自动映射入口**

Run:

```powershell
rg -n "MapStandardCrudResources|AddStandardCrudResource|IStandardCrudResourceRegistry|PersonUserLinksController|IPersonUserLinkService" apps/host-api server/platform-crud server/modules/Organization tests
```

Expected: output includes `apps/host-api/Program.cs`, `server/platform-crud`, `server/modules/Organization/OrganizationModule.cs`, `PersonUserLinksController`, and new tests.

- [ ] **Step 3: 回填验证记录**

Append to `docs/verification/organization-module-verification.md`:

````md

## 标准 CRUD 路由自动映射与人员旧样板退场验证

验证日期：2026-05-11

验证目标：

- Person 标准 CRUD 路由由 `StandardCrudDescriptor.Route` 自动映射。
- `/api/organization/persons` 标准 CRUD 不再依赖 `PersonsController`。
- 人员账号绑定保留为专属 Controller 能力。
- 人员标准 CRUD 旧 service/repository/controller/DTO 样板已退场。

验证命令：

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
pnpm --dir .\frontend --filter @icp-modules/organization build
rg -n "PersonsController|IPersonService|PersonService|IPersonRepository|EfPersonRepository|PersonListItemDto|PersonDetailDto|PersonSummaryBlockDto" server/modules/Organization tests/Organization.Tests frontend/modules/organization/src
```

验证结论：

- 标准 CRUD API 对外继续使用模块语义路由，对内统一由平台 mapper 委托 `StandardCrudEndpoint`。
- 人员中心只保留账号绑定专属 Controller，不再保留标准 CRUD 空壳 Controller。
- Person 的运行时真源为 `PersonCrudDescriptorFactory` 与模型 metadata；人员账号绑定关系继续由 Organization 模块专属代码维护。
````

- [ ] **Step 4: 运行平台 CRUD 测试**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
```

Expected: PASS。

- [ ] **Step 5: 运行 Organization 测试**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
```

Expected: PASS。

- [ ] **Step 6: 构建 HostApi**

Run:

```powershell
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
```

Expected: build succeeds with 0 errors.

- [ ] **Step 7: 构建 Organization 前端**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-modules/organization build
```

Expected: build succeeds.

- [ ] **Step 8: 提交**

```powershell
git add .\docs\verification\organization-module-verification.md
git commit -m "验证：记录标准CRUD自动映射收口结果"
```

## Self-Review

- **Spec coverage:** Task 1 覆盖资源注册表与 descriptor.Route 唯一真源；Task 2 覆盖自动路由映射全部标准动作；Task 3 覆盖人员账号绑定专属服务；Task 4 覆盖 Host 显式平台引用、Organization 接入、标准 CRUD Controller/Service/Repository/DTO 删除和专属 Controller 保留；Task 5 覆盖前端标准返回模型迁移；Task 6 覆盖验证记录和残留扫描。
- **占位检查:** 本计划未保留占位语句或无期望结果的命令。
- **Type consistency:** 平台统一使用 `IStandardCrudResourceRegistry`、`StandardCrudResourceRegistry`、`AddStandardCrudResource`、`MapStandardCrudResources`；人员专属能力统一使用 `IPersonUserLinkService`、`PersonUserLinkService`、`PersonUserLinksController`；前端标准读取模型统一使用 `StandardCrudListResult<TItem>`、`StandardCrudDetail`、`PersonListRow`、`PersonDetailView`。
