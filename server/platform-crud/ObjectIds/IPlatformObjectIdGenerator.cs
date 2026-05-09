namespace ICP.Platform.Crud.ObjectIds;

public interface IPlatformObjectIdGenerator
{
    /// <summary>
    /// 为平台对象生成 EID 值；objectName 必须是稳定模型名，返回格式固定为 {ModelName}_{Guid:N}。
    /// </summary>
    string NewId(string objectName);
}
