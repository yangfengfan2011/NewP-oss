using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Services;

/// <summary>显式建立默认公司和管理员部门；不修改、清理或推断既有业务组织。</summary>
public sealed class OrganizationInitializationService(EfPlatformDbContext db, IPlatformTransactionExecutor transactions,
    IPlatformObjectIdGenerator ids, IBusinessAuditRecorder recorder, IEntityAuditValueProvider audit)
{
    public const string CompanyCode = "NO_INFIGHTING";
    public const string DepartmentCode = "SYSTEM_ADMINISTRATION";
    public const string DepartmentId = "Department_00000000000000000000000000000001";
    private const string CompanyName = "No Infighting Company";

    /// <summary>在统一组织协调及事务中幂等初始化；提交前失败或取消整体回滚，提交后故障沿平台已提交结果传播。</summary>
    public async ValueTask<OrganizationInitializationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var operation = audit.BeginOperation();
        return await transactions.ExecuteAsync(["Company", "Department"], InitializeCoreAsync, cancellationToken);
    }

    // 只将本次缺少的默认档案及其业务记录登记到当前事务；保存、缓存发布和提交均由平台负责。
    private async ValueTask<OrganizationInitializationResult> InitializeCoreAsync(CancellationToken token)
    {
        var companies = Rows("Company");
        var departments = Rows("Department");
        var company = await companies.SingleOrDefaultAsync(row => EF.Property<string>(row, "ECODE") == CompanyCode, token);
        var companyCreated = company is null;
        if (company is null)
        {
            if (await companies.AnyAsync(row => EF.Property<string>(row, "ENAME") == CompanyName, token))
                throw new OrganizationInitializationConflictException();
            company = NewRow(ids.NewId("Company"), CompanyCode, CompanyName);
            companies.Add(company);
        }
        else if (!Equals(company["ENAME"], CompanyName)) throw new OrganizationInitializationConflictException();

        var companyId = (string)company["EID"]!;
        var department = await departments.SingleOrDefaultAsync(row => EF.Property<string>(row, "EID") == DepartmentId, token);
        var departmentCreated = department is null;
        if (department is null)
        {
            if (await departments.AnyAsync(row => EF.Property<string>(row, "CompanyId") == companyId
                && (EF.Property<string>(row, "ECODE") == DepartmentCode
                    || (EF.Property<string?>(row, "ParentDepartmentId") == null && EF.Property<string>(row, "ENAME") == "系统管理组")), token))
                throw new OrganizationInitializationConflictException();
            department = NewRow(DepartmentId, DepartmentCode, "系统管理组");
            department["CompanyId"] = companyId;
            department["ParentDepartmentId"] = null;
            departments.Add(department);
        }
        else if (!Equals(department["CompanyId"], companyId) || !Equals(department["ECODE"], DepartmentCode)
            || department.GetValueOrDefault("ParentDepartmentId") is not null)
            throw new OrganizationInitializationConflictException();

        if (companyCreated) await RecordCreatedAsync("company", company, token);
        if (departmentCreated) await RecordCreatedAsync("department", department, token);
        return new(companyId, DepartmentId, companyCreated, departmentCreated);
    }

    // 元数据与固定审计仍由正式持久化模型及审计提供者填充；初始化不接收客户端身份或时间。
    private static Dictionary<string, object?> NewRow(string id, string code, string name) => new()
    {
        ["EID"] = id, ["ECODE"] = code, ["ENAME"] = name, ["IsEnabled"] = true, ["SortOrder"] = "0"
    };

    // 默认部门初始化记录保留准确公司引用，管理员归属由该不可伪造的保留部门身份派生。
    private ValueTask RecordCreatedAsync(string kind, IReadOnlyDictionary<string, object?> row, CancellationToken token)
    {
        var fields = new List<BusinessAuditField>
        {
            new("ECODE", null, BusinessAuditValue.Text((string)row["ECODE"]!)),
            new("ENAME", null, BusinessAuditValue.Text((string)row["ENAME"]!))
        };
        if (kind == "department") fields.Add(new("CompanyId", null, BusinessAuditValue.Reference("company", (string)row["CompanyId"]!)));
        return recorder.RegisterAsync(new($"organization.{kind}.initialized", $"organization.{kind}", (string)row["EID"]!, fields), token);
    }

    // 模型身份经当前正式装配解析，避免手写表名成为第二事实源。
    private DbSet<Dictionary<string, object?>> Rows(string model) => db.Set<Dictionary<string, object?>>(db.ResolveModelKey(model));
}

/// <summary>实际初始化身份及本次新增情况；重试无变化时两个新增标志均为 false。</summary>
public sealed record OrganizationInitializationResult(string CompanyId, string DepartmentId, bool CompanyCreated, bool DepartmentCreated);

/// <summary>既有组织占用默认编码、名称或保留身份；必须显式解决冲突，不能覆盖业务数据。</summary>
public sealed class OrganizationInitializationConflictException : InvalidOperationException { }
