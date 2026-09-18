using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ICP.Platform.HostApi.Tests;

/// <summary>
/// 只用于集成验证的正式组件 binding；它不声明业务模型，只向真实人员 CRUD 贡献横切。
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
    }
}

internal enum ObjectOperationExtensionProbeMode
{
    None,
    Supplement,
    Reject,
    InvalidFinalInput,
    ProtectedInput,
    AfterFailure
}

internal sealed class ObjectOperationExtensionProbeControl
{
    internal ObjectOperationExtensionProbeMode Mode { get; set; }
    internal string SupplementedName { get; set; } = "扩展补充人员";
    internal IReadOnlyDictionary<string, object?>? AfterInput { get; set; }
    internal IReadOnlyDictionary<string, object?>? AfterResult { get; set; }
    internal string? TargetId { get; set; }
    internal bool AfterMutationBlocked { get; set; }
}

internal sealed class PersonCreateProbeExtension(ObjectOperationExtensionProbeControl control)
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

    public ValueTask AfterAsync(
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
        return ValueTask.CompletedTask;
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
