# Files 详细设计

## 1. 背景

`Files` 模块已经具备文件上传、下载、元数据记录、本地存储提供者和前端文件页面，但现有文档还没有把存储抽象、元数据模型、软删除与物理删除语义、配置挂接和前端集成整理成一份详细设计。

这份设计的目的，是把 V1 阶段的文件能力固定为“可替换存储提供者的最小文件模块”：先稳定上传、下载、查询、删除和元数据抽象，再为对象存储、持久化仓储和预览能力预留明确扩展点。

## 2. 目标

- 提供统一的文件上传、下载、查询和删除能力。
- 提供独立的文件元数据模型，与存储实现解耦。
- 提供可替换的存储提供者抽象，默认落地本地存储。
- 支撑文件模块前端页面接入 Shell。
- 通过配置 schema 暴露上传限制和存储策略的基础配置入口。

## 3. 非目标

- 不在本轮实现对象存储（OSS / S3）正式接入。
- 不实现分片上传、断点续传、文件预览和病毒扫描。
- 不实现复杂的权限域隔离、临时签名 URL 或外链分享。
- 不引入数据库持久化元数据存储。
- 不把旧平台全部文件资产迁移纳入当前阶段。

## 4. 范围

涉及范围：

- 后端模块：`server/modules/Files/`
- 前端模块：`frontend/modules/files/`
- 测试项目：`tests/Files.Tests/`
- 配置 schema：`FilesModule.GetConfigSchema()`

当前不覆盖：

- 专用文件服务拆分
- 对象存储驱动实现
- 文件转换、缩略图、在线预览

## 5. 设计约束

- 文件元数据与实际二进制存储必须解耦，避免未来切换存储实现时侵入业务逻辑。
- 上传和下载逻辑统一通过 `IFileService` 暴露。
- 当前允许本地文件系统和内存元数据仓储，但接口必须为未来正式实现保留稳定边界。
- 删除能力必须区分软删除和物理删除。
- 模块权限、配置 schema 和前端菜单路由必须与后端声明保持一致。

## 6. 总体方案

采用“文件服务协调上传下载 + 存储提供者负责二进制 + 元数据仓储负责索引”的方案：

- `FilesModule` 负责注册默认的 `LocalStorageProvider`、`InMemoryFileMetadataRepository` 和 `IFileService`。
- `FileService` 负责处理上传、下载、元数据查询、软删除和物理删除。
- `IStorageProvider` 负责文件二进制的保存、读取、删除和存在性检查。
- `IFileMetadataRepository` 负责记录和查询元数据。
- 控制器负责 HTTP 文件接口暴露，前端模块提供文件列表和上传页面。

选择这个方案的原因：

- 存储实现和业务接口分离后，后续接 OSS/S3 时改动面最小。
- 软删除和物理删除分开，便于后续审计和恢复策略扩展。
- 当前方案已经能满足开发阶段与 Phase 4 的“最小可运行文件能力”目标。

本轮不选这些方案：

- 不选控制器直接操作文件系统：会让服务逻辑分散且难测试。
- 不选先做对象存储优先实现：会把基础模块验证复杂化。
- 不选把文件二进制直接塞进数据库：不符合当前阶段目标。

## 7. 关键模型

### 上传模型

- `UploadRequest`
  - `FileStream`
  - `FileName`
  - `ContentType`
  - `UploadedBy`
  - `EntityType`
  - `EntityId`
  - `ExtendedProperties`

### 元数据模型

- `FileMetadata`
  - `FileId`
  - `FileName`
  - `FileSize`
  - `ContentType`
  - `StoragePath`
  - `StorageProvider`
  - `FileHash`
  - `UploadedBy`
  - `UploadedAt`
  - `EntityType`
  - `EntityId`
  - `ExtendedProperties`
  - `IsDeleted`
  - 删除相关字段（由仓储维护）

### 下载模型

- `DownloadResult`
  - `FileStream`
  - `FileName`
  - `ContentType`
  - `FileSize`

### DTO

- `UploadResponse`
- `FileInfoResponse`

### 抽象接口

- `IFileService`
- `IStorageProvider`
- `IFileMetadataRepository`

### 前端模型

- `FrontendModule.routes`
- `FrontendModule.menus`
- `FrontendModule.permissions`
- `FileUpload`
- `FileList`
- `FilesHome`

## 8. 核心流程

### 主流程：上传文件

