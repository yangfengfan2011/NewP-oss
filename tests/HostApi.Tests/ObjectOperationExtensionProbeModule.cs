using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ICP.Platform.HostApi.Tests;

/// <summary>
/// 只用于集成验证的正式组件 binding；它不声明业务模型，只向真实 Organization CRUD 贡献横切。
/// </summary>
public sealed class ObjectOperationExtensionProbeModule : ModuleCapsuleBindingBase
{
    public override string BehaviorKey => "host-api-tests.object-operation-extension-probe";

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton<ObjectOperationExtensionProbeControl>();
        services.AddObjectOperationExtension<PersonCreateProbeExtension>(this, PersonCreateProbeExtension.Definition);
        services.AddObjectOperationExtension<PersonCreateProbeExtension>(this, PersonCreateProbeExtension.UpdateDefinition);
        services.AddObjectOperationExtension<PersonCreateProbeExtension>(this, PersonCreateProbeExtension.DeleteDefinition);
        foreach (var definition in PersonCreateProbeExtension.CompanyAndPositionDefinitions)
        {
            services.AddObjectOperationExtension<PersonCreateProbeExtension>(this, definition);
        }
        services.AddObjectOperationExtension<OrganizationNestedWriteProbeExtension>(
            this,
            OrganizationNestedWriteProbeExtension.CompanyCreateDefinition);
        services.AddObjectOperationExtension<OrganizationNestedWriteProbeExtension>(
            this,
            OrganizationNestedWriteProbeExtension.CompanyUpdateDefinition);
    }
}

internal enum ObjectOperationExtensionProbeMode
{
    None,
    Supplement,
    Reject,
    InvalidFinalInput,
    ProtectedInput,
    AfterFailure,
    NestedCreate,
    NestedDisableThenCreate,
    NestedDeleteThenCreate,
    AfterRepeatDelete
}

internal sealed class ObjectOperationExtensionProbeControl
{
    internal ObjectOperationExtensionProbeMode Mode { get; set; }
    internal string SupplementedName { get; set; } = "扩展补充人员";
    internal IReadOnlyDictionary<string, object?>? AfterInput { get; set; }
    internal IReadOnlyDictionary<string, object?>? AfterResult { get; set; }
    internal string? TargetId { get; set; }
    internal bool AfterMutationBlocked { get; set; }
    internal string? NestedCompanyId { get; set; }
    internal string? NestedDepartmentId { get; set; }
    internal string? NestedPersonId { get; set; }
    internal StandardCrudMutationResult? NestedDepartmentResult { get; set; }
    internal StandardCrudMutationResult? NestedPersonResult { get; set; }
    internal StandardCrudMutationResult? RepeatedDeleteResult { get; set; }
    internal TaskCompletionSource? NestedWritesCompleted { get; set; }
    internal TaskCompletionSource? ReleaseNestedWrite { get; set; }
}

