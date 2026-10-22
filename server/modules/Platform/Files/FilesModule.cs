using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Files.Abstractions;
using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Modules.Files.Repositories;
using ICP.Platform.Modules.Files.Services;
using ICP.Platform.Modules.Files.Storage;
using ICP.Platform.Sdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ICP.Platform.Modules.Files;

/// <summary>身份与模型来自正式 Capsule，仅绑定平台文件行为与自有服务。</summary>
public sealed class FilesModule : ModuleCapsuleBindingBase
{
    public override string BehaviorKey => "files.backend";

    /// <inheritdoc/>
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddOptions<FilesOptions>().Configure<IConfiguration>((settings, configuration) => configuration.GetSection("Files").Bind(settings)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FilesOptions>, FilesOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPlatformObjectIdGenerator, PlatformObjectIdGenerator>();
        services.AddSingleton<FileIoGate>();
        services.AddSingleton<LocalStorageProvider>(provider => new(provider.GetRequiredService<IOptions<FilesOptions>>().Value.StorageRoot));
        services.AddSingleton<IFileContentStore>(provider => provider.GetRequiredService<LocalStorageProvider>());
        services.AddScoped<FileStateStore>();
        services.AddScoped<IFileLifecycleService, FileLifecycleService>();
        services.AddScoped<IFileCopyService>(provider => (FileLifecycleService)provider.GetRequiredService<IFileLifecycleService>());
        services.AddScoped<FileCleanupService>();
        services.AddHostedService<FileCleanupWorker>();
        // DenyAll 的历史 Controller 保留其依赖，业务消费者只能调用稳定生命周期契约。
        services.AddSingleton<IStorageProvider>(provider => provider.GetRequiredService<LocalStorageProvider>());
        services.AddSingleton<IFileMetadataRepository, InMemoryFileMetadataRepository>();
        services.AddScoped<IFileService, FileService>();
    }
}
