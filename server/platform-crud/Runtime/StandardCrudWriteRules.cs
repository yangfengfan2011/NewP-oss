using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modeling;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Presentation;

namespace ICP.Platform.Crud.Runtime;

// 默认写入的字段规则只在平台内部使用；创建和更新分别编排各自的输入与完整候选。
internal sealed class StandardCrudWriteRules
{
    private readonly IStandardCrudRepository _repository;
    private readonly IReadOnlyDictionary<string, IStandardCrudValidator> _validators;
    private readonly ITemporalInputNormalizer? _temporalInputNormalizer;
    private readonly IEntityAuditValueProvider? _auditValueProvider;

    // 共用 descriptor、仓储和可信审计来源，不持有单次写入状态。
    internal StandardCrudWriteRules(
        IStandardCrudRepository repository,
        IEnumerable<IStandardCrudValidator> validators,
        ITemporalInputNormalizer? temporalInputNormalizer,
        IEntityAuditValueProvider? auditValueProvider)
    {
        _repository = repository;
        _validators = validators.ToDictionary(static item => item.ResourceKey, StringComparer.Ordinal);
        _temporalInputNormalizer = temporalInputNormalizer;
        _auditValueProvider = auditValueProvider;
    }

    // 对同一份输入按所有权、规范化、必填、审计、唯一性和对象规则的固定顺序收敛。
    internal async Task<StandardCrudWritePreparation> PrepareCreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
        var error = FindReadOnlyOwnershipFieldError(descriptor, values, StandardCrudFieldValueOwner.ServerOnCreate)
            ?? FindReadOnlyDisplayFieldError(descriptor, values);
        if (error is not null)
        {
            return StandardCrudWritePreparation.Failure(error);
        }

        var allowed = FilterFields(values, descriptor.FieldSet.CreateFields);

        error = NormalizeTemporalInputs(descriptor, allowed)
            ?? ValidateCreateRequiredFields(descriptor, allowed);
        if (error is not null)
        {
            return StandardCrudWritePreparation.Failure(error);
        }

        ApplyCreateDefaults(descriptor, allowed);
        error = ValidateFieldPolicies(descriptor, allowed);
        if (error is not null)
        {
            return StandardCrudWritePreparation.Failure(error);
        }

        error = ApplyCreateAuditFields(descriptor, allowed)
            ?? ValidateStorageInvariants(descriptor, allowed)
            ?? await ValidateUniqueConstraintsAsync(descriptor, allowed, null, cancellationToken);
        if (error is not null)
        {
            return StandardCrudWritePreparation.Failure(error);
        }

        if (_validators.TryGetValue(descriptor.ResourceKey, out var validator))
        {
            error = await validator.ValidateCreateAsync(descriptor, allowed, cancellationToken);
            if (error is not null)
            {
                return StandardCrudWritePreparation.Failure(error);
            }
        }

