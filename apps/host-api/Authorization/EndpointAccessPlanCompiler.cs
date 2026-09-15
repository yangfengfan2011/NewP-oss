using ICP.Platform.Authorization.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace ICP.Platform.HostApi.Authorization;

public static class EndpointAccessFailureCodes
{
    public const string RuleMissing = "AUTH_ENDPOINT_RULE_MISSING";
    public const string RuleConflict = "AUTH_ENDPOINT_RULE_CONFLICT";
}

public sealed record EndpointAccessDiagnostic(
    string Code,
    string Endpoint,
    string Message);

public sealed class EndpointAccessPlanValidationException : InvalidOperationException
{
    public EndpointAccessPlanValidationException(
        IReadOnlyList<EndpointAccessDiagnostic> diagnostics)
        : base(BuildMessage(diagnostics))
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (diagnostics.Count == 0)
        {
            throw new ArgumentException("Endpoint access diagnostics cannot be empty.", nameof(diagnostics));
        }

        Diagnostics = diagnostics.ToArray();
    }

    public IReadOnlyList<EndpointAccessDiagnostic> Diagnostics { get; }

    private static string BuildMessage(IReadOnlyList<EndpointAccessDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return string.Join(
            Environment.NewLine,
            diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Endpoint}: {diagnostic.Message}"));
    }
}

/// <summary>
/// 在 Host 监听前保证每个路由只有一个终态 access rule。
/// </summary>
public static class EndpointAccessPlanCompiler
{
    /// <summary>校验已装配端点的终态规则，聚合诊断后在 Host 开放前拒绝不完整计划。</summary>
    public static void Compile(
        IEnumerable<EndpointDataSource> dataSources)
    {
        ArgumentNullException.ThrowIfNull(dataSources);

        var diagnostics = new List<EndpointAccessDiagnostic>();
        foreach (var endpoint in dataSources
                     .SelectMany(static source => source.Endpoints)
                     .OrderBy(GetEndpointName, StringComparer.Ordinal))
        {
            ValidateEndpoint(endpoint, diagnostics);
        }

        if (diagnostics.Count > 0)
        {
            throw new EndpointAccessPlanValidationException(
                diagnostics
                    .OrderBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
                    .ThenBy(static diagnostic => diagnostic.Endpoint, StringComparer.Ordinal)
                    .ThenBy(static diagnostic => diagnostic.Message, StringComparer.Ordinal)
                    .ToArray());
        }
    }

    // 功能位置是业务终态；既有 Host policy 作为附加约束共同满足，不替代位置判权。
    private static void ValidateEndpoint(
        Endpoint endpoint,
        ICollection<EndpointAccessDiagnostic> diagnostics)
    {
        var endpointName = GetEndpointName(endpoint);
        var allowAnonymous = endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>();
        var authenticatedOnly = endpoint.Metadata.GetOrderedMetadata<AuthenticatedOnlyAttribute>();
        var denyAll = endpoint.Metadata.GetOrderedMetadata<DenyAllAttribute>();
        var featureRules = endpoint.Metadata.GetOrderedMetadata<RequireFeatureAttribute>();
        var authorizeData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        var otherAuthorization = authorizeData
            .Where(static data => data is not AuthenticatedOnlyAttribute
                                  && data is not DenyAllAttribute
                                  && data is not RequireFeatureAttribute)
            .ToArray();
        var hostPolicies = otherAuthorization
            .Where(static data => !string.IsNullOrWhiteSpace(data.Policy))
            .ToArray();
        var bareAuthorization = otherAuthorization
            .Where(static data => string.IsNullOrWhiteSpace(data.Policy))
            .ToArray();

        var terminalRuleCount = allowAnonymous.Count
                                + authenticatedOnly.Count
                                + denyAll.Count
                                + featureRules.Count
                                + (featureRules.Count == 0 ? hostPolicies.Length : 0);
        var conflict = terminalRuleCount > 1
                       || (allowAnonymous.Count > 0 && authorizeData.Count > 0)
                       || (bareAuthorization.Length > 0 && terminalRuleCount > 0);
        if (conflict)
        {
            diagnostics.Add(new EndpointAccessDiagnostic(
                EndpointAccessFailureCodes.RuleConflict,
                endpointName,
                "Endpoint must declare exactly one terminal access rule."));
        }
        else if (terminalRuleCount == 0 || bareAuthorization.Length > 0)
        {
            diagnostics.Add(new EndpointAccessDiagnostic(
                EndpointAccessFailureCodes.RuleMissing,
                endpointName,
                "Endpoint has no terminal access rule; bare Authorize is not sufficient."));
        }
    }

    private static string GetEndpointName(Endpoint endpoint)
    {
        if (endpoint is RouteEndpoint routeEndpoint)
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            return $"{string.Join(',', methods)} /{routeEndpoint.RoutePattern.RawText?.TrimStart('/')}";
        }

        return endpoint.DisplayName ?? "(unknown endpoint)";
    }
}
