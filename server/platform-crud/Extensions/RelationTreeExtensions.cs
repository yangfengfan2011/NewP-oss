using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Kernel.ModulePlanning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Crud.Extensions;

/// <summary>从活动配置装配标准树读取；特殊业务继续使用模块自己的 Provider/API。</summary>
public static class RelationTreeExtensions
{
    private sealed record Catalog(IReadOnlyList<RelationTreeDefinition> Trees);

    /// <summary>消费冻结候选的关系树配置，权限从节点对应的已注册资源派生。</summary>
    public static IServiceCollection AddRelationTrees(this IServiceCollection services, CanonicalModulePlan plan)
    {
        var trees = RelationTreeMetadata.Project(plan);
        services.AddSingleton(new Catalog(trees));
        services.AddScoped<RelationTreeSql>();
        services.AddScoped<RelationTreeReader>();
        if (trees.Count == 0) return services;
        services.AddOptions<AuthorizationOptions>().Configure<IStandardCrudResourceRegistry>((options, registry) =>
        {
            foreach (var tree in trees)
            {
                var permissions = tree.Source.Nodes.Select(node =>
                {
                    var resource = registry.FindByResourceKey(node.ResourceKey)
                        ?? throw new InvalidOperationException($"Missing tree resource: {node.ResourceKey}");
                    var permission = resource.Authorization?.GetPermissionCode(StandardCrudOperation.List);
                    if (resource.ModuleId != tree.ModuleId || permission is null
                        || !plan.Modules.Single(module => module.ModuleId == tree.ModuleId).PermissionCodes.Contains(permission, StringComparer.Ordinal))
                        throw new InvalidOperationException($"Invalid tree resource permission: {node.ResourceKey}");
                    return permission;
                }).Distinct(StringComparer.Ordinal).ToArray();
                options.AddPolicy(Policy(tree), policy => policy.RequireAuthenticatedUser().AddRequirements(
                    permissions.Select(permission => new PermissionAuthorizationRequirement(permission)).ToArray()));
            }
        });
        return services;
    }

    /// <summary>每棵树使用服务端固定路由和完整读取权限；请求只能携带定位及分页输入。</summary>
    public static IEndpointRouteBuilder MapRelationTrees(this IEndpointRouteBuilder endpoints)
    {
        var trees = endpoints.ServiceProvider.GetRequiredService<Catalog>().Trees;
        using var scope = endpoints.ServiceProvider.CreateScope();
        foreach (var tree in trees)
        {
            // 启动期拒绝不可读字段或无效关系，不能等首个用户请求才暴露损坏配置。
            scope.ServiceProvider.GetRequiredService<RelationTreeSql>().Build(tree, applyResourceConditions: false);
            foreach (var operation in new[] { "roots", "children", "path", "search" })
            {
                endpoints.MapGet("/api/trees/" + Uri.EscapeDataString(tree.ViewKey) + "/" + operation,
                    async (HttpContext context, RelationTreeReader reader, CancellationToken token) =>
                    {
                        var query = context.Request.Query;
                        var allowed = operation switch
                        {
                            "roots" => new[] { "scope" },
                            "search" => ["scope", "keyword", "page", "pageSize"],
                            _ => ["scope", "nodeKey"]
                        };
                        if (query.Any(pair => !allowed.Contains(pair.Key, StringComparer.Ordinal) || pair.Value.Count != 1))
                            return Invalid("query", "存在未知或重复树查询参数。");
                        if (query["scope"] != "global") return Invalid("scope", "缺少或无效树读取范围。");
                        if (operation == "roots") return Results.Ok(await reader.RootsAsync(tree, token));
                        if (operation == "search")
                        {
                            var keyword = query["keyword"].ToString();
                            if (string.IsNullOrWhiteSpace(keyword) || !int.TryParse(query["page"], out var page)
                                || !int.TryParse(query["pageSize"], out var pageSize) || page < 1 || pageSize < 1 || page > int.MaxValue / pageSize)
                                return Invalid("search", "搜索需要非空关键词、正页码和正整数 pageSize。");
                            return Results.Ok(await reader.SearchAsync(tree, keyword, page, pageSize, token));
                        }
                        var nodeKey = query["nodeKey"].ToString();
                        if (string.IsNullOrWhiteSpace(nodeKey)) return Invalid("nodeKey", "缺少 nodeKey。");
                        var result = operation == "children" ? await reader.ChildrenAsync(tree, nodeKey, token) : await reader.PathAsync(tree, nodeKey, token);
                        return result is null ? Results.NotFound() : Results.Ok(result);
                    }).WithMetadata(new EndpointModuleAttribute(tree.ModuleId)).RequireAuthorization(Policy(tree));
            }
        }
        return endpoints;
    }

    // 每棵树一个包含全部节点权限的终端策略，与 endpoint 覆盖审计保持一致。
    private static string Policy(RelationTreeDefinition tree) => "Platform.RelationTree." + tree.ViewKey;

    // 输入错误使用带字段的 Problem Details，便于调用方定位而不解析消息文本。
    private static IResult Invalid(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
