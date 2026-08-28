namespace ICP.Platform.Crud.Descriptors;

/// <summary>
/// 标识 Standard CRUD 字段成功持久化前由调用方还是可信服务端 owner 提供值。
/// </summary>
public enum StandardCrudFieldValueOwner
{
    Caller,
    Server
}
