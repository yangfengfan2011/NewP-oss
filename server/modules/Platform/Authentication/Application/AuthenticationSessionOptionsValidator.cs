using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace ICP.Platform.Modules.Authentication.Application;

internal sealed class AuthenticationSessionOptionsValidator : IValidateOptions<AuthenticationSessionOptions>
{
    private const string LifetimeInvalid = "AUTH_SESSION_LIFETIME_INVALID";
    private const string PurgeBatchSizeInvalid = "AUTH_SESSION_PURGE_BATCH_SIZE_INVALID";
    private const string DefaultTimeZoneMissing = "AUTH_SESSION_DEFAULT_TIME_ZONE_MISSING";
    private const string DefaultTimeZoneBlank = "AUTH_SESSION_DEFAULT_TIME_ZONE_BLANK";
    private const string DefaultTimeZoneInvalid = "AUTH_SESSION_DEFAULT_TIME_ZONE_INVALID";
    private const string DefaultTimeZoneBindingMismatch = "AUTH_SESSION_DEFAULT_TIME_ZONE_BINDING_MISMATCH";
    private readonly IConfiguration _configuration;

    // 保留原始配置存在性，使属性默认值不能掩盖缺失的部署配置。
    public AuthenticationSessionOptionsValidator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // 启动前同时验证会话成本边界和部署默认 IANA 时区。
    public ValidateOptionsResult Validate(string? name, AuthenticationSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configuredTimeZoneId = _configuration[$"{AuthenticationSessionOptions.SectionName}:DefaultTimeZoneId"];
        if (options.Lifetime <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(LifetimeInvalid);
        }
        if (options.PurgeBatchSize is < 1 or > 1000)
        {
            return ValidateOptionsResult.Fail(PurgeBatchSizeInvalid);
        }
        if (configuredTimeZoneId is null)
        {
            return ValidateOptionsResult.Fail(DefaultTimeZoneMissing);
        }
        if (string.IsNullOrWhiteSpace(configuredTimeZoneId))
        {
            return ValidateOptionsResult.Fail(DefaultTimeZoneBlank);
        }
        if (!AuthenticationSessionTimeZone.TryNormalize(configuredTimeZoneId, out var configuredNormalized)
            || !AuthenticationSessionTimeZone.TryNormalize(options.DefaultTimeZoneId, out var boundNormalized))
        {
            return ValidateOptionsResult.Fail(DefaultTimeZoneInvalid);
        }
        if (!string.Equals(configuredNormalized, boundNormalized, StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(DefaultTimeZoneBindingMismatch);
        }

        return ValidateOptionsResult.Success;
    }
}
