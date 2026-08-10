namespace ICP.Platform.Modules.Authentication.Application;

public sealed class AuthenticationSessionOptions
{
    public const string SectionName = "Authentication:Sessions";

    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(8);

    public int PurgeBatchSize { get; set; } = 500;
}
