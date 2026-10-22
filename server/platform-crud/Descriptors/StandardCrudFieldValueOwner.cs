namespace ICP.Platform.Crud.Descriptors;

/// <summary>
/// 标识 Standard CRUD 字段成功持久化前由调用方还是可信服务端 owner 提供值。
/// </summary>
public enum StandardCrudFieldValueOwner
{
    /// <summary>调用方在创建和更新时都可以提供该字段。</summary>
    Caller,

    /// <summary>服务端在创建和更新时都拥有该字段，调用方提交会被拒绝。</summary>
    Server,

    /// <summary>服务端拥有创建值，普通更新阶段改由调用方维护。</summary>
    ServerOnCreate,

    /// <summary>调用方只能在创建时提供该字段，普通更新提交会被拒绝。</summary>
    CallerOnCreate
}
