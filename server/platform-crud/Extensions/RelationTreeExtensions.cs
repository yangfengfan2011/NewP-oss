using ICP.Platform.Authorization.Http;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.EfCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Crud.Extensions;

/// <summary>从活动配置装配标准树读取；特殊业务继续使用模块自己的 Provider/API。</summary>
public static class RelationTreeExtensions
{
    private sealed record Catalog(IReadOnlyList<RelationTreeDefinition> Trees, IReadOnlySet<string> FeatureTrees);

    /// <summary>消费冻结候选的关系树配置，读取资格来自活动功能声明。</summary>
    public static IServiceCollection AddRelationTrees(this IServiceCollection services, CanonicalModulePlan plan)
    {
        var trees = RelationTreeMetadata.Project(plan);
        foreach (var tree in trees)
        {
            var role = tree.ReadBinding?.Requirements?.SingleOrDefault();
            if (role is null || role.Role != "query" || role.Stage != "read" || role.Shape != "query"
                || role.MinimumCount != 0 || role.MaximumCount is not null || role.AnyOf.Count != 1
                || !role.AnyOf[0].SequenceEqual(["read"]) || role.ReadResolverKey is not null || role.ResourceAnyOf is not null
                || !role.Resources.Order().SequenceEqual(tree.Source.Nodes.Select(node => node.ResourceKey).Distinct().Order()))
                throw new InvalidOperationException("RELATION_TREE_DATA_EXECUTION_INVALID");
        }
        var featureTrees = trees.Where(tree => plan.FeatureCatalog.Any(point => point.ModuleId == tree.ModuleId
            && (point.BehaviorKey == "tree:" + tree.ViewKey || point.ReadBehaviors.Contains("tree:" + tree.ViewKey, StringComparer.Ordinal))))
            .Select(tree => tree.ViewKey).ToHashSet(StringComparer.Ordinal);
        services.AddSingleton(new Catalog(trees, featureTrees));
        services.AddScoped<RelationTreeSql>();
        // 正式装配不能因执行器漏注册而让DI选择旧手工构造，绕过声明来源校验。
        services.AddScoped(provider => new RelationTreeReader(provider.GetRequiredService<EfPlatformDbContext>(),
            provider.GetRequiredService<RelationTreeSql>(), provider.GetService<IDataScopeReadPlanProvider>(),
            provider.GetRequiredService<IDataExecutionAccess>()));
        services.AddSingleton(new LinkTreeCatalog(LinkTreeMetadata.Project(plan)));
        services.AddScoped<LinkTreeReader>();
        return services;
    }

    /// <summary>每棵树使用服务端固定路由和完整读取权限；请求只能携带定位及分页输入。</summary>
    public static IEndpointRouteBuilder MapRelationTrees(this IEndpointRouteBuilder endpoints)
    {
        var catalog = endpoints.ServiceProvider.GetRequiredService<Catalog>();
        var trees = catalog.Trees;
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
                    }).AddEndpointFilter(async (invocation, next) =>
                    {
                        try { return await next(invocation); }
                        catch (ICP.Platform.Authorization.Contracts.ActionDataAccessException exception)
                        {
                            return Results.Problem(statusCode: exception.Status, title: exception.Code,
                                extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
                        }
                    }).WithMetadata(new EndpointModuleAttribute(tree.ModuleId)).RequireAuthorization(catalog.FeatureTrees.Contains(tree.ViewKey)
                        ? new RequireFeatureAttribute("tree:" + tree.ViewKey) : new DenyAllAttribute("该关系树没有活动功能声明。"));
            }
        }
        var linkTrees = endpoints.ServiceProvider.GetRequiredService<LinkTreeCatalog>().Definitions;
        if (trees.Select(tree => tree.ViewKey).Concat(linkTrees.Select(tree => tree.ViewKey)).Distinct(StringComparer.Ordinal).Count() != trees.Count + linkTrees.Count)
            throw new InvalidOperationException("Duplicate tree query source.");
        foreach (var tree in linkTrees)
        {
            var linkReader = scope.ServiceProvider.GetRequiredService<LinkTreeReader>();
            linkReader.Validate(tree);
            var scopeKind = linkReader.ScopeKind(tree);
            foreach (var operation in new[] { "roots", "children", "path", "search" })
            {
                endpoints.MapGet("/api/trees/" + Uri.EscapeDataString(tree.ViewKey) + "/" + operation,
                    async (HttpContext context, LinkTreeReader reader, CancellationToken token) =>
                    {
                        try
                        {
                            var query = context.Request.Query;
                            var allowed = operation switch { "roots" => new[] { "scope" }, "search" => ["scope", "keyword", "page", "pageSize"], _ => ["scope", "nodeKey"] };
                            if (query.Any(pair => !allowed.Contains(pair.Key, StringComparer.Ordinal) || pair.Value.Count != 1) || query["scope"] != scopeKind)
                                throw new LinkQueryException(400, "Platform.TreeInvalidQuery");
                            if (operation == "roots") return Results.Ok(await reader.RootsAsync(tree, token));
                            if (operation == "search")
                            {
                                var page = 1; var size = 20;
                                if (query.TryGetValue("page", out var pageValue) && !int.TryParse(pageValue, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out page)
                                    || query.TryGetValue("pageSize", out var sizeValue) && !int.TryParse(sizeValue, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out size))
                                    throw new LinkQueryException(400, "Platform.TreeInvalidPage");
                                return Results.Ok(await reader.SearchAsync(tree, query["keyword"].ToString(), page, size, token));
                            }
                            var position = LinkTreeNodeKey.Decode(query["nodeKey"].ToString());
                            return Results.Ok(operation == "children" ? await reader.ChildrenAsync(tree, position, token) : await reader.PathAsync(tree, position, token));
                        }
                        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
                    }).WithMetadata(new EndpointModuleAttribute(tree.ModuleId)).RequireAuthorization(new RequireFeatureAttribute("tree:" + tree.ViewKey));
            }
        }
        return endpoints;
    }

    // 输入错误使用带字段的 Problem Details，便于调用方定位而不解析消息文本。
    private static IResult Invalid(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
