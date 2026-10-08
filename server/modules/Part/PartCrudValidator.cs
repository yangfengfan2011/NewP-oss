using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Part;

/// <summary>长文本存储保留说明，业务上仅接受最多 500 字。</summary>
public sealed class PartCrudValidator(EfPlatformDbContext db) : IStandardCrudValidator
{
    public string ResourceKey => "part.part";

    /// <inheritdoc/>
    public async Task<StandardCrudError?> ValidateCreateAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
    {
        if (values.GetValueOrDefault("Description") is string { Length: > 500 })
            return FieldError("Description", "Part.DescriptionTooLong", "说明最多 500 字。");

        // 输入已由标准字段策略规范化；数据库比较与唯一索引采用同一列规则。
        // 预检只改善字段反馈，真正并发冲突仍由 UX_Part_ECODE 与标准 409 翻译保护。
        var code = (string)values["ECODE"]!;
        return await db.Set<Dictionary<string, object?>>("Part/Part").AsNoTracking()
            .AnyAsync(row => EF.Property<string>(row, "ECODE") == code, cancellationToken)
                ? FieldError("ECODE", "Part.CodeDuplicated", "零组件编码必须唯一。", 409) : null;
    }

    /// <inheritdoc/>
    public Task<StandardCrudError?> ValidateUpdateAsync(StandardCrudDescriptor descriptor, string id, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
        => ValidateCreateAsync(descriptor, values, cancellationToken);

    // 业务校验使用标准字段错误协议，Drawer 可直接定位失败字段。
    private static StandardCrudError FieldError(string field, string code, string message, int status = 400) => new()
    {
        StatusCode = status, Code = code, Message = message, ReasonKey = code,
        FieldErrors = [new(field, code, message)]
    };
}