internal sealed class PersonCreateProbeExtension(
    ObjectOperationExtensionProbeControl control,
    IServiceProvider services)
    : IObjectOperationExtension
{
    internal static ObjectOperationExtensionDefinition Definition { get; } = new(
        "host-api-tests.person-create-probe",
        "Organization",
        "organization.person",
        ObjectOperationAction.Create);

    internal static ObjectOperationExtensionDefinition UpdateDefinition { get; } = new(
        "host-api-tests.person-crud-probe",
        "Organization",
        "organization.person",
        ObjectOperationAction.Update);

    internal static ObjectOperationExtensionDefinition DeleteDefinition { get; } = new(
        "host-api-tests.person-crud-probe",
        "Organization",
        "organization.person",
        ObjectOperationAction.Delete);

    internal static IReadOnlyList<ObjectOperationExtensionDefinition> CompanyAndPositionDefinitions { get; } =
    [
        new("host-api-tests.company-crud-probe", "Organization", "organization.company", ObjectOperationAction.Create),
        new("host-api-tests.company-crud-probe", "Organization", "organization.company", ObjectOperationAction.Update),
        new(
            "host-api-tests.company-crud-probe",
            "Organization",
            "organization.company",
            ObjectOperationAction.Delete,
            calls: [new ObjectOperationTarget("organization.company", ObjectOperationAction.Delete)]),
        new("host-api-tests.position-crud-probe", "Organization", "organization.position", ObjectOperationAction.Create),
        new("host-api-tests.position-crud-probe", "Organization", "organization.position", ObjectOperationAction.Update),
        new("host-api-tests.position-crud-probe", "Organization", "organization.position", ObjectOperationAction.Delete)
    ];

    public ValueTask<StandardCrudError?> BeforeAsync(
        ObjectOperationBeforeContext context,
        CancellationToken cancellationToken = default)
    {
        control.TargetId = context.TargetId;
        if (control.Mode == ObjectOperationExtensionProbeMode.Supplement)
        {
            context.SetValue("ENAME", control.SupplementedName);
        }
        else if (control.Mode == ObjectOperationExtensionProbeMode.InvalidFinalInput)
        {
            context.SetValue("ENAME", " ");
        }
        else if (control.Mode == ObjectOperationExtensionProbeMode.ProtectedInput)
        {
            context.SetValue("CreatedBy", "extension-actor");
        }
        else if (control.Mode == ObjectOperationExtensionProbeMode.Reject)
        {
            return ValueTask.FromResult<StandardCrudError?>(new StandardCrudError
            {
                StatusCode = 409,
                Code = "Organization.Person.ExtensionRejected",
                Message = "测试扩展拒绝人员操作。"
            });
        }
        return ValueTask.FromResult<StandardCrudError?>(null);
    }

    public async ValueTask AfterAsync(
        ObjectOperationAfterContext context,
        CancellationToken cancellationToken = default)
    {
        control.AfterInput = context.InputValues;
        control.AfterResult = context.ResultFields;
        control.AfterMutationBlocked = context.ResultFields is not IDictionary<string, object?> mutable
            || ThrowsNotSupported(() => mutable["ENAME"] = "after 不得改写");
        if (control.Mode == ObjectOperationExtensionProbeMode.AfterFailure)
        {
            throw new InvalidOperationException("injected object-operation after failure");
        }
        if (control.Mode == ObjectOperationExtensionProbeMode.AfterRepeatDelete
            && context.Action == ObjectOperationAction.Delete)
        {
            var descriptor = services.GetRequiredService<IStandardCrudResourceRegistry>()
                .FindByResourceKey(context.ObjectKey)
                ?? throw new InvalidOperationException($"Missing test descriptor: {context.ObjectKey}");
            var adapter = services.GetServices<IStandardCrudConditionalDeleteExecutor>()
                .Single(executor => string.Equals(executor.ResourceKey, context.ObjectKey, StringComparison.Ordinal));
            control.RepeatedDeleteResult = await adapter.DeleteAsync(
                descriptor,
                context.TargetId!,
                cancellationToken);
        }
        return;
    }

    // 真实尝试修改运行时对象，证明 after 获得的不是 tracked 字典的只读静态视图。
    private static bool ThrowsNotSupported(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (NotSupportedException)
        {
            return true;
        }
    }
}

