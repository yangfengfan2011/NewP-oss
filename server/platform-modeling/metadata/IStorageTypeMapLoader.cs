namespace ICP.Platform.Modeling.Metadata;

public interface IStorageTypeMapLoader
{
    // 加载平台级 storage.type 真源，供校验与投影复用。
    StorageTypeMapDocument LoadFromFile(string path);
}
