namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>功能选择校验的稳定失败事实；模块负责映射用户错误，诊断继续由 InvalidOperationException 承载。</summary>
public sealed class FeatureSelectionException : InvalidOperationException
{
    public string Code { get; }
    public IReadOnlyDictionary<string, object?> Args { get; }

    // 只由目录校验创建，参数来自原位置和依赖事实，不解析诊断文本。
    internal FeatureSelectionException(string code, string diagnostic, string? position, string? dependencyPosition = null)
        : base(diagnostic)
    {
        Code = code;
        var args = new Dictionary<string, object?>(StringComparer.Ordinal) { ["position"] = position };
        if (dependencyPosition is not null) args["dependencyPosition"] = dependencyPosition;
        Args = args;
    }
}
