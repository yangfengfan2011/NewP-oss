using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modeling;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Presentation;

namespace ICP.Platform.Crud.Runtime;

/// <summary>
/// 把任意正式调用来源的原始创建输入收敛为对象行为可持久化的最终输入。
/// </summary>
public sealed class StandardCrudCreateRequestPreparer
{
    private readonly IStandardCrudRepository _repository;
    private readonly IReadOnlyDictionary<string, IStandardCrudValidator> _validators;
    private readonly ITemporalInputNormalizer? _temporalInputNormalizer;
    private readonly IEntityAuditValueProvider? _auditValues;

    /// <summary>组合标准创建规则所需的仓储、对象 validator、temporal 与可信审计能力。</summary>
    public StandardCrudCreateRequestPreparer(
        IStandardCrudRepository repository,
        IEnumerable<IStandardCrudValidator> validators,
        ITemporalInputNormalizer? temporalInputNormalizer = null,
        IEntityAuditValueProvider? auditValues = null)
    {
        _repository = repository;
        _validators = validators.ToDictionary(static item => item.ResourceKey, StringComparer.Ordinal);
        _temporalInputNormalizer = temporalInputNormalizer;
        _auditValues = auditValues;
    }

    // 对同一份输入按所有权、规范化、必填、审计、唯一性和对象规则的固定顺序收敛。
    internal async Task<StandardCrudCreatePreparation> PrepareAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
        var error = FindReadOnlyOwnershipFieldError(descriptor, values)
            ?? FindReadOnlyDisplayFieldError(descriptor, values);
        if (error is not null)
        {
            return StandardCrudCreatePreparation.Failure(error);
        }

