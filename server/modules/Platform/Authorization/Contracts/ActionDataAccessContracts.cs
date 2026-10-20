namespace ICP.Platform.Authorization.Contracts;

/// <summary>声明上下文无效与对象拒绝分开；依赖故障不转换为找不到。</summary>
public sealed class ActionDataAccessException(int status, string code) : InvalidOperationException(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