1. 控制器接收 `IFormFile` 和可选的 `entityType / entityId`。
2. `FilesController.Upload(...)` 打开文件流并组装 `UploadRequest`。
3. `FileService.UploadAsync(...)` 校验流、生成 `fileId`、计算文件哈希。
4. 调用 `IStorageProvider.SaveAsync(...)` 保存二进制内容。
5. 组装 `FileMetadata` 并调用 `IFileMetadataRepository.SaveAsync(...)` 保存元数据。
6. 返回 `UploadResponse`。

### 主流程：下载文件

1. 调用方访问 `GET /api/files/{fileId}/download`。
2. `FileService.DownloadAsync(...)` 读取元数据。
3. 若文件不存在或已软删除，则抛出 `FileNotFoundException`。
4. 通过 `IStorageProvider.ReadAsync(...)` 打开文件流。
5. 控制器返回文件流响应。

### 主流程：查询文件

1. 调用方访问 `GET /api/files` 或 `GET /api/files/{fileId}`。
2. 服务层读取元数据仓储。
3. 控制器将元数据映射为 `FileInfoResponse` 返回。

### 主流程：软删除

1. 调用方访问 `DELETE /api/files/{fileId}`。
2. 服务层先查元数据是否存在。
3. 调用 `MarkAsDeletedAsync(...)` 标记软删除，不立刻删除物理文件。

### 主流程：物理删除

1. 调用方访问 `DELETE /api/files/{fileId}/permanent`。
2. 服务层读取元数据。
3. 调用 `IStorageProvider.DeleteAsync(...)` 删除物理文件。
4. 调用 `IFileMetadataRepository.DeleteAsync(...)` 删除元数据记录。

## 9. 接口与集成点

### 后端接口

- `POST /api/files/upload`
- `GET /api/files/{fileId}/download`
- `GET /api/files/{fileId}`
- `GET /api/files`
- `DELETE /api/files/{fileId}`
- `DELETE /api/files/{fileId}/permanent`

### 与配置中心的集成

`FilesModule` 当前声明 3 个配置项：

- `Files.MaxUploadSize`
- `Files.AllowedExtensions`
- `Files.StorageProvider`

当前这些配置主要作为 schema 和默认值声明，尚未在上传逻辑里形成完整运行时限制。

### 与审计模块的衔接

- 当前未自动记录文件上传、下载、删除的审计事件。
- 后续可以在 `FileService` 的关键动作后接入 `Audit` 记录。

### 与前端的集成

- `frontend/modules/files/src/index.ts` 注册 `/files` 路由。
- 前端模块声明 `Files.Upload / Download / Delete / View` 四类权限。
- `FilesHome` 页面组合 `FileUpload` 与 `FileList` 组件。

## 10. 数据与状态

- 当前二进制内容默认写入应用目录下的 `FileStorage`。
- 元数据保存在内存仓储中，重启后丢失。
- `FileHash` 使用 SHA-256 计算。
- 软删除与物理删除的状态语义分离。
- 当前未实现配额、目录隔离、生命周期管理和重复文件去重。

## 11. 错误处理与风险

- 无效流会在上传时抛出参数错误。
- 元数据不存在或已删除时，下载会返回文件不存在。
- 当前上传逻辑未真正按配置限制文件大小和扩展名。
- 本地存储路径位于应用目录，生产环境下需要更稳定的路径与权限管理。
- 元数据仓储和存储提供者当前都偏开发态实现，重启或多实例部署下不适合作为正式方案。

风险控制方式：

- 通过抽象接口隔离后续对象存储和数据库实现。
- 通过软删除保留后续恢复和审计扩展空间。
- 通过测试覆盖上传、下载、删除和本地存储基础能力。

## 12. 验证方案

- `tests/Files.Tests` 通过。
- `pnpm -C frontend build` 通过。
- 上传接口可保存文件并返回元数据。
- 下载接口可读取已上传文件。
- 查询接口可返回文件元数据列表。
- 软删除和物理删除接口行为符合预期。

## 13. 未决问题

- 上传限制是否在下一阶段正式绑定到 ConfigCenter 的运行时配置读取。
- 对象存储接入是直接新增 provider，还是引入 provider 工厂和策略层。
- 文件模块是否需要在后续阶段拆成“元数据 + 存储 + 预览”更细的能力边界。

## 14. 实施拆分建议

1. 固化上传、下载、查询、删除和元数据抽象。
2. 固化本地存储提供者和基础前端页面。
3. 补齐验证记录，确认接口、前端和测试口径一致。
4. 后续再引入对象存储、持久化元数据、审计联动和上传限制校验。
