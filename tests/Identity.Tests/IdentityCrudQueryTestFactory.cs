using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Crud.Persistence;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Persistence.Descriptors;
using ICP.Platform.Modules.Identity.Repositories;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Presentation;
using ICP.Platform.Query.Runtime;

namespace ICP.Platform.Identity.Tests;

internal static class IdentityCrudQueryTestFactory
{
    public static IdentityManagementQueryService Create(IIdentityRepository repository, IPlatformQueryExecutor executor,
        ITemporalDisplayProjectionExecutor temporal, EfPlatformDbContext? context = null)
    {
        var plan = new CompiledPersistencePlan("candidate", "snapshot", "hash", default, "digest", [], IdentityModelDefinitions.All, []);
        var registry = new StandardCrudResourceRegistry([IdentityAccountCrudResource.Create()], plan);
        IStandardCrudRepository crudRepository = context is null
            ? new AccountReadRepository(repository)
            : new EfStandardCrudRepository(context, new PlatformObjectIdGenerator());
        var crud = new StandardCrudService(
            crudRepository,
            executor,
            new StandardCrudDefaultWriter(crudRepository, [], []),
            [new IdentityAccountCrudResource()],
            new DisplayValueProjection(
                [],
                temporalDisplayProjectionExecutor: temporal,
                userDisplayNameDirectory: new IdentityUserDisplayNameDirectory(new IdentityAccountDirectory(repository), new AllowAllReadableIds()),
                presentationContextAccessor: new TestPresentationContext(),
                textCatalog: RuntimeTextCatalog.Parse("""
                { "defaultLanguage": "zhs", "supportedLanguages": [
                    { "code": "zhs", "locale": "zh-CN" }, { "code": "en", "locale": "en-US" } ],
                  "catalogs": { "zhs": { "Platform.SystemActor": "系统" }, "en": { "Platform.SystemActor": "System" } } }
                """)),
            []);
        return new IdentityManagementQueryService(repository, crud, temporal, registry);
    }

    // 查询夹具显式沿用英文请求语境；系统操作者仍经正式运行时词典解析。
    private sealed class TestPresentationContext : IRequestPresentationContextAccessor
    {
        public RequestPresentationContext Current { get; } = new("en-US", "Asia/Taipei");
    }

    private sealed class AccountReadRepository(IIdentityRepository repository) : IStandardCrudRepository
    {
        public async Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default)
        {
            var account = await repository.FindAccountAsync(id, cancellationToken);
            return account is null ? null : descriptor.FieldSet.DetailFields.ToDictionary(field => field, field =>
            {
                var value = account.GetType().GetProperty(field)!.GetValue(account);
                return value is Enum enumeration ? enumeration.ToString() : value;
            });
        }
        public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(StandardCrudDescriptor descriptor, StandardCrudListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(StandardCrudDescriptor descriptor, StandardCrudUniqueConstraint constraint, IReadOnlyDictionary<string, object?> equalityValues, string? excludedId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, object?>> CreateAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(StandardCrudDescriptor descriptor, string id, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
