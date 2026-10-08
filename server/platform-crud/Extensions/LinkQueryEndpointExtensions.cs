using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Query.Runtime;
using ICP.Platform.Crud.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Crud.Extensions;

/// <summary>只为正式登记的 Link 暴露读取；通用查询不提供任意写入。</summary>
public static class LinkQueryEndpointExtensions
{
    /// <summary>以资源固定路径及活动行为绑定读取，客户端只提交类型身份和标准查询输入。</summary>
    public static IEndpointRouteBuilder MapLinkQueries(this IEndpointRouteBuilder endpoints)
    {
        var catalog = endpoints.ServiceProvider.GetRequiredService<LinkQueryCatalog>();
        using var scope = endpoints.ServiceProvider.CreateScope();
        var policies = scope.ServiceProvider.GetServices<ILinkQuerySourcePolicy>().ToArray();
        var constraints = scope.ServiceProvider.GetServices<ILinkQueryEndpointConstraint>().ToArray();
        var customizers = scope.ServiceProvider.GetServices<IStandardCrudQueryCustomizer>().ToArray();
        var resources = scope.ServiceProvider.GetRequiredService<IStandardCrudResourceRegistry>();
        foreach (var definition in catalog.Definitions)
        {
            if (definition.Metadata.PolicyKey is { } policyKey && policies.Count(policy => policy.Key == policyKey) != 1
                || definition.Endpoints.Values.Any(endpoint => endpoint.ConstraintKey is { } key &&
                    (constraints.Count(constraint => constraint.Key == key) != 1 || constraints.Single(constraint => constraint.Key == key).ModelKey is { } model && model != endpoint.Model.LogicalKey)))
                throw new InvalidOperationException("LINK_QUERY_REQUIRED_POLICY_MISSING_OR_AMBIGUOUS");
            if (customizers.Any(customizer => customizer.ResourceKey == definition.Metadata.ResourceKey)
                && resources.FindByResourceKey(definition.Metadata.ResourceKey) is null)
                throw new InvalidOperationException("LINK_QUERY_SOURCE_CUSTOMIZER_DESCRIPTOR_MISSING");
            endpoints.MapGet("/api/links/" + Uri.EscapeDataString(definition.Metadata.ResourceKey), async (HttpContext context, LinkQueryReader reader, CancellationToken token) =>
            {
                try
                {
                    if (context.Request.Query.Any(pair => pair.Value.Count != 1)) throw new LinkQueryException(400, "LinkQuery.DuplicateQueryParameter");
                    var query = StandardCrudEndpointRouteBuilderExtensions.BuildListQuery(context.Request.Query);
                    var filters = new Dictionary<string, string?>(query.Filters ?? new Dictionary<string, string?>());
                    foreach (var key in new[] { "direction", "anchorModelKey", "anchorId", "types" }) filters.Remove(key);
                    var input = context.Request.Query;
                    var result = await reader.QueryAsync(definition.Metadata.ResourceKey, new(input["direction"].ToString(),
                        new(input["anchorModelKey"].ToString(), input["anchorId"].ToString()), query with { Filters = filters },
                        input.ContainsKey("types") ? input["types"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries) : null), token);
                    return Results.Ok(result);
                }
                catch (LinkQueryException exception) { return Problem(exception); }
                catch (PlatformQueryValidationException exception) { return Results.Problem(statusCode: 400, title: exception.Message, extensions: new Dictionary<string, object?> { ["code"] = exception.Code }); }
            }).WithMetadata(new EndpointModuleAttribute(definition.Model.OwnerModuleId)).RequireAuthorization(new RequireFeatureAttribute(definition.Metadata.ReadBehavior));
        }
        return endpoints;
    }

    /// <summary>业务拒绝保留具体原因与字段；不可读与不存在使用同一安全状态。</summary>
    public static IResult Problem(LinkQueryException exception) => Results.Problem(statusCode: exception.Status, title: exception.Code,
        extensions: new Dictionary<string, object?> { ["code"] = exception.Code, ["reasonKey"] = exception.Code,
            ["fieldErrors"] = exception.Field is null ? Array.Empty<object>() : [new { field = exception.Field, code = exception.Code, reasonKey = exception.Code }] });
}
