namespace ICP.Platform.Crud.Descriptors;

/// <summary>
/// 表达 Standard CRUD 字段彼此独立的存储、输入、所有权和创建初始值语义。
/// </summary>
public sealed class StandardCrudFieldPolicy
{
    public required string Field { get; init; }

    public bool StorageRequired { get; init; }

    public bool CreateInputRequired { get; init; }

    public bool UpdateNonEmpty { get; init; }

    public StandardCrudFieldValueOwner ValueOwner { get; init; }

    public object? CreateDefaultValue { get; init; }
}
