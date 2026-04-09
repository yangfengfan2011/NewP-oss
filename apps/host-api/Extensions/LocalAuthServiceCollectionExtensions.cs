using ICP.Platform.HostApi.Authentication;

namespace ICP.Platform.HostApi.Extensions;

public static class LocalAuthServiceCollectionExtensions
{
    public static IServiceCollection AddLocalAuthSeeding(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<LocalAuthOptions>(configuration.GetSection("LocalAuth"));
        services.AddScoped<LocalAuthSeeder>();

        return services;
    }
}