        var allowed = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in descriptor.FieldSet.CreateFields)
        {
            if (values.TryGetValue(field, out var value))
            {
                allowed[field] = value;
            }
        }

        error = NormalizeTemporalInputs(descriptor, allowed)
            ?? ValidateRequiredFields(descriptor, allowed);
        if (error is not null)
        {
            return StandardCrudCreatePreparation.Failure(error);
        }

        ApplyCreateDefaults(descriptor, allowed);
        error = ValidateFieldPolicies(descriptor, allowed);
        if (error is not null)
        {
            return StandardCrudCreatePreparation.Failure(error);
        }

        error = ApplyCreateAuditFields(descriptor, allowed)
            ?? ValidateStorageInvariants(descriptor, allowed)
            ?? await ValidateUniqueConstraintsAsync(descriptor, allowed, cancellationToken);
        if (error is not null)
        {
            return StandardCrudCreatePreparation.Failure(error);
        }

        if (_validators.TryGetValue(descriptor.ResourceKey, out var validator))
        {
            error = await validator.ValidateCreateAsync(descriptor, allowed, cancellationToken);
            if (error is not null)
            {
                return StandardCrudCreatePreparation.Failure(error);
            }
        }

        return StandardCrudCreatePreparation.Success(allowed);
    }

    // 在过滤未知字段前拒绝客户端伪造服务端拥有字段。
    private static StandardCrudError? FindReadOnlyOwnershipFieldError(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values)
    {
        var auditField = descriptor.DisplayProjectionPlan?.Fields.FirstOrDefault(plan =>
            plan.IsEntityAuditField && values.ContainsKey(plan.SourceField));
        if (auditField is not null)
        {
            return ReadOnlyField(auditField.SourceField, "StandardCrud.ServerOwnedFieldReadOnly",
                "standardCrud.serverOwnedFieldReadOnly", "服务端字段");
        }

        var submitted = descriptor.FieldPolicies.FirstOrDefault(policy =>
            policy.ValueOwner is StandardCrudFieldValueOwner.Server or StandardCrudFieldValueOwner.ServerOnCreate
            && values.ContainsKey(policy.Field));
        return submitted is null
            ? null
            : ReadOnlyField(submitted.Field, "StandardCrud.ServerOwnedFieldReadOnly",
                "standardCrud.serverOwnedFieldReadOnly", "服务端字段");
    }

    // canonical display companion 只允许输出；legacy 非 canonical 字段继续由白名单过滤。
    private static StandardCrudError? FindReadOnlyDisplayFieldError(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values)
    {
        var submitted = StandardCrudQueryDescriptorAdapter.GetQueryDisplayValueFields(descriptor)
            .FirstOrDefault(field =>
                string.Equals(field.Field, string.Concat(field.SourceField, "_DisplayValue"), StringComparison.Ordinal)
                && values.ContainsKey(field.Field));
        if (submitted is null)
        {
            return null;
        }

        var message = $"标准 CRUD 显示字段 {submitted.Field} 只允许输出，不能提交写入。";
        return new StandardCrudError
        {
            StatusCode = 400,
            Code = "StandardCrud.DisplayValueFieldReadOnly",
            ReasonKey = "standardCrud.displayValueFieldReadOnly",
            Message = message,
            FieldErrors =
            [
                new StandardCrudFieldError(
                    submitted.Field,
                    "StandardCrud.DisplayValueFieldReadOnly",
                    message)
            ]
        };
    }

    // 只读字段错误保持既有稳定 Problem Details 与字段定位语义。
    private static StandardCrudError ReadOnlyField(
        string field,
        string code,
        string reasonKey,
        string kind)
    {
        var message = $"标准 CRUD {kind} {field} 不能由客户端提交。";
        return new StandardCrudError
        {
            StatusCode = 400,
            Code = code,
            ReasonKey = reasonKey,
            Message = message,
            FieldErrors = [new StandardCrudFieldError(field, code, message)]
        };
    }

    // temporal 字段在对象 validator 前转换为 DateOnly 或 UTC DateTime。
    private StandardCrudError? NormalizeTemporalInputs(
        StandardCrudDescriptor descriptor,
        IDictionary<string, object?> values)
    {
        foreach (var policy in descriptor.FieldPolicies.Where(static policy =>
                     policy.ValueType is FieldValueType.CalendarDate or FieldValueType.Instant))
        {
            if (!values.TryGetValue(policy.Field, out var value))
            {
                continue;
            }

            if (_temporalInputNormalizer is null)
            {
                throw new InvalidOperationException(
                    $"标准 CRUD 资源 {descriptor.ResourceKey} 声明了 temporal 输入字段，但未注册平台规范化器。");
            }

            var result = _temporalInputNormalizer.Normalize(policy.ValueType, value);
            if (!result.IsSuccess)
            {
                return new StandardCrudError
                {
                    StatusCode = 400,
                    Code = result.ErrorCode!,
                    ReasonKey = "standardCrud.temporalInputInvalid",
                    Message = result.Message!,
                    FieldErrors = [new StandardCrudFieldError(policy.Field, result.ErrorCode!, result.Message!)]
                };
            }

            values[policy.Field] = result.Value;
        }

        return null;
    }

    // 创建必填规则在默认值前检查调用方责任字段。
    private static StandardCrudError? ValidateRequiredFields(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values)
    {
        var missing = descriptor.RequiredFields.FirstOrDefault(field =>
            field.Scope is StandardCrudRequiredFieldScope.CreateOnly or StandardCrudRequiredFieldScope.CreateAndUpdate
            && (!values.TryGetValue(field.Field, out var value) || IsMissing(value)));
        return missing is null
            ? null
            : new StandardCrudError
            {
                StatusCode = 400,
                Code = missing.ErrorCode,
                Message = missing.Message,
                ReasonKey = missing.ReasonKey,
                FieldErrors = [new StandardCrudFieldError(missing.Field, missing.ErrorCode, missing.Message)]
            };
    }

    // 默认值只补齐未提交字段，不覆盖调用方规范输入。
    private static void ApplyCreateDefaults(
        StandardCrudDescriptor descriptor,
        IDictionary<string, object?> values)
    {
        foreach (var policy in descriptor.FieldPolicies)
        {
            if (!values.ContainsKey(policy.Field) && policy.CreateDefaultValue is not null)
            {
                values[policy.Field] = policy.CreateDefaultValue;
            }
        }
    }

    // 短文本规范与长度在进入对象业务规则前失败。
    private static StandardCrudError? ValidateFieldPolicies(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values)
    {
        foreach (var policy in descriptor.FieldPolicies)
        {
            if (!values.TryGetValue(policy.Field, out var value) || value is not string text)
            {
                continue;
            }

            if (policy.Trim && !string.Equals(text, text.Trim(), StringComparison.Ordinal)
                || policy.EmptyAsNull && text.Length == 0)
            {
                return FieldPolicyError(policy.Field, "StandardCrud.ShortTextNotNormalized",
                    $"字段 {policy.Field} 必须使用去除首尾空白后的规范短文本，空值请提交 null。",
                    "standardCrud.shortTextNotNormalized");
            }

            if (policy.MaxLength is { } maxLength && text.Length > maxLength)
            {
                return FieldPolicyError(policy.Field, "StandardCrud.ShortTextTooLong",
                    $"字段 {policy.Field} 不能超过 {maxLength} 个字符。", "standardCrud.shortTextTooLong");
            }
        }

        return null;
    }

    // Entity 四字段只接受统一可信 owner，并在 operation scope 内固定为同一次实际写入值。
    private StandardCrudError? ApplyCreateAuditFields(
        StandardCrudDescriptor descriptor,
        IDictionary<string, object?> values)
    {
        var fields = descriptor.FieldPolicies
            .Where(static policy => policy.ValueOwner == StandardCrudFieldValueOwner.Server
                && policy.Field is "CreatedBy" or "CreatedAt" or "UpdatedBy" or "UpdatedAt")
            .Select(static policy => policy.Field)
            .ToArray();
        if (fields.Length == 0)
        {
            if (descriptor.FieldSet.DetailFields.Contains("CreatedAt", StringComparer.Ordinal))
            {
                values["CreatedAt"] = DateTime.UtcNow;
            }

            return null;
        }

        var required = descriptor.FieldPolicies.FirstOrDefault(policy =>
            policy.StorageRequired && fields.Contains(policy.Field, StringComparer.Ordinal));
        try
        {
            if (_auditValues is null)
            {
                return required is null ? null : ServerOwnedValueUnavailable(descriptor, required.Field);
            }

            _auditValues.ForCreate().ApplyTo(values, fields);
            return null;
        }
        catch (EntityAuditStampUnavailableException)
        {
            return required is null ? null : ServerOwnedValueUnavailable(descriptor, required.Field);
        }
    }

    // 调用方拥有的存储必填字段必须在最终候选中成立。
    private static StandardCrudError? ValidateStorageInvariants(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values)
    {
        var invalid = descriptor.FieldPolicies.FirstOrDefault(policy =>
            policy.StorageRequired
            && policy.ValueOwner != StandardCrudFieldValueOwner.Server
            && (!values.TryGetValue(policy.Field, out var value) || IsMissing(value)));
        return invalid is null
            ? null
            : new StandardCrudError
            {
                StatusCode = 409,
                Code = "StandardCrud.StorageInvariantViolation",
                Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 的存储必填字段 {invalid.Field} 当前为空。",
                ReasonKey = "standardCrud.storageInvariantViolation"
            };
    }

    // descriptor 声明唯一性，仓储使用与旧创建路径相同的规范值检查。
    private async Task<StandardCrudError?> ValidateUniqueConstraintsAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken)
    {
        foreach (var constraint in descriptor.UniqueConstraints)
        {
            var equality = BuildUniqueEqualityValues(constraint, values);
            if (equality is null)
            {
                continue;
            }

            if (await _repository.ExistsAsync(descriptor, constraint, equality, null, cancellationToken))
            {
                return new StandardCrudError
                {
                    StatusCode = 409,
                    Code = constraint.ErrorCode,
                    Message = constraint.Message,
                    ReasonKey = constraint.ReasonKey,
                    FieldErrors = constraint.Fields.Count == 0
                        ? []
                        : [new StandardCrudFieldError(constraint.Fields[0], constraint.ErrorCode, constraint.Message)]
                };
            }
        }

        return null;
    }

    // 缺字段时遵循 constraint 的显式忽略策略；文本比较保持 trim 与大小写声明。
    private static IReadOnlyDictionary<string, object?>? BuildUniqueEqualityValues(
        StandardCrudUniqueConstraint constraint,
        IReadOnlyDictionary<string, object?> values)
    {
        var equality = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in constraint.Fields.Concat(constraint.ScopeFields))
        {
            if (!values.TryGetValue(field, out var value) || value is null)
            {
                if (constraint.IgnoreWhenAnyFieldMissing)
                {
                    return null;
                }

                equality[field] = null;
                continue;
            }

            equality[field] = value is string text
                ? constraint.CaseInsensitive
                    ? (constraint.TrimWhitespace ? text.Trim() : text).ToUpperInvariant()
                    : constraint.TrimWhitespace ? text.Trim() : text
                : value;
        }

        return equality;
    }

    private static bool IsMissing(object? value) =>
        value is null || value is string text && string.IsNullOrWhiteSpace(text);

    private static StandardCrudError FieldPolicyError(
        string field,
        string code,
        string message,
        string reasonKey) => new()
        {
            StatusCode = 400,
            Code = code,
            Message = message,
            ReasonKey = reasonKey,
            FieldErrors = [new StandardCrudFieldError(field, code, message)]
        };

    private static StandardCrudError ServerOwnedValueUnavailable(
        StandardCrudDescriptor descriptor,
        string field) => new()
        {
            StatusCode = 500,
            Code = "StandardCrud.ServerOwnedValueUnavailable",
            ReasonKey = "standardCrud.serverOwnedValueUnavailable",
            Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 的服务端必填字段 {field} 无法取得可信值。"
        };
}

internal sealed record StandardCrudCreatePreparation
{
    public IReadOnlyDictionary<string, object?>? Values { get; init; }
    public StandardCrudError? Error { get; init; }
    public bool Succeeded => Error is null;

    public static StandardCrudCreatePreparation Success(IReadOnlyDictionary<string, object?> values) =>
        new() { Values = values };

    public static StandardCrudCreatePreparation Failure(StandardCrudError error) =>
        new() { Error = error };
}
