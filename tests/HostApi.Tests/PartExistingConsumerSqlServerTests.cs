using System.Net;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceObjectMutationSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "既有Part/Document内部删除及全部引用清理独立于基线重复编码冲突缺口")]
    public async Task PartWriteSource_MixedDeleteAndSingleDeleteRemoveEveryActualReference()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await RootAsync(fixture, admin);
        var other = await CreateAsync(fixture, admin, root, "folders", "create", "另一个出现位置");
        var part = await CreateAsync(fixture, admin, root, "parts", "createPart", "源零组件");
        var document = await CreateAsync(fixture, admin, root, "documents", "createDocument", "源图文档");
        var keep = await CreateAsync(fixture, admin, root, "parts", "createPart", "暂保留零组件");
        var otherPosition = root with { LinkIds = [.. root.LinkIds, other.Link] };
        foreach (var target in new[] { part, document })
        {
            using var added = await SendAsync(fixture, admin, "add", HttpMethod.Post, "/api/workspace/content/references",
                new { position = otherPosition, target = new { modelKey = target.Model, id = target.Id } });
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        }
        using (var wrong = await SendAsync(fixture, admin, "edit", HttpMethod.Put, "/api/workspace/content/" + part.Link,
            new { position = otherPosition, values = new { ENAME = "不得更换出现位置" } }))
            Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        using (var removed = await SendAsync(fixture, admin, "deleteObjects", HttpMethod.Post, "/api/workspace/content/delete",
            new { position = root, linkIds = new[] { part.Link, document.Link } }))
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            foreach (var target in new[] { part, document })
            {
                Assert.False(await db.Set<Dictionary<string, object?>>(target.Model).AnyAsync(row => EF.Property<string>(row, "EID") == target.Id));
                Assert.False(await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AnyAsync(row => EF.Property<string>(row, "CEID") == target.Id));
            }
            Assert.True(await db.Set<Dictionary<string, object?>>("Part").AnyAsync(row => EF.Property<string>(row, "EID") == keep.Id));
            Assert.False(await db.Set<Dictionary<string, object?>>("DocumentVersion").AnyAsync());
            Assert.False(await db.Set<Dictionary<string, object?>>("DocumentIteration").AnyAsync());
        }
        using var single = await SendAsync(fixture, admin, "delete", HttpMethod.Delete, "/api/workspace/content/" + keep.Link, new { position = root });
        Assert.Equal(HttpStatusCode.OK, single.StatusCode);
        using var afterScope = fixture.Services.CreateScope();
        var after = afterScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        Assert.False(await after.Set<Dictionary<string, object?>>("Part").AnyAsync());
        Assert.False(await after.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AnyAsync(row => EF.Property<string>(row, "CEID") == keep.Id));
    }
}
