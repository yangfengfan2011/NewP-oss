using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Persistence.Ef;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class DepartmentCrudValidator : IStandardCrudValidator
{
    private readonly EfPlatformDbContext _dbContext;
    private readonly IDepartmentHierarchyQuery _hierarchyQuery;

    /// <summary>
    /// 使用 Organization 当前 SQL Server 层级查询构造部门写入校验器。
    /// </summary>
    public DepartmentCrudValidator(EfPlatformDbContext dbContext)
        : this(dbContext, new SqlServerDepartmentHierarchyQuery(dbContext))
    {
    }

    // 测试可替换内部层级查询，不把 provider 细节扩大到公共模块协议。
    internal DepartmentCrudValidator(
        EfPlatformDbContext dbContext,
        IDepartmentHierarchyQuery hierarchyQuery)
    {
        _dbContext = dbContext;
        _hierarchyQuery = hierarchyQuery;
    }

    public string ResourceKey => "organization.department";

    /// <summary>
    /// 校验部门创建时的排序码、所属公司与可选同公司启用上级。
    /// </summary>
    public async Task<StandardCrudError?> ValidateCreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        _ = descriptor;
        var sortOrderError = ValidateSortOrder(values);
        if (sortOrderError is not null)
        {
            return sortOrderError;
        }

        var companyId = values.GetValueOrDefault("CompanyId")?.ToString();
        if (string.IsNullOrWhiteSpace(companyId))
        {
            return null;
        }

        var companyState = await Companies()
            .AsNoTracking()
            .Where(company => EF.Property<string>(company, "EID") == companyId)
            .Select(company => new { IsEnabled = EF.Property<bool>(company, "IsEnabled") })
            .SingleOrDefaultAsync(cancellationToken);
        var companyError = companyState is null
            ? CompanyError(404, "CompanyNotFound", "所属公司不存在。")
            : companyState.IsEnabled
                ? null
                : CompanyError(409, "CompanyDisabled", "所属公司已停用，不能新建部门。");
        if (companyError is not null)
        {
            return companyError;
        }

        return await ValidateParentAsync(
            companyId,
            ReadNullableId(values, "ParentDepartmentId"),
            currentDepartmentId: null,
            cancellationToken);
    }

    /// <summary>
    /// 校验部门普通编辑中的排序码、同公司换父、防环与启用子级停用守卫。
    /// </summary>
    public async Task<StandardCrudError?> ValidateUpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        _ = descriptor;
        var sortOrderError = ValidateSortOrder(values);
        if (sortOrderError is not null)
        {
            return sortOrderError;
        }

        var current = await FindDepartmentAsync(id, cancellationToken);
        if (current is null)
        {
            return null;
        }

        if (values.GetValueOrDefault("IsEnabled") is false
            && await Departments().AsNoTracking().AnyAsync(
                department => EF.Property<string?>(department, "ParentDepartmentId") == id
                    && EF.Property<bool>(department, "IsEnabled"),
                cancellationToken))
        {
            return LifecycleError(
                "DisableBlockedByEnabledChild",
                "部门仍有启用子部门，不能停用。");
        }

        if (!values.ContainsKey("ParentDepartmentId"))
        {
            return null;
        }

        return await ValidateParentAsync(
            current.CompanyId,
            ReadNullableId(values, "ParentDepartmentId"),
            id,
            cancellationToken);
    }

    // 父引用必须存在、启用且属于同一公司；编辑时用数据库递归查询拒绝自身和任意深度后代。
    private async Task<StandardCrudError?> ValidateParentAsync(
        string companyId,
        string? parentDepartmentId,
        string? currentDepartmentId,
        CancellationToken cancellationToken)
    {
        if (parentDepartmentId is null)
        {
            return null;
        }

        var parent = await FindDepartmentAsync(parentDepartmentId, cancellationToken);
        if (parent is null)
        {
            return ParentError(404, "ParentNotFound", "上级部门不存在。");
        }

        if (!string.Equals(parent.CompanyId, companyId, StringComparison.Ordinal))
        {
            return ParentError(400, "ParentCompanyMismatch", "上级部门必须属于同一公司。");
        }

        if (!parent.IsEnabled)
        {
            return ParentError(400, "ParentDisabled", "上级部门已停用，不能作为新的上级。");
        }

        if (currentDepartmentId is null)
        {
            return null;
        }

        var relationship = await _hierarchyQuery.ClassifyParentAsync(
            companyId,
            currentDepartmentId,
            parentDepartmentId,
            cancellationToken);

        return relationship switch
        {
            DepartmentParentRelationship.Self => ParentError(
                400,
                "ParentSelfReference",
                "部门不能把自身设为上级。"),
            DepartmentParentRelationship.Descendant => ParentError(
                400,
                "ParentCycle",
                "上级部门不能是当前部门的后代。"),
            DepartmentParentRelationship.Corrupted => LifecycleError(
                "HierarchyCorrupted",
                "现有部门层级包含环或断链，不能调整上级。"),
            _ => null
        };
    }

    // 单次读取一个部门状态所需的最小字段，避免把动态持久化行泄漏到领域校验分支。
    private Task<DepartmentState?> FindDepartmentAsync(string id, CancellationToken cancellationToken) =>
        Departments()
            .AsNoTracking()
            .Where(department => EF.Property<string>(department, "EID") == id)
            .Select(department => new DepartmentState(
                EF.Property<string>(department, "EID"),
                EF.Property<string>(department, "CompanyId"),
                EF.Property<string?>(department, "ParentDepartmentId"),
                EF.Property<bool>(department, "IsEnabled")))
            .SingleOrDefaultAsync(cancellationToken);

    // 空字符串与 null 都表示根部门；字段策略会在持久化前把空字符串规范化为 null。
    private static string? ReadNullableId(IReadOnlyDictionary<string, object?> values, string field) =>
        values.GetValueOrDefault(field)?.ToString() is { } value && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    // 排序码必须原样由一至三十二个 ASCII 数字组成，不能接受 Unicode 数字或隐式 trim。
    private static StandardCrudError? ValidateSortOrder(IReadOnlyDictionary<string, object?> values)
    {
        if (!values.TryGetValue("SortOrder", out var value))
        {
            return null;
        }

        var sortOrder = value as string;
        return sortOrder is { Length: >= 1 and <= 32 }
            && sortOrder.All(static character => character is >= '0' and <= '9')
                ? null
                : SortOrderError();
    }

    // 公司状态从 Organization 持久化边界直接读取，不恢复旧 Department service。
    private DbSet<Dictionary<string, object?>> Companies() =>
        _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey("Company"));

    // 部门状态只从 Organization 自有模型读取，层级校验不经过第二套写入服务。
    private DbSet<Dictionary<string, object?>> Departments() =>
        _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey("Department"));

    // 排序码格式错误绑定到 SortOrder，供标准 Drawer 精确展示。
    private static StandardCrudError SortOrderError() => new()
    {
        StatusCode = 400,
        Code = "Organization.Department.SortOrderInvalid",
        Message = "排序码必须由 1 至 32 位 ASCII 数字组成。",
        ReasonKey = "organization.department.sortOrderInvalid",
        FieldErrors =
        [
            new StandardCrudFieldError(
                "SortOrder",
                "Organization.Department.SortOrderInvalid",
                "排序码必须由 1 至 32 位 ASCII 数字组成。")
        ]
    };

    // 公司引用失败保持资源稳定码，并把错误精确绑定到 CompanyId。
    private static StandardCrudError CompanyError(int statusCode, string codeStem, string message) => new()
    {
        StatusCode = statusCode,
        Code = $"Organization.Department.{codeStem}",
        Message = message,
        ReasonKey = $"organization.department.{char.ToLowerInvariant(codeStem[0])}{codeStem[1..]}",
        FieldErrors = [new StandardCrudFieldError("CompanyId", $"Organization.Department.{codeStem}", message)]
    };

    // 可定位的父部门输入错误统一绑定 ParentDepartmentId。
    private static StandardCrudError ParentError(int statusCode, string codeStem, string message) => new()
    {
        StatusCode = statusCode,
        Code = $"Organization.Department.{codeStem}",
        Message = message,
        ReasonKey = $"organization.department.{char.ToLowerInvariant(codeStem[0])}{codeStem[1..]}",
        FieldErrors = [new StandardCrudFieldError("ParentDepartmentId", $"Organization.Department.{codeStem}", message)]
    };

    // 生命周期冲突不归因于单个可修正字段，返回资源稳定 409。
    private static StandardCrudError LifecycleError(string codeStem, string message) => new()
    {
        StatusCode = 409,
        Code = $"Organization.Department.{codeStem}",
        Message = message,
        ReasonKey = $"organization.department.{char.ToLowerInvariant(codeStem[0])}{codeStem[1..]}"
    };

    private sealed record DepartmentState(
        string Id,
        string CompanyId,
        string? ParentDepartmentId,
        bool IsEnabled);
}
