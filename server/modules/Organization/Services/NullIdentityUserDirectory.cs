namespace ICP.Platform.Modules.Organization.Services;

public sealed class NullIdentityUserDirectory : IIdentityUserDirectory
{
    public Task EnsureUserExistsAsync(string userId, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Identity Lite 用户目录尚未接入，暂不支持绑定账号。");
}
