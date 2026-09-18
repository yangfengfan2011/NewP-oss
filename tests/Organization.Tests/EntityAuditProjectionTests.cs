using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Crud.Persistence;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Projection;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Presentation;
using ICP.Platform.Query.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class EntityAuditProjectionTests
{
    [Theory]
    [InlineData("zh-CN", "Asia/Taipei", "2026/9/4 15:24:26")]
    [InlineData("en-US", "Asia/Taipei", "9/4/2026 3:24:26 PM")]
    [InlineData("zh-CN", "America/Los_Angeles", "2026/9/4 00:24:26")]
    [InlineData("en-US", "America/Los_Angeles", "9/4/2026 12:24:26 AM")]
    public async Task CompanyAndPosition_AutomaticallyProjectAuditsAcrossCreateListDetailAndUpdate(string locale, string zone, string expected)
    {
        var models = OrganizationModelDefinitions.All.Where(model => model.Name is "Company" or "Position")
            .Select(model => model with { OwnerModuleId = "Organization" }).ToArray();
        var plan = new CompiledPersistencePlan("candidate", "snapshot", "hash", default, "digest", [], models, []);
        var registry = new StandardCrudResourceRegistry([CompanyCrudDescriptorFactory.Create(), PositionCrudDescriptorFactory.Create()], plan);
        var stamp = new FixedStamp();
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options;
        await using var context = new EfPlatformDbContext(options, plan, new EntityAuditValueProvider(stamp));
        var directory = new Names();
        IStandardCrudService service = new StandardCrudService(new EfStandardCrudRepository(context, new PlatformObjectIdGenerator()),
            new EfPlatformQueryExecutor(context), [], [], [], [], [], auditStampProvider: stamp,
            temporalDisplayProjectionExecutor: new TemporalDisplayProjectionExecutor(new Presentation(locale, zone), new TemporalDisplayFormatter()),
            userDisplayNameDirectory: directory);

        foreach (var descriptor in registry.GetAll())
        {
            var created = await service.CreateAsync(descriptor, new Dictionary<string, object?>
            {
                ["ECODE"] = "AUDIT-1", ["ENAME"] = "Audit resource", ["SortOrder"] = "0100"
            });
            Assert.True(created.Succeeded, created.Error?.Message);
            var original = created.Detail!.Fields;
            Assert.Equal(stamp.Instant, original["CreatedAt"]);
            Assert.Equal(expected, original["CreatedAt_DisplayValue"]);
            Assert.Equal("Current account", original["CreatedBy_DisplayValue"]);
            var id = Assert.IsType<string>(original["EID"]);
            context.ChangeTracker.Clear();

            var detail = await service.GetDetailAsync(descriptor, id);
            Assert.Equal(original["UpdatedAt"], detail!.Fields["UpdatedAt"]);
            Assert.Equal(expected, detail.Fields["UpdatedAt_DisplayValue"]);
            var list = await service.ListAsync(descriptor, new StandardCrudListQuery());
            var row = Assert.Single(list.Items);
            if (descriptor.FieldSet.ListFields.Contains("UpdatedAt"))
            {
                Assert.Equal(expected, row["UpdatedAt_DisplayValue"]);
                var selected = await service.ListAsync(descriptor, new StandardCrudListQuery(Fields: ["UpdatedAt_DisplayValue"]));
                Assert.Equal(stamp.Instant, Assert.Single(selected.Items)["UpdatedAt"]);
                var export = new StandardCrudExportService(service, new ICP.Platform.Crud.Exports.StandardCrudXlsxWriter(),
                    new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
                var file = await export.ExportAsync(descriptor, new StandardCrudExportRequest("current", [new("updatedAt", "UpdatedAt", "更新时间")]));
                using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(file.Content));
                using var sheet = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
                var cells = System.Xml.Linq.XDocument.Load(sheet).Descendants().Where(element => element.Name.LocalName == "t")
                    .Select(element => element.Value).ToArray();
                Assert.Equal(new[] { "更新时间", expected }, cells);
            }
            else
            {
                Assert.DoesNotContain("UpdatedAt_DisplayValue", row.Keys);
                await Assert.ThrowsAsync<PlatformQueryValidationException>(() =>
                    service.ListAsync(descriptor, new StandardCrudListQuery(Fields: ["UpdatedAt"])));
            }
            Assert.DoesNotContain("CreatedBy", row.Keys);
            Assert.DoesNotContain("CreatedBy_DisplayValue", row.Keys);

            stamp.Instant = stamp.Instant.AddSeconds(1);
            var updated = await service.UpdateAsync(descriptor, id, new Dictionary<string, object?> { ["ENAME"] = "Updated resource" });
            Assert.True(updated.Succeeded, updated.Error?.Message);
            Assert.Equal(original["CreatedAt"], updated.Detail!.Fields["CreatedAt"]);
            Assert.Equal(stamp.Instant, updated.Detail.Fields["UpdatedAt"]);
            foreach (var field in new[] { "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt" })
            {
                var rejected = await service.UpdateAsync(descriptor, id, new Dictionary<string, object?> { [field + "_DisplayValue"] = "forged" });
                Assert.False(rejected.Succeeded);
                Assert.Equal("StandardCrud.DisplayValueFieldReadOnly", rejected.Error!.Code);
                var rawRejected = await service.UpdateAsync(descriptor, id, new Dictionary<string, object?> { [field] = "forged" });
                Assert.Equal("StandardCrud.ServerOwnedFieldReadOnly", rawRejected.Error!.Code);
            }
            stamp.Instant = stamp.Instant.AddSeconds(-1);
        }
    }

    private sealed class FixedStamp : IEntityAuditStampProvider
    {
        public DateTime Instant { get; set; } = new DateTime(2026, 9, 4, 7, 24, 26, DateTimeKind.Utc).AddTicks(5023291);
        public EntityAuditStamp GetCurrent() => new("actor-1", Instant);
    }
    private sealed class Names : IUserDisplayNameDirectory
    {
        public Task<IReadOnlyDictionary<string, string>> FindManyAsync(IReadOnlyCollection<string> accountIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { ["actor-1"] = "Current account" });
    }
    private sealed class Presentation(string locale, string zone) : IRequestPresentationContextAccessor
    {
        public RequestPresentationContext? Current => new(locale, zone);
    }
}
