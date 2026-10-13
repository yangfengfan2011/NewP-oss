using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Modules.Files.Abstractions;
using ICP.Platform.Modules.Files.Repositories;
using ICP.Platform.Modules.Files.Services;
using ICP.Platform.Modules.Files.Storage;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Files;

public class FilesModule : ModuleBase, IMenuProvider, IConfigProvider
{
    public override string ModuleId => "Files";
    public override string DisplayName => "文件管理";
    public override string Version => "1.0.0";
    public override string Description => "提供文件上传、下载、存储抽象能力";

    public override void ConfigureServices(IServiceCollection services)
    {
        // 注册存储提供者（默认使用本地存储）
        services.AddSingleton<IStorageProvider>(sp =>
        {
            var basePath = Path.Combine(AppContext.BaseDirectory, "FileStorage");
            return new LocalStorageProvider(basePath);
        });

        // 注册元数据仓储（默认使用内存实现，生产环境应替换为数据库实现）
        services.AddSingleton<IFileMetadataRepository, InMemoryFileMetadataRepository>();

        // 注册文件服务
        services.AddScoped<IFileService, FileService>();
    }

    public IReadOnlyList<MenuDescriptor> GetMenus()
    {
        return new[]
        {
            new MenuDescriptor
            {
                Id = "Files",
                DisplayName = "文件管理",
                ParentId = null,
                Route = "/files",
                Icon = "file-icon",
                Order = 50,
                ModuleId = ModuleId
            }
        };
    }

    public IReadOnlyList<ConfigDescriptor> GetConfigSchema()
    {
        return new[]
        {
            new ConfigDescriptor
            {
                Key = "Files.MaxUploadSize",
                DisplayName = "最大上传文件大小",
                Description = "单个文件最大上传大小（MB）",
                Type = "Int",
                DefaultValue = "100",
                Required = true,
                ModuleId = ModuleId,
                Group = "上传限制"
            },
            new ConfigDescriptor
            {
                Key = "Files.AllowedExtensions",
                DisplayName = "允许的文件扩展名",
                Description = "允许上传的文件扩展名列表（逗号分隔）",
                Type = "String",
                DefaultValue = ".jpg,.jpeg,.png,.pdf,.doc,.docx,.xls,.xlsx",
                Required = false,
                ModuleId = ModuleId,
                Group = "上传限制"
            },
            new ConfigDescriptor
            {
                Key = "Files.StorageProvider",
                DisplayName = "存储提供者",
                Description = "文件存储提供者类型（Local/OSS/S3）",
                Type = "String",
                DefaultValue = "Local",
                Required = true,
                ModuleId = ModuleId,
                Group = "存储配置"
            }
        };
    }
}
