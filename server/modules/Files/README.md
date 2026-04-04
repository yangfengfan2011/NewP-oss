# Files 模块

文件管理模块,提供文件上传、下载、存储抽象能力。

## 功能特性

- 文件上传/下载
- 文件元数据管理
- 存储提供者抽象(支持本地存储,可扩展至对象存储)
- 软删除和物理删除
- 文件哈希校验
- 业务实体关联
- 权限集成
- 审计挂钩点

## 后端架构

### 核心组件

- **IStorageProvider**: 存储提供者抽象接口
- **IFileService**: 文件服务接口
- **IFileMetadataRepository**: 文件元数据仓储接口
- **LocalStorageProvider**: 本地文件系统存储实现
- **FileService**: 文件服务实现
- **InMemoryFileMetadataRepository**: 内存元数据仓储(演示用)

### API 端点

- `POST /api/files/upload` - 上传文件
- `GET /api/files/{fileId}/download` - 下载文件
- `GET /api/files/{fileId}` - 获取文件信息
- `GET /api/files` - 查询文件列表
- `DELETE /api/files/{fileId}` - 删除文件(软删除)
- `DELETE /api/files/{fileId}/permanent` - 永久删除文件

### 权限点

- `Files.Upload` - 上传文件
- `Files.Download` - 下载文件
- `Files.Delete` - 删除文件
- `Files.View` - 查看文件

## 前端组件

### 组件

- **FileUpload**: 文件上传组件
- **FileList**: 文件列表组件
- **FilesHome**: 文件管理主页

### 服务

- **fileService**: 文件服务客户端

### 使用示例

```tsx
import FileUpload from '@icp-modules/files/components/FileUpload';
import FileList from '@icp-modules/files/components/FileList';

function MyComponent() {
  const [refreshTrigger, setRefreshTrigger] = useState(0);

  return (
    <div>
      <FileUpload
        entityType="Order"
        entityId="12345"
        onUploadSuccess={() => setRefreshTrigger(prev => prev + 1)}
      />
      <FileList
        entityType="Order"
        entityId="12345"
        refreshTrigger={refreshTrigger}
      />
    </div>
  );
}
```

## 扩展存储提供者

实现 `IStorageProvider` 接口即可添加新的存储后端:

```csharp
public class S3StorageProvider : IStorageProvider
{
    public string ProviderName => "S3";
    
    public async Task<string> SaveAsync(Stream fileStream, string fileName, CancellationToken cancellationToken)
    {
        // 实现 S3 上传逻辑
    }
    
    // 实现其他接口方法...
}
```

在 `FilesModule.ConfigureServices` 中注册:

```csharp
services.AddSingleton<IStorageProvider, S3StorageProvider>();
```

## 配置项

- `Files.MaxUploadSize` - 最大上传文件大小(MB)
- `Files.AllowedExtensions` - 允许的文件扩展名
- `Files.StorageProvider` - 存储提供者类型

## 测试

运行测试:

```bash
dotnet test tests/Files.Tests
```

## 待完成事项

- [ ] 数据库持久化元数据仓储实现
- [ ] 对象存储(OSS/S3)提供者实现
- [ ] 文件预览功能
- [ ] 缩略图生成
- [ ] 大文件分片上传
- [ ] 断点续传
- [ ] 文件去重优化
