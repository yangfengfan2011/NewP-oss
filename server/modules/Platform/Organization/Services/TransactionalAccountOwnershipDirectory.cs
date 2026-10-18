using ICP.Platform.Identity.Contracts;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Ownership;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Services;

/// <summary>只在共享事务内读取 Organization 自有当前行；账号事实仍来自 Identity 稳定契约。</summary>
public sealed class TransactionalAccountOwnershipDirectory(EfPlatformDbContext db, IPlatformTransactionExecutor transactions,
    IIdentityAccountDirectory accounts) : ITransactionalAccountOwnershipDirectory
{
    /// <inheritdoc/>
    public async ValueTask<AccountOwnership?> FindAsync(string accountId, CancellationToken cancellationToken = default)
    {
        if (!transactions.IsActive) throw new InvalidOperationException("ENTITY_OWNERSHIP_TRANSACTION_REQUIRED");
        var account = await accounts.FindAsync(accountId, cancellationToken);
        if (account is null) return null;
        string? departmentId;
        if (account.IsLocalAdministrator)
        {
            departmentId = await db.Set<Dictionary<string, object?>>("Department").AsNoTracking()
                .Where(row => EF.Property<string>(row, "EID") == OrganizationInitializationService.DepartmentId
                    && db.Set<Dictionary<string, object?>>("Company").Any(company =>
                        EF.Property<string>(company, "EID") == EF.Property<string>(row, "CompanyId")
                        && EF.Property<string>(company, "ECODE") == OrganizationInitializationService.CompanyCode))
                .Select(row => EF.Property<string>(row, "EID")).SingleOrDefaultAsync(cancellationToken);
        }
        else
        {
            departmentId = await db.Set<Dictionary<string, object?>>("Person").AsNoTracking()
                .Where(row => EF.Property<string?>(row, "AccountEID") == accountId)
                .Select(row => EF.Property<string?>(row, "DepartmentId")).SingleOrDefaultAsync(cancellationToken);
            if (departmentId is not null && !await db.Set<Dictionary<string, object?>>("Department").AsNoTracking()
                    .AnyAsync(row => EF.Property<string>(row, "EID") == departmentId, cancellationToken)) departmentId = null;
        }
        return new(account.AccountEid, departmentId);
    }
}