internal sealed class OrganizationNestedWriteProbeExtension(
    Func<IObjectOperationExecutor> executor,
    ObjectOperationExtensionProbeControl control) : IObjectOperationExtension
{
    internal static ObjectOperationExtensionDefinition CompanyCreateDefinition { get; } = new(
        "host-api-tests.organization-nested-write-probe",
        "Organization",
        "organization.company",
        ObjectOperationAction.Create,
        calls:
        [
            new ObjectOperationTarget("organization.department", ObjectOperationAction.Create),
            new ObjectOperationTarget("organization.person", ObjectOperationAction.Create)
        ]);

    internal static ObjectOperationExtensionDefinition CompanyUpdateDefinition { get; } = new(
        "host-api-tests.organization-nested-write-probe",
        "Organization",
        "organization.company",
        ObjectOperationAction.Update,
        calls:
        [
            new ObjectOperationTarget("organization.department", ObjectOperationAction.Update),
            new ObjectOperationTarget("organization.department", ObjectOperationAction.Delete),
            new ObjectOperationTarget("organization.person", ObjectOperationAction.Create)
        ]);

    /// <inheritdoc />
    public ValueTask<StandardCrudError?> BeforeAsync(
        ObjectOperationBeforeContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<StandardCrudError?>(null);

    /// <inheritdoc />
    public async ValueTask AfterAsync(
        ObjectOperationAfterContext context,
        CancellationToken cancellationToken = default)
    {
        if (control.Mode == ObjectOperationExtensionProbeMode.NestedCreate)
        {
            await CreateDepartmentAndPersonAsync(context, cancellationToken);
            return;
        }
        if (control.Mode == ObjectOperationExtensionProbeMode.NestedDisableThenCreate)
        {
            await executor().ExecuteAsync(
                "organization.department",
                ObjectOperationAction.Update,
                control.NestedDepartmentId,
                new Dictionary<string, object?> { ["IsEnabled"] = false },
                cancellationToken);
            control.NestedPersonResult = await CreatePersonAsync(cancellationToken);
            return;
        }
        if (control.Mode == ObjectOperationExtensionProbeMode.NestedDeleteThenCreate)
        {
            await executor().ExecuteAsync(
                "organization.department",
                ObjectOperationAction.Delete,
                control.NestedDepartmentId,
                new Dictionary<string, object?>(),
                cancellationToken);
            control.NestedPersonResult = await CreatePersonAsync(cancellationToken);
        }
    }

    // 公司核心提供真实 EID，后续两个正式入口按保存顺序建立完整关系。
    private async Task CreateDepartmentAndPersonAsync(
        ObjectOperationAfterContext context,
        CancellationToken cancellationToken)
    {
        control.NestedCompanyId = Convert.ToString(context.ResultFields!["EID"]);
        var department = await executor().ExecuteAsync(
            "organization.department",
            ObjectOperationAction.Create,
            null,
            new Dictionary<string, object?>
            {
                ["CompanyId"] = control.NestedCompanyId,
                ["ECODE"] = "NESTED-DEPARTMENT",
                ["ENAME"] = "嵌套部门",
                ["SortOrder"] = "0100"
            },
            cancellationToken);
        control.NestedDepartmentResult = department;
        control.NestedDepartmentId = Convert.ToString(department.Detail!.Fields["EID"]);
        control.NestedPersonResult = await CreatePersonAsync(cancellationToken);
        control.NestedPersonId = Convert.ToString(control.NestedPersonResult.Detail!.Fields["EID"]);
        control.NestedWritesCompleted?.TrySetResult();
        if (control.ReleaseNestedWrite is not null)
        {
            await control.ReleaseNestedWrite.Task.WaitAsync(cancellationToken);
        }
    }

    // 人员始终引用前序操作的实际公司与部门标识，验证事务内有效视图而非固定夹具。
    private Task<StandardCrudMutationResult> CreatePersonAsync(CancellationToken cancellationToken) =>
        executor().ExecuteAsync(
            "organization.person",
            ObjectOperationAction.Create,
            null,
            new Dictionary<string, object?>
            {
                ["PersonNo"] = "NESTED-PERSON-" + Guid.NewGuid().ToString("N"),
                ["ENAME"] = "嵌套人员",
                ["CompanyId"] = control.NestedCompanyId,
                ["DepartmentId"] = control.NestedDepartmentId,
                ["EmploymentStatus"] = "Active",
                ["IsEnabled"] = true
            },
            cancellationToken);
}

internal sealed class InvalidOrderPersonCreateProbeExtension : IObjectOperationExtension
{
    internal static ObjectOperationExtensionDefinition Definition { get; } = new(
        "host-api-tests.invalid-order-probe",
        "Organization",
        "organization.person",
        ObjectOperationAction.Create,
        runsAfter: ["host-api-tests.absent-probe"]);

    public ValueTask<StandardCrudError?> BeforeAsync(
        ObjectOperationBeforeContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<StandardCrudError?>(null);

    public ValueTask AfterAsync(
        ObjectOperationAfterContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
