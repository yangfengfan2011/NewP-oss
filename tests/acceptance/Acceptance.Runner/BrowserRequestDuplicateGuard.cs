using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ICP.Platform.Acceptance;

// 浏览器请求证据只保留不可逆指纹与固定路由，不持有原始 query 或 body。
internal sealed class BrowserRequestDuplicateGuard
{
    private readonly object _sync = new();
    private readonly string _method;
    private readonly string _route;
    private readonly HashSet<string> _excludedQueryParameterNames;
    private readonly Dictionary<string, int> _physicalCounts = new(StringComparer.Ordinal);
    private DateTimeOffset? _lastMatchingRequestUtc;
    private TaskCompletionSource<bool> _matchingRequestChanged = CreateChangeSignal();

    // 显式排除集防止调用者在不知道裁决口径的情况下默认忽略请求参数。
    internal BrowserRequestDuplicateGuard(
        string method,
        string route,
        IEnumerable<string> excludedQueryParameterNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(excludedQueryParameterNames);

        _method = method.ToUpperInvariant();
        _route = route;
        _excludedQueryParameterNames = new HashSet<string>(
            excludedQueryParameterNames,
            StringComparer.Ordinal);
    }

    // 请求开始事件一旦匹配就立即计数，因此之后取消或中止不会撤回物理请求事实。
    internal bool Observe(string method, string url, string? body)
    {
        if (!string.Equals(method, _method, StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.AbsolutePath, _route, StringComparison.Ordinal))
        {
            return false;
        }

        var normalizedUrl = NormalizeUrl(uri, _excludedQueryParameterNames);
        var normalizedBody = NormalizeBody(body);
        var fingerprint = ComputeFingerprint($"{_method}\n{normalizedUrl}\n{normalizedBody}");

        TaskCompletionSource<bool> changedSignal;
        lock (_sync)
        {
            _physicalCounts[fingerprint] = _physicalCounts.GetValueOrDefault(fingerprint) + 1;
            _lastMatchingRequestUtc = DateTimeOffset.UtcNow;
            changedSignal = _matchingRequestChanged;
            _matchingRequestChanged = CreateChangeSignal();
        }
        changedSignal.TrySetResult(true);

        return true;
    }

    // 语义 ready 后至少观察一个 quiet period；新的匹配请求会重置窗口，整体等待受 timeout 限制。
    internal async Task<BrowserRequestDuplicateGuardReport> WaitForQuietPeriodAsync(
        TimeSpan quietPeriod,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (quietPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(quietPeriod));
        }
        if (timeout < quietPeriod)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var startedAt = DateTimeOffset.UtcNow;
        var deadline = startedAt + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset quietPeriodStart;
            Task requestChanged;
            lock (_sync)
            {
                quietPeriodStart = _lastMatchingRequestUtc is { } lastMatchingRequestUtc
                    && lastMatchingRequestUtc > startedAt
                    ? lastMatchingRequestUtc
                    : startedAt;
                requestChanged = _matchingRequestChanged.Task;
            }

            var now = DateTimeOffset.UtcNow;
            var quietDeadline = quietPeriodStart + quietPeriod;
            if (now >= quietDeadline)
            {
                return CreateReport();
            }
            if (now >= deadline)
            {
                return CreateReport() with
                {
                    Decision = "failed-quiet-timeout",
                    Diagnostic = "BROWSER_REQUEST_DUPLICATE_GUARD_QUIET_TIMEOUT"
                };
            }

