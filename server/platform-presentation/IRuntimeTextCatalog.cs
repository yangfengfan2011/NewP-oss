namespace ICP.Platform.Presentation;

/// <summary>从 Host 当前已激活的词典快照取得译文；模块只依赖只读抽象。</summary>
public interface IRuntimeTextCatalog
{
    /// <summary>按规范请求 locale 解析完整词条并替换具名标量参数。</summary>
    /// <param name="key">完整 dicKey.localKey，不接受文件路径。</param>
    /// <param name="locale">已校验的请求 locale；省略时使用平台默认语言。</param>
    /// <param name="args">参数为空值时呈现空文本，缺参保留占位符。</param>
    /// <returns>活动快照中的译文；缺词条或无效 locale 稳定失败。</returns>
    string GetText(string key, string? locale = null, IReadOnlyDictionary<string, object?>? args = null);
}
