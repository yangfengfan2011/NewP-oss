using System.Net;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeRuleNamesSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "规则名称按同级校验并防止跨根并发重名")]
    public async Task Names_AreUniqueAmongSiblingsIncludingConcurrentRoots()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var save = new FeatureHttpTestRequests(fixture).Workbench("authorization.dataScope.save");
        var root = Node("NameRoot", " 根 ");
        Assert.Equal(HttpStatusCode.NoContent, await Save(root));
        Assert.Equal(HttpStatusCode.NoContent, await Save(root with { Name = "根" }));
        Assert.Equal(HttpStatusCode.BadRequest, await Save(Node("Duplicate", "根")));
        Assert.Equal(HttpStatusCode.BadRequest, await Save(root with { Children = [
            Node("A", "子", root.Id, root.Id), Node("B", " 子 ", root.Id, root.Id)] }));
        Assert.Equal(HttpStatusCode.NoContent, await Save(root with { Children = [
            Node("A", "甲", root.Id, root.Id) with { Children = [Node("AA", "同名", root.Id, "A")] },
            Node("B", "乙", root.Id, root.Id) with { Children = [Node("BB", "同名", root.Id, "B")] }] }));
        var concurrent = await Task.WhenAll(Save(Node("ConcurrentA", "并发")), Save(Node("ConcurrentB", "并发")));
        Assert.Single(concurrent, status => status == HttpStatusCode.NoContent);
        Assert.Single(concurrent, status => status == HttpStatusCode.BadRequest);

        async Task<HttpStatusCode> Save(DataScopeRuleDto rule)
        {
            using var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, save, HttpMethod.Put,
                "/api/authorization/data-scope/rules/" + rule.Id, rule);
            return response.StatusCode;
        }
    }

    private static DataScopeRuleDto Node(string id, string name, string? rootId = null, string? parentId = null) =>
        new(id, rootId ?? id, parentId, name, true, "organization.person",
            new("attribute", "EID", "equal", JsonSerializer.SerializeToElement("Person_1")), [], []);
}