            var delay = Task.Delay(
                quietDeadline < deadline ? quietDeadline - now : deadline - now,
                cancellationToken);
            if (await Task.WhenAny(delay, requestChanged) == delay)
            {
                await delay;
            }
        }
    }

    // 快照只公开聚合后的脱敏字段，并对零匹配与重复请求关闭失败。
    internal BrowserRequestDuplicateGuardReport CreateReport()
    {
        BrowserRequestFingerprintEvidence[] fingerprints;
        lock (_sync)
        {
            fingerprints = _physicalCounts
                .OrderBy(static item => item.Key, StringComparer.Ordinal)
                .Select(item => new BrowserRequestFingerprintEvidence(
                    _method,
                    _route,
                    item.Key,
                    item.Value,
                    Math.Max(0, item.Value - 1),
                    item.Value > 1 ? "duplicate" : "unique"))
                .ToArray();
        }

        var (decision, diagnostic) = fingerprints.Length == 0
            ? ("failed-zero-match", "BROWSER_REQUEST_DUPLICATE_GUARD_ZERO_MATCH")
            : fingerprints.Any(static item => item.DuplicateCount > 0)
                ? ("failed-duplicate", "BROWSER_REQUEST_DUPLICATE_GUARD_DUPLICATE")
                : ("passed", "BROWSER_REQUEST_DUPLICATE_GUARD_PASSED");
        return new BrowserRequestDuplicateGuardReport(decision, diagnostic, fingerprints);
    }

    // URL 规范化包含 origin、固定路径与按解码后 name/value 排序的全部未排除参数。
    private static string NormalizeUrl(Uri uri, IReadOnlySet<string> excludedQueryParameterNames)
    {
        var queryParts = ParseQuery(uri.Query)
            .Where(part => !excludedQueryParameterNames.Contains(part.Name))
            .OrderBy(static part => part.Name, StringComparer.Ordinal)
            .ThenBy(static part => part.Value, StringComparer.Ordinal)
            .ToArray();
        var authority = uri.IsDefaultPort
            ? $"{uri.Scheme.ToLowerInvariant()}://{uri.IdnHost.ToLowerInvariant()}"
            : $"{uri.Scheme.ToLowerInvariant()}://{uri.IdnHost.ToLowerInvariant()}:{uri.Port}";
        var normalizedQuery = string.Concat(queryParts.Select(static part =>
            $"{part.Name.Length}:{part.Name}{part.Value.Length}:{part.Value}"));
        return $"{authority}{uri.AbsolutePath}?{normalizedQuery}";
    }

    // Query 解析保留重复参数，并在排序前按浏览器表单语义解码加号和百分号编码。
    private static IReadOnlyList<QueryPart> ParseQuery(string query)
    {
        if (string.IsNullOrEmpty(query) || query == "?")
        {
            return [];
        }

        return query[1..]
            .Split('&', StringSplitOptions.None)
            .Select(static segment =>
            {
                var separator = segment.IndexOf('=');
                var name = separator < 0 ? segment : segment[..separator];
                var value = separator < 0 ? string.Empty : segment[(separator + 1)..];
                return new QueryPart(DecodeQueryPart(name), DecodeQueryPart(value));
            })
            .ToArray();
    }

    // Query 解码显式处理 application/x-www-form-urlencoded 的加号空格语义。
    private static string DecodeQueryPart(string value) =>
        Uri.UnescapeDataString(value.Replace('+', ' '));

    // Body 仅在成功解析为 JSON 时规范化结构；其他内容按原值参与哈希但不会进入证据。
    private static string NormalizeBody(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return "body:empty";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteCanonicalJson(writer, document.RootElement);
            }
            return "body:json:" + Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return "body:raw:" + body;
        }
    }

    // JSON 对象递归按 key 排序，数组保留原顺序，标量保留其 JSON 值语义。
    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(
                             static property => property.Name,
                             StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalJson(writer, item);
                }
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    // SHA-256 让证据能够比较相同物理请求而不能恢复请求内容。
    private static string ComputeFingerprint(string value) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    // 每轮等待使用独立 signal，避免一个历史请求让后续 quiet-period 检查持续忙循环。
    private static TaskCompletionSource<bool> CreateChangeSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed record QueryPart(string Name, string Value);
}

// 单个请求指纹分组只暴露稳定计数和分组裁决。
internal sealed record BrowserRequestFingerprintEvidence(
    string Method,
    string Route,
    string Fingerprint,
    int PhysicalCount,
    int DuplicateCount,
    string Decision);

// Guard 报告是 runner 写盘与测试共同观察的唯一裁决 Interface。
internal sealed record BrowserRequestDuplicateGuardReport(
    string Decision,
    string Diagnostic,
    IReadOnlyList<BrowserRequestFingerprintEvidence> Fingerprints)
{
    // Runner 必须先写盘本报告，再调用此入口把 fail-closed 裁决提升为场景失败。
    internal void EnsurePassed()
    {
        if (!string.Equals(Decision, "passed", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(Diagnostic);
        }
    }
}
