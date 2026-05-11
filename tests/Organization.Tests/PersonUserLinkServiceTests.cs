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
    public async Task LinkUserAsync_WhenUserIdBlank_ThrowsStableError()
    {
        var service = BuildService(existingPersonIds: ["Person_00000000000000000000000000000001"]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LinkUserAsync(
                "Person_00000000000000000000000000000001",
                new LinkPersonUserRequest { UserId = "   " }));

        Assert.Equal("绑定账号不能为空。", ex.Message);
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

    [Fact]
    public async Task LinkUserAsync_WhenRelinkingSamePerson_PreservesPersonUserLinkId()
    {
        var repository = new InMemoryPersonUserLinkRepository();
        await repository.UpsertAsync(new PersonUserLink
        {
            Id = "PersonUserLink_existing",
            PersonId = "Person_target",
            UserId = "user-1",
            LinkedAt = DateTime.UtcNow,
            LinkedBy = "tester"
        });
        var service = BuildService(repository, ["Person_target"]);

        var link = await service.LinkUserAsync(
            "Person_target",
            new LinkPersonUserRequest { UserId = "user-2", LinkedBy = "tester" });

        Assert.Equal("PersonUserLink_existing", link.Id);
        Assert.Equal("Person_target", link.PersonId);
        Assert.Equal("user-2", link.UserId);
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

        public StandardCrudDescriptor? FindByResourceKey(string resourceKey) =>
            resourceKey == "organization.person" ? Descriptor() : null;

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

        public Task<StandardCrudDetail?> GetDetailAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_existingPersonIds.Contains(id)
                ? new StandardCrudDetail { Fields = new Dictionary<string, object?> { ["EID"] = id } }
                : null);

        public Task<StandardCrudListResult> ListAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudListQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudListQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StandardCrudMutationResult> CreateAsync(
            StandardCrudDescriptor descriptor,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StandardCrudMutationResult> UpdateAsync(
            StandardCrudDescriptor descriptor,
            string id,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StandardCrudMutationResult> DisableAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StandardCrudMutationResult> DeleteAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeIdentityUserDirectory : IIdentityUserDirectory
    {
        public Task EnsureUserExistsAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<IdentityUserOptionDto>> SearchAsync(
            string keyword,
            CancellationToken cancellationToken = default) =>
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

        public Task<bool> ExistsByUserIdAsync(
            string userId,
            string? excludedPersonId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_links.Any(link => link.UserId == userId && link.PersonId != excludedPersonId));

        public Task UpsertAsync(PersonUserLink link, CancellationToken cancellationToken = default)
        {
            _links.RemoveAll(existing => existing.PersonId == link.PersonId);
            _links.Add(link);
            return Task.CompletedTask;
        }
    }
}