        return StandardCrudWritePreparation.Success(allowed);
    }

    // 更新校验只规范本次输入；存储与唯一性必须读取合并旧值后的完整候选。
    internal async Task<StandardCrudWritePreparation> PrepareUpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> existing,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken)
    {
        var ownershipFieldError = FindReadOnlyOwnershipFieldError(descriptor, values, StandardCrudFieldValueOwner.CallerOnCreate);
        if (ownershipFieldError is not null)
        {
            return StandardCrudWritePreparation.Failure(ownershipFieldError);
        }

        var readOnlyDisplayFieldError = FindReadOnlyDisplayFieldError(descriptor, values);
        if (readOnlyDisplayFieldError is not null)
        {
            return StandardCrudWritePreparation.Failure(readOnlyDisplayFieldError);
        }

        var allowed = FilterFields(values, descriptor.FieldSet.UpdateFields);
        var temporalError = NormalizeTemporalInputs(descriptor, allowed);
        if (temporalError is not null)
        {
            return StandardCrudWritePreparation.Failure(temporalError);
        }
        var requiredError = ValidateUpdateRequiredFields(descriptor, allowed);
        if (requiredError is not null)
        {
            return StandardCrudWritePreparation.Failure(requiredError);
        }
        var fieldPolicyError = ValidateFieldPolicies(descriptor, allowed);
        if (fieldPolicyError is not null)
        {
            return StandardCrudWritePreparation.Failure(fieldPolicyError);
        }

        var serverOwnedValueError = TryResolveRequiredAuditValues(descriptor, existing, out var auditValues);
        if (serverOwnedValueError is not null)
        {
            return StandardCrudWritePreparation.Failure(serverOwnedValueError);
        }

        ApplyUpdateAuditFields(descriptor, allowed, auditValues);
        var candidate = new Dictionary<string, object?>(existing, StringComparer.Ordinal);
        foreach (var pair in allowed)
        {
            candidate[pair.Key] = pair.Value;
        }

        var storageInvariantError = ValidateStorageInvariants(descriptor, candidate);
        if (storageInvariantError is not null)
        {
            return StandardCrudWritePreparation.Failure(storageInvariantError);
        }

        // 数据库可能按 provider 规则以别名命中主键；唯一性排除必须使用受保护旧值中的实际持久化标识。
        var persistedId = Convert.ToString(existing.GetValueOrDefault(descriptor.IdField)) ?? id;
        var uniqueError = await ValidateUniqueConstraintsAsync(
            descriptor,
            candidate,
            excludedId: persistedId,
            cancellationToken);
        if (uniqueError is not null)
        {
            return StandardCrudWritePreparation.Failure(uniqueError);
        }

        if (_validators.TryGetValue(descriptor.ResourceKey, out var validator))
        {
            var error = await validator.ValidateUpdateAsync(descriptor, id, allowed, cancellationToken);
            if (error is not null)
            {
                return StandardCrudWritePreparation.Failure(error);
            }
        }

        return StandardCrudWritePreparation.Success(allowed);
    }

    // 只保留动作声明允许写入的字段；所有权保护在过滤前执行。
    private static Dictionary<string, object?> FilterFields(
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyList<string> allowedFields)
    {
        var filtered = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in allowedFields)
        {
            if (values.TryGetValue(field, out var value))
            {
                filtered[field] = value;
            }
        }

        return filtered;
    }

    // 短文本存储事实在公共写入边界失败，避免各模块等待数据库截断异常或复制长度协议。
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
                return FieldPolicyError(
                    policy.Field,
                    "StandardCrud.ShortTextNotNormalized",
                    $"字段 {policy.Field} 必须使用去除首尾空白后的规范短文本，空值请提交 null。",
                    "standardCrud.shortTextNotNormalized",
                    new Dictionary<string, object?>(StringComparer.Ordinal));
            }

            if (policy.MaxLength is { } maxLength && text.Length > maxLength)
            {
                return FieldPolicyError(
                    policy.Field,
                    "StandardCrud.ShortTextTooLong",
                    $"字段 {policy.Field} 不能超过 {maxLength} 个字符。",
                    "standardCrud.shortTextTooLong",
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["maxLength"] = maxLength });
            }
        }

        return null;
    }

    // 字段策略错误统一带稳定顶层码与单字段错误，供标准表单精确定位。
    private static StandardCrudError FieldPolicyError(
        string field,
        string code,
        string message,
        string reasonKey,
        IReadOnlyDictionary<string, object?> args) => new()
        {
            StatusCode = 400,
            Code = code,
            Message = message,
            ReasonKey = reasonKey,
            Args = args,
            FieldErrors = [new StandardCrudFieldError(field, code, message) { ReasonKey = reasonKey, Args = args }]
        };

    // Modeling 标记的固定审计字段与规范字段策略共同保持服务端所有权，在过滤未知字段前拒绝越权输入。
    private static StandardCrudError? FindReadOnlyOwnershipFieldError(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        StandardCrudFieldValueOwner actionProtectedOwner)
    {
        var auditField = descriptor.DisplayProjectionPlan?.Fields.FirstOrDefault(plan => plan.IsEntityAuditField && values.ContainsKey(plan.SourceField));
        if (auditField is not null)
        {
            return CreateReadOnlyServerOwnedFieldError(auditField.SourceField);
        }
        var submitted = descriptor.FieldPolicies.FirstOrDefault(policy =>
            (policy.ValueOwner == StandardCrudFieldValueOwner.Server
                || policy.ValueOwner == actionProtectedOwner)
            && values.ContainsKey(policy.Field));
        return submitted is null
            ? null
            : submitted.ValueOwner == StandardCrudFieldValueOwner.CallerOnCreate
                ? CreateImmutableFieldError(submitted.Field)
                : CreateReadOnlyServerOwnedFieldError(submitted.Field);
    }

    // 服务端拥有字段使用字段级 400，调用方可直接移除伪造值且不会把平台事实误判为普通未知字段。
    private static StandardCrudError CreateReadOnlyServerOwnedFieldError(string field) => new()
    {
        StatusCode = 400,
        Code = "StandardCrud.ServerOwnedFieldReadOnly",
        ReasonKey = "standardCrud.serverOwnedFieldReadOnly",
        Message = $"标准 CRUD 服务端字段 {field} 不能由客户端提交。",
        FieldErrors =
        [
            new StandardCrudFieldError(
                field,
                "StandardCrud.ServerOwnedFieldReadOnly",
                $"标准 CRUD 服务端字段 {field} 不能由客户端提交。")
        ]
    };

    // 创建后不可变字段使用独立稳定码，避免把调用方创建事实误报成服务端生成事实。
    private static StandardCrudError CreateImmutableFieldError(string field) => new()
    {
        StatusCode = 400,
        Code = "StandardCrud.ImmutableFieldReadOnly",
        ReasonKey = "standardCrud.immutableFieldReadOnly",
        Message = $"标准 CRUD 字段 {field} 只能在创建时提交。",
        FieldErrors =
        [
            new StandardCrudFieldError(
                field,
                "StandardCrud.ImmutableFieldReadOnly",
                $"标准 CRUD 字段 {field} 只能在创建时提交。")
        ]
    };

    // 服务端必填审计字段不能降级为客户端输入；owner 未装配或返回无效值时在仓储调用前独立失败。
    private StandardCrudError? TryResolveRequiredAuditValues(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> existing,
        out EntityAuditValues? values)
    {
        var requiredAuditField = descriptor.FieldPolicies.FirstOrDefault(policy =>
            policy.StorageRequired
            && policy.ValueOwner == StandardCrudFieldValueOwner.Server
            && IsEntityAuditField(policy.Field));
        if (requiredAuditField is null)
        {
            values = null;
            return null;
        }

        try
        {
            values = _auditValueProvider?.ForUpdate(
                Convert.ToString(existing.GetValueOrDefault("CreatedBy"))!,
                existing.GetValueOrDefault("CreatedAt") is DateTime createdAt
                    ? createdAt
                    : throw new EntityAuditStampUnavailableException());
        }
        catch (Exception exception) when (exception is ArgumentException or EntityAuditStampUnavailableException)
        {
            values = null;
        }

        return values is null
            ? ServerOwnedValueUnavailable(descriptor, requiredAuditField.Field)
            : null;
    }

    // ADR-0026 固定 Entity 审计四字段；主键等其他服务端字段继续由其既有 owner 处理。
    private static bool IsEntityAuditField(string field) =>
        field is "CreatedBy" or "CreatedAt" or "UpdatedBy" or "UpdatedAt";

    // owner 缺失属于平台 500，不生成字段级调用方错误，避免把平台配置缺陷归因于请求。
    private static StandardCrudError ServerOwnedValueUnavailable(
        StandardCrudDescriptor descriptor,
        string field) => new()
        {
            StatusCode = 500,
            Code = "StandardCrud.ServerOwnedValueUnavailable",
            ReasonKey = "standardCrud.serverOwnedValueUnavailable",
            Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 的服务端必填字段 {field} 无法取得可信值。"
        };

    // 只有 canonical raw/display companion 属于 output-only；legacy 别名与未知字段继续沿既有边界处理。
    private static StandardCrudError? FindReadOnlyDisplayFieldError(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values)
    {
        foreach (var displayField in StandardCrudQueryDescriptorAdapter.GetQueryDisplayValueFields(descriptor))
        {
            if (!IsCanonicalDisplayCompanion(displayField)
                || !values.ContainsKey(displayField.Field))
            {
                continue;
            }

            return CreateReadOnlyDisplayFieldError(displayField.Field);
        }

        return null;
    }

    // canonical companion 以 source + "_DisplayValue" 为唯一稳定命名；其余 display 声明不参与本次写拒绝。
    private static bool IsCanonicalDisplayCompanion(ICP.Platform.Query.Descriptors.PlatformDisplayValueField displayField) =>
        string.Equals(displayField.Field, string.Concat(displayField.SourceField, "_DisplayValue"), StringComparison.Ordinal);

    // display companion 只能由服务端在读取和写后详情阶段补齐，客户端提交视为稳定 400。
    private static StandardCrudError CreateReadOnlyDisplayFieldError(string field) => new()
    {
        StatusCode = 400,
        Code = "StandardCrud.DisplayValueFieldReadOnly",
        ReasonKey = "standardCrud.displayValueFieldReadOnly",
        Message = $"标准 CRUD 显示字段 {field} 只允许输出，不能提交写入。",
        FieldErrors =
        [
            new StandardCrudFieldError(
                field,
                "StandardCrud.DisplayValueFieldReadOnly",
                $"标准 CRUD 显示字段 {field} 只允许输出，不能提交写入。")
        ]
    };

    // temporal 语义由字段策略触发；规范化完成后 validator 与仓储只接收 DateOnly 或 UTC DateTime。
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

    // 创建检查调用方应提供的字段，仍在默认值补齐前执行。
    private static StandardCrudError? ValidateCreateRequiredFields(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values)
    {
        var missing = descriptor.RequiredFields.FirstOrDefault(field =>
            field.Scope is StandardCrudRequiredFieldScope.CreateOnly or StandardCrudRequiredFieldScope.CreateAndUpdate
            && (!values.TryGetValue(field.Field, out var value) || IsMissingRequiredValue(value)));
        return missing is null ? null : RequiredFieldError(missing);
    }

    // 部分更新只拒绝显式清空，不要求重交未修改的必填字段。
    private static StandardCrudError? ValidateUpdateRequiredFields(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values)
    {
        var missing = descriptor.RequiredFields.FirstOrDefault(field =>
            field.Scope is StandardCrudRequiredFieldScope.UpdateOnly or StandardCrudRequiredFieldScope.CreateAndUpdate
            && values.TryGetValue(field.Field, out var value) && IsMissingRequiredValue(value));
        return missing is null ? null : RequiredFieldError(missing);
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
            if (_auditValueProvider is null)
            {
                return required is null ? null : ServerOwnedValueUnavailable(descriptor, required.Field);
            }

            _auditValueProvider.ForCreate().ApplyTo(values, fields);
            return null;
        }
        catch (EntityAuditStampUnavailableException)
        {
            return required is null ? null : ServerOwnedValueUnavailable(descriptor, required.Field);
        }
    }

    // null 与空白文本都不能满足必填声明。
    private static bool IsMissingRequiredValue(object? value)
    {
        return value is null || value is string text && string.IsNullOrWhiteSpace(text);
    }

    // 必填声明拥有稳定错误信息，字段定位和顶层错误保持一致。
    private static StandardCrudError RequiredFieldError(StandardCrudRequiredField requiredField) => new()
    {
        StatusCode = 400,
        Code = requiredField.ErrorCode,
        Message = requiredField.Message,
        ReasonKey = requiredField.ReasonKey,
        FieldErrors = [new StandardCrudFieldError(requiredField.Field, requiredField.ErrorCode, requiredField.Message)]
    };

    // 合并后的完整候选必须满足由调用方拥有的存储非空不变量；服务端拥有值由其可信持久化 owner 在写入边界补齐。
    private static StandardCrudError? ValidateStorageInvariants(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> candidate)
    {
        var invalid = descriptor.FieldPolicies.FirstOrDefault(policy =>
            policy.StorageRequired
            && policy.ValueOwner != StandardCrudFieldValueOwner.Server
            && (!candidate.TryGetValue(policy.Field, out var value) || IsMissingRequiredValue(value)));
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

    // 唯一约束属于标准写入前置校验；descriptor 只声明约束，执行顺序由内核统一控制。
    private async Task<StandardCrudError?> ValidateUniqueConstraintsAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        string? excludedId,
        CancellationToken cancellationToken)
    {
        foreach (var constraint in descriptor.UniqueConstraints)
        {
            var equalityValues = BuildUniqueEqualityValues(constraint, values);
            if (equalityValues is null)
            {
                continue;
            }

            if (await _repository.ExistsAsync(descriptor, constraint, equalityValues, excludedId, cancellationToken))
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

    // 唯一键缺字段时遵循显式忽略策略，范围字段也参与相同规范化。
    private static IReadOnlyDictionary<string, object?>? BuildUniqueEqualityValues(
        StandardCrudUniqueConstraint constraint,
        IReadOnlyDictionary<string, object?> values)
    {
        var equalityValues = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in constraint.Fields.Concat(constraint.ScopeFields))
        {
            if (!values.TryGetValue(field, out var value) || value is null)
            {
                if (constraint.IgnoreWhenAnyFieldMissing)
                {
                    return null;
                }

                equalityValues[field] = null;
                continue;
            }

            equalityValues[field] = NormalizeUniqueValue(value, constraint);
        }

        return equalityValues;
    }

    // 唯一性比较只采用约束声明的文本 trim 与大小写规则。
    private static object? NormalizeUniqueValue(object? value, StandardCrudUniqueConstraint constraint)
    {
        if (value is not string text)
        {
            return value;
        }

        var normalized = constraint.TrimWhitespace ? text.Trim() : text;
        return constraint.CaseInsensitive ? normalized.ToUpperInvariant() : normalized;
    }

    // 规范策略存在时只刷新服务端拥有的更新审计字段；未采用策略的资源保留既有 UpdatedAt 行为。
    private static void ApplyUpdateAuditFields(
        StandardCrudDescriptor descriptor,
        IDictionary<string, object?> values,
        EntityAuditValues? auditValues)
    {
        var serverOwnedUpdateAuditPolicies = descriptor.FieldPolicies
            .Where(policy =>
                policy.ValueOwner == StandardCrudFieldValueOwner.Server
                && policy.Field is "UpdatedBy" or "UpdatedAt")
            .ToArray();
        if (serverOwnedUpdateAuditPolicies.Length > 0)
        {
            var trustedValues = auditValues ?? throw new InvalidOperationException("Required Entity audit values were not resolved.");
            trustedValues.ApplyTo(values, serverOwnedUpdateAuditPolicies.Select(static policy => policy.Field));

            return;
        }

        if (descriptor.FieldSet.UpdateFields.Contains("UpdatedAt", StringComparer.Ordinal))
        {
            values["UpdatedAt"] = DateTime.UtcNow;
        }
    }

}

internal sealed record StandardCrudWritePreparation
{
    public IReadOnlyDictionary<string, object?>? Values { get; init; }
    public StandardCrudError? Error { get; init; }
    public bool Succeeded => Error is null;

    // 成功只返回规范写入字段，展示值由外层读取适配负责。
    public static StandardCrudWritePreparation Success(IReadOnlyDictionary<string, object?> values) =>
        new() { Values = values };

    // 失败保留既有错误形状，不触发持久化。
    public static StandardCrudWritePreparation Failure(StandardCrudError error) =>
        new() { Error = error };
}
