using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ICP.Platform.Kernel.Localization;

/// <summary>前后端共同的具名标量参数语义；缺参保留占位符，空值呈现为空文本。</summary>
public static class LocalizationTextTemplate
{
    private static readonly Regex Tokens = new(@"\{([^{}]+)\}", RegexOptions.CultureInvariant);

    /// <summary>取得模板的参数集合，用于候选阶段校验全部语言使用相同参数。</summary>
    public static IReadOnlySet<string> Parameters(string template) =>
        Tokens.Matches(template).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>按语言无关的标量表示替换参数；不递归展开参数内容。</summary>
    public static string Format(string template, IReadOnlyDictionary<string, object?>? args = null) =>
        Tokens.Replace(template, match => args is not null && args.TryGetValue(match.Groups[1].Value, out var value)
            ? Scalar(value) : match.Value);

    // JSON 标量与 CLR 标量共用规则，复杂对象不隐式转成用户文案。
    private static string Scalar(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        bool boolean => boolean ? "true" : "false",
        JsonElement element => element.ValueKind switch
        {
            JsonValueKind.Null => string.Empty,
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => element.GetRawText(),
            _ => throw new ArgumentException("LOCALIZATION_ARGUMENT_MUST_BE_SCALAR")
        },
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal =>
            Convert.ToString(value, CultureInfo.InvariantCulture)!,
        _ => throw new ArgumentException("LOCALIZATION_ARGUMENT_MUST_BE_SCALAR")
    };
}
