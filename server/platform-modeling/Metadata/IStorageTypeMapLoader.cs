namespace ICP.Platform.Modeling.Metadata;

/// <summary>
/// 定义存储类型映射文档的加载契约。
/// </summary>
public interface IStorageTypeMapLoader
{
    /// <summary>
    /// 从指定路径加载存储类型映射文档。
    /// </summary>
    StorageTypeMapDocument Load(string path);
}
