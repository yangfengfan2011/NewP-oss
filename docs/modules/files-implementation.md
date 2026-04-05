# Files 模块实现概览

## 当前状态

状态：已完成 V1 基础实现并通过验证

Files 模块已完成”可替换存储提供者的最小文件模块”目标，具备完整的后端抽象、前端模块、测试覆盖和配置 schema。

## 已实现内容

### 后端实现

**模块入口**
- `server/modules/Files/FilesModule.cs`：注册服务、声明权限、菜单和配置 schema

**抽象接口**
- `IFileService`：文件服务接口，定义上传、下载、查询、软删除、物理删除
- `IStorageProvider`：存储提供者抽象，定义保存、读取、删除、存在性检查
- `IFileMetadataRepository`：元数据仓储接口，定义元数据 CRUD 和查询

**核心实现**
- `FileService`：文件服务实现，协调上传下载流程，计算 SHA-256 文件哈希
- `LocalStorageProvider`：本地文件系统存储，按日期目录组织文件
- `InMemoryFileMetadataRepository`：内存元数据仓储，支持软删除和条件查询

**控制器**
- `FilesController`：提供 6 个 HTTP 接口
  - `POST /api/files/upload`：上传文件
  - `GET /api/files/{fileId}/download`：下载文件
  - `GET /api/files/{fileId}`：获取文件元数据
  - `GET /api/files`：查询文件列表
  - `DELETE /api/files/{fileId}`：软删除文件
  - `DELETE /api/files/{fileId}/permanent`：物理删除文件

**模型与 DTO**
- `FileMetadata`：文件元数据模型，包含 FileId、FileName、FileSize、ContentType、StoragePath、FileHash、UploadedBy、EntityType/EntityId、IsDeleted 等字段
- `UploadRequest`：上传请求模型
- `DownloadResult`：下载结果模型
- `UploadResponse`：上传响应 DTO
- `FileInfoResponse`：文件信息响应 DTO

**配置 Schema**
- `Files.MaxUploadSize`：最大上传文件大小（默认 100MB）
- `Files.AllowedExtensions`：允许的文件扩展名
- `Files.StorageProvider`：存储提供者类型（默认 Local）

**权限声明**
- `Files.Upload`：上传文件
- `Files.Download`：下载文件
- `Files.Delete`：删除文件
- `Files.View`：查看文件

### 前端实现

**模块注册**
- `frontend/modules/files/src/index.ts`：注册路由、菜单、权限和国际化

**页面组件**
- `FilesHome`：文件管理主页，组合上传和列表组件
- `FileUpload`：文件上传组件
- `FileList`：文件列表组件

**服务层**
- `FileService`：封装文件 API 调用，提供 upload、download、query、delete、permanentDelete 方法

**前端路由**
- `/files`：文件管理页面，需要 `Files.View` 权限

### 测试覆盖

**单元测试**
- `FileServiceTests`：测试上传、下载、软删除逻辑（3 个测试用例）
- `LocalStorageProviderTests`：测试本地存储保存、读取、删除、存在性检查（4 个测试用例）

**测试结果**
- 7 个测试用例全部通过
- 前端构建通过，无类型错误

## 核心流程实现

### 上传流程
1. 控制器接收 `IFormFile` 和可选的 `entityType/entityId`
2. 打开文件流并组装 `UploadRequest`
3. `FileService` 计算 SHA-256 哈希
4. 调用 `IStorageProvider.SaveAsync` 保存二进制
5. 组装 `FileMetadata` 并保存到仓储
6. 返回 `UploadResponse`

### 下载流程
1. 根据 `fileId` 查询元数据
2. 检查文件是否存在或已软删除
3. 通过 `IStorageProvider.ReadAsync` 打开文件流
4. 返回文件流响应

### 软删除流程
1. 查询元数据是否存在
2. 调用 `MarkAsDeletedAsync` 标记 `IsDeleted = true`
3. 不删除物理文件

### 物理删除流程
1. 查询元数据
2. 调用 `IStorageProvider.DeleteAsync` 删除物理文件
3. 调用 `IFileMetadataRepository.DeleteAsync` 删除元数据记录

## 技术特征

### 架构设计
- 存储提供者抽象与业务逻辑解耦，便于后续接入对象存储
- 软删除与物理删除分离，保留审计和恢复扩展空间
- 元数据与二进制存储分离，支持独立扩展

### 存储策略
- 本地存储按 `yyyy/MM/dd` 日期目录组织
- 文件名使用 GUID 避免冲突，保留原始扩展名
- 文件哈希使用 SHA-256 算法

### 元数据管理
- 支持关联业务实体（EntityType/EntityId）
- 支持扩展属性（ExtendedProperties JSON）
- 支持软删除标记和查询过滤

### 前端集成
- 与 Shell 模块路由和权限系统集成
- 支持拖拽上传和点击选择
- 上传成功后自动刷新列表

## 当前限制

### 开发态实现
- 元数据仓储使用内存实现，重启后丢失
- 本地存储路径位于应用目录，生产环境需调整
- 配置 schema 已声明但未在上传逻辑中强制校验

### 未实现功能
- 对象存储（OSS/S3）提供者
- 数据库持久化元数据存储
- 文件大小和扩展名运行时限制
- 分片上传、断点续传
- 文件预览、缩略图生成
- 审计事件自动记录
- 文件去重、配额管理
- 临时签名 URL、外链分享

## 扩展点

### 存储提供者扩展
- 实现 `IStorageProvider` 接口
- 在 `FilesModule.ConfigureServices` 中注册
- 支持 OSS、S3、Azure Blob 等

### 元数据仓储扩展
- 实现 `IFileMetadataRepository` 接口
- 替换 `InMemoryFileMetadataRepository`
- 接入 EF Core 或其他 ORM

### 审计集成
- 在 `FileService` 关键操作后调用 `IAuditService`
- 记录上传、下载、删除事件

### 配置限制
- 在 `FileService.UploadAsync` 中读取 `IConfigService`
- 校验文件大小和扩展名

## 后续工作

### Phase 5 计划
- 引入数据库持久化元数据存储
- 实现 OSS/S3 存储提供者
- 补充上传限制运行时校验
- 接入审计模块记录文件操作

### 长期规划
- 文件预览和缩略图生成
- 分片上传和断点续传
- 文件去重和配额管理
- 临时签名 URL 和外链分享
- 病毒扫描和内容审核
