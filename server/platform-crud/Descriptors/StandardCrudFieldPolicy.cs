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

    /// <summary>
    /// 获取字段是否要求调用方提交已去除首尾空白的规范短文本。
    /// </summary>
    public bool Trim { get; init; }

    /// <summary>
    /// 获取字段在进入持久化前允许的最大字符数；当前短文本固定投影为 256。
    /// </summary>
    public int? MaxLength { get; init; }

    /// <summary>
    /// 获取可空短文本是否只接受 null 表达空值。
    /// </summary>
    public bool EmptyAsNull { get; init; }
}
