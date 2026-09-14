using System.Text.Json;
using ICP.Platform.Authorization.Extensions;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Query.Runtime;
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
        "pageIndex",
        "pageSize",
        "sortBy",
        "sortDirection",
        "fields",
        "queryScope"
    };

    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 按 descriptor.Route 自动映射标准 CRUD API；特殊业务动作仍应留在模块 Controller。
    /// </summary>
    public static IEndpointRouteBuilder MapStandardCrudResources(this IEndpointRouteBuilder endpoints)
    {
        var registry = endpoints.ServiceProvider.GetRequiredService<IStandardCrudResourceRegistry>();
        var descriptors = registry.GetAll().Where(descriptor => descriptor.MapStandardEndpoints).ToArray();
        StandardCrudAuthorizationValidator.Validate(
            descriptors,
            StandardCrudAuthorizationValidator.MappedEndpointOperations);
        ValidateStandardRouteConflicts(descriptors);

        foreach (var descriptor in descriptors)
        {
            MapResource(endpoints, descriptor);
        }

        return endpoints;
    }

    // 单个 descriptor 的派生路由在这里统一映射，权限和错误形状不能由模块自行分叉。
    private static void MapResource(IEndpointRouteBuilder endpoints, StandardCrudDescriptor descriptor)
    {
        var route = "/" + descriptor.Route.Trim().Trim('/');
        var authorization = descriptor.Authorization!;

        endpoints.MapGet(route, async (
            HttpContext context,
            StandardCrudEndpoint endpoint,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await endpoint.ListAsync(descriptor, BuildListQuery(context.Request.Query), cancellationToken);
                return Results.Ok(result);
            }
            catch (PlatformQueryValidationException ex)
            {
                return Results.BadRequest(new StandardCrudError
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Code = ex.Code,
                    Message = ex.Message
                });
            }
        }).WithMetadata(new EndpointModuleAttribute(descriptor.ModuleId))
            .RequireCrudOperation(authorization, StandardCrudOperation.List);

        endpoints.MapGet(route + "/options", async (
            HttpContext context,
            StandardCrudEndpoint endpoint,
            CancellationToken cancellationToken) =>
        {
            var result = await endpoint.OptionsAsync(descriptor, BuildLookupRequest(context.Request.Query), cancellationToken);
            return Results.Ok(result);
        }).WithMetadata(new EndpointModuleAttribute(descriptor.ModuleId))
            .RequireCrudOperation(authorization, StandardCrudOperation.Options);

        endpoints.MapPost(route + "/export", async (
            HttpContext context,
            StandardCrudExportService exportService,
            CancellationToken cancellationToken) =>
        {
            var request = await ReadExportRequestAsync(context, cancellationToken);
            if (request is null)
            {
                return Results.BadRequest(new StandardCrudError
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Code = "StandardCrud.Export.InvalidRequest",
                    Message = "标准 CRUD 导出请求不能为空。"
                });
            }

            try
            {
                var file = await exportService.ExportAsync(descriptor, request, cancellationToken);
                return Results.File(file.Content, file.ContentType, file.FileName);
            }
            catch (PlatformQueryValidationException ex)
            {
                return Results.BadRequest(new StandardCrudError
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Code = ex.Code,
                    Message = ex.Message
                });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new StandardCrudError
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Code = "StandardCrud.Export.InvalidRequest",
                    Message = ex.Message
                });
            }
            catch (StandardCrudExportLimitExceededException ex)
            {
                return Results.BadRequest(new StandardCrudError
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Code = "StandardCrud.Export.LimitExceeded",
                    Message = ex.Message
                });
            }
        }).WithMetadata(new EndpointModuleAttribute(descriptor.ModuleId))
            .RequireCrudOperation(authorization, StandardCrudOperation.Export);

        endpoints.MapGet(route + "/{id}", async (
            string id,
            StandardCrudEndpoint endpoint,
            CancellationToken cancellationToken) =>
        {
            var detail = await endpoint.GetDetailAsync(descriptor, id, cancellationToken);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        }).WithMetadata(new EndpointModuleAttribute(descriptor.ModuleId))
            .RequireCrudOperation(authorization, StandardCrudOperation.Detail);

        endpoints.MapPost(route, async (
            HttpContext context,
            StandardCrudEndpoint endpoint,
            CancellationToken cancellationToken) =>
        {
            var values = await ReadValuesAsync(context, cancellationToken);
            var result = await endpoint.CreateAsync(descriptor, values, cancellationToken);
            return ToMutationResult(result, detail => Results.Created(BuildCreatedLocation(route, descriptor, detail), detail));
        }).WithMetadata(new EndpointModuleAttribute(descriptor.ModuleId))
            .RequireCrudOperation(authorization, StandardCrudOperation.Create);

        endpoints.MapPut(route + "/{id}", async (
            string id,
            HttpContext context,
            StandardCrudEndpoint endpoint,
            CancellationToken cancellationToken) =>
        {
            var values = await ReadValuesAsync(context, cancellationToken);
            var result = await endpoint.UpdateAsync(descriptor, id, values, cancellationToken);
            return ToMutationResult(result, Results.Ok);
        }).WithMetadata(new EndpointModuleAttribute(descriptor.ModuleId))
            .RequireCrudOperation(authorization, StandardCrudOperation.Update);

        endpoints.MapPost(route + "/{id}/enable", async (
            string id,
            StandardCrudEndpoint endpoint,
            CancellationToken cancellationToken) =>
        {
            var result = await endpoint.EnableAsync(descriptor, id, cancellationToken);
            return ToMutationResult(result, Results.Ok);
        }).WithMetadata(new EndpointModuleAttribute(descriptor.ModuleId))
            .RequireCrudOperation(authorization, StandardCrudOperation.Enable);

        endpoints.MapPost(route + "/{id}/disable", async (
            string id,
            StandardCrudEndpoint endpoint,
            CancellationToken cancellationToken) =>
        {
            var result = await endpoint.DisableAsync(descriptor, id, cancellationToken);
            return ToMutationResult(result, Results.Ok);
        }).WithMetadata(new EndpointModuleAttribute(descriptor.ModuleId))
            .RequireCrudOperation(authorization, StandardCrudOperation.Disable);

        endpoints.MapDelete(route + "/{id}", async (
            string id,
            StandardCrudEndpoint endpoint,
            CancellationToken cancellationToken) =>
        {
            var result = await endpoint.DeleteAsync(descriptor, id, cancellationToken);
            return result.Succeeded
                ? Results.NoContent()
                : ToErrorResult(result.Error!);
        }).WithMetadata(new EndpointModuleAttribute(descriptor.ModuleId))
            .RequireCrudOperation(authorization, StandardCrudOperation.Delete);
    }

    // 显式采用位置绑定的资源不再消费旧 Permission；未开放操作仍映射为拒绝以封闭遗留地址。
    private static RouteHandlerBuilder RequireCrudOperation(this RouteHandlerBuilder endpoint,
        StandardCrudAuthorization authorization, StandardCrudOperation operation)
    {
        if (authorization.FeatureBehaviorStem is null) return endpoint.RequirePermission(authorization.GetPermissionCode(operation));
        var behavior = authorization.GetFeatureBehavior(operation);
        return behavior is null ? endpoint.RequireAuthorization(new DenyAllAttribute("该标准操作没有活动功能声明。"))
            : endpoint.RequireAuthorization(new RequireFeatureAttribute(behavior));
    }
    // 自动映射会从一个 descriptor 派生多个路由；这里在启动期拦截派生路由遮挡，避免运行期出现双真源。
    private static void ValidateStandardRouteConflicts(IReadOnlyList<StandardCrudDescriptor> descriptors)
    {
        var registered = new List<StandardRouteCandidate>();

        foreach (var descriptor in descriptors)
        {
            foreach (var candidate in BuildRouteCandidates(descriptor))
            {
                var existing = registered.FirstOrDefault(route => !route.BelongsTo(candidate) && route.ExactlyMatches(candidate))
                    ?? registered.FirstOrDefault(route => !route.BelongsTo(candidate) && route.ConflictsWith(candidate));
                if (existing is not null)
                {
                    throw new InvalidOperationException(
                        $"标准 CRUD 路由 {candidate.Method} {candidate.Pattern} 被 {existing.ResourceKey} 与 {candidate.ResourceKey} 重复映射。");
                }

                registered.Add(candidate);
            }
        }
    }

    // 枚举一个 descriptor 会派生的全部 HTTP 路由，供启动期统一检查静态与参数路由冲突。
    private static IEnumerable<StandardRouteCandidate> BuildRouteCandidates(StandardCrudDescriptor descriptor)
    {
        var route = "/" + descriptor.Route.Trim().Trim('/');

        yield return StandardRouteCandidate.Create("GET", route, descriptor.ResourceKey);
        yield return StandardRouteCandidate.Create("GET", route + "/options", descriptor.ResourceKey);
        yield return StandardRouteCandidate.Create("POST", route + "/export", descriptor.ResourceKey);
        yield return StandardRouteCandidate.Create("GET", route + "/{id}", descriptor.ResourceKey);
        yield return StandardRouteCandidate.Create("POST", route, descriptor.ResourceKey);
        yield return StandardRouteCandidate.Create("PUT", route + "/{id}", descriptor.ResourceKey);
        yield return StandardRouteCandidate.Create("POST", route + "/{id}/disable", descriptor.ResourceKey);
        yield return StandardRouteCandidate.Create("DELETE", route + "/{id}", descriptor.ResourceKey);
    }

    // 页面范围与用户筛选分离传入模块查询 owner；模块在计数和分页前取交集。
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
            Page: ReadInt(query, "pageIndex", ReadInt(query, "page", 1)),
            PageSize: ReadInt(query, "pageSize", 20),
            SortBy: ReadString(query, "sortBy"),
            SortDirection: ReadString(query, "sortDirection"),
            Filters: filters,
            Fields: ParseFields(ReadString(query, "fields")),
            QueryScope: ReadQueryScope(query));
    }

    // 无效页面输入在 HTTP 边界拒绝，不降级为无范围列表。
    private static IReadOnlyDictionary<string, string?>? ReadQueryScope(IQueryCollection query)
    {
        if (!query.TryGetValue("queryScope", out var value)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(value.ToString())
                ?? throw new JsonException("queryScope must be an object.");
        }
        catch (JsonException exception)
        {
            throw new BadHttpRequestException("Invalid queryScope.", exception);
        }
    }

    // lookup 使用独立稳定契约，避免把列表排序、字段投影和任意过滤器泄漏给 provider。
    private static StandardCrudLookupRequest BuildLookupRequest(IQueryCollection query) => new(
        Keyword: ReadString(query, "keyword"),
        Page: ReadInt(query, "pageIndex", ReadInt(query, "page", 1)),
        PageSize: ReadInt(query, "pageSize", 20),
        CurrentValueIds: ParseCurrentValueIds(query));

    // 当前值标识既支持逗号分隔也支持重复 query key，并在边界去空白和去重。
    private static IReadOnlyList<string> ParseCurrentValueIds(IQueryCollection query) =>
        query.TryGetValue("currentValueIds", out var values)
            ? values
                .SelectMany(value => value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [])
                .Where(static value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList()
            : [];

    private static IReadOnlyList<string>? ParseFields(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var fields = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static field => field.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return fields.Count == 0 ? null : fields;
    }

    private static async Task<StandardCrudExportRequest?> ReadExportRequestAsync(
        HttpContext context,
        CancellationToken cancellationToken) =>
        await JsonSerializer.DeserializeAsync<StandardCrudExportRequest>(
            context.Request.Body,
            WebJsonOptions,
            cancellationToken);

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

    private sealed record StandardRouteCandidate(
        string Method,
        string Pattern,
        string ResourceKey,
        IReadOnlyList<string> Segments)
    {
        public static StandardRouteCandidate Create(string method, string pattern, string resourceKey)
        {
            var segments = pattern.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            return new StandardRouteCandidate(method, pattern, resourceKey, segments);
        }

        public bool ConflictsWith(StandardRouteCandidate other)
        {
            if (!string.Equals(Method, other.Method, StringComparison.OrdinalIgnoreCase) ||
                Segments.Count != other.Segments.Count)
            {
                return false;
            }

            for (var i = 0; i < Segments.Count; i++)
            {
                if (!SegmentsConflict(Segments[i], other.Segments[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public bool ExactlyMatches(StandardRouteCandidate other) =>
            string.Equals(Method, other.Method, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Pattern, other.Pattern, StringComparison.OrdinalIgnoreCase);

        public bool BelongsTo(StandardRouteCandidate other) =>
            string.Equals(ResourceKey, other.ResourceKey, StringComparison.Ordinal);

        private static bool SegmentsConflict(string left, string right) =>
            IsRouteParameter(left) ||
            IsRouteParameter(right) ||
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        private static bool IsRouteParameter(string segment) =>
            segment.StartsWith('{') && segment.EndsWith('}');
    }
}
