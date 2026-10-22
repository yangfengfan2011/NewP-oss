# Files 平台文件服务

Files 的正式身份、模型和迁移来自 `frontend/modules/platform/files/newp.plugin.json` 及其 authoring。后端只绑定 `files.backend`，前端能力关闭。

业务模块仅引用 `Contracts/Files.Contracts.csproj` 并消费 `IFileLifecycleService`、`IFileCopyService`。Files 自有 `FileRecord`、`FileUploadLease`、`FileCopyLease` 和 `FileUsage` 四个 Entity 及独立 SQL Server migration stream；业务模块拥有实际文件关联、角色和格式规则。

## 上传与事务

`StageAsync` 先在独立短事务中持久化 receiving intent 和已知私有键。提交确认后才写 `.part`，单遍计数和 SHA256，不要求输入流可 seek，不整体缓冲。写句柄关闭后完成不可覆盖的本地改名，再由第二短事务提交 ready/staged。

上传失败或提交结果未知时，在原事务退出后的新 scope 重读持久事实。只有仍 receiving 的失败进入回收；未知结果已经 staged 时保留合法 lease，已经 confirmed 且使用 active 时保留内容。

`ConfirmUsageAsync` 和 `ReleaseUsageAsync` 要求已有平台事务，只登记共享 tracker 的变更，不能自行 Flush、Save 或提交。业务 owner 在确认返回后填齐关联的 UsageId，再由外层统一保存和提交。相同已 confirmed 的活跃目标只返回原使用，不能换目标或复活 released 使用。

业务上下文通过 FileOwnerOperationContext 显式交接，不进入上传或业务 Values。首次及幂等 Confirm 由 Document 私有创建 scope 证明真实候选、原事务和 Added 关联，不接受通用创建声明；未存在对象不增加 read。Release 使用 FileOwnerReleaseContext 固定真实资源与对象，在原锁内读取、回链并独立检查。Stage 和 Cancel 保持原功能、actor/lease 守卫；旧 FileOwnerCreationContext 已无消费者并删除。

## 独立文件副本

`IFileCopyService` 只接受已登记 owner 的真实活跃 `FileUsageReference`，通过 `CopySource` 核证来源，不借用 download。每份副本物理克隆不可变字节，生成独立文件和使用身份，保留安全名称、ContentType，并重新测量 Length/Sha256。新上传格式、大小及附件配置不能作为既有内容的默认复制拒绝；业务复制准入仍由 owner 明确裁决。

`FileCopyLease` 明确绑定 actor、request key、来源 File/Usage/owner 和目标用途，不复用 UploadId。准备在业务事务及图锁外执行，以两个独立短事务记录 receiving 与 prepared；actor+request key 的事务锁使取消先于迟到意图时留下持久 cancelled 墓碑。首次 ConfirmCopy 必须在原业务事务内证明真实 Added 关联；prepared 是技术准备，不代替最终来源/目标授权。相同已确认活跃目标可重读原使用，不能改 owner、换关联或复活 released。

来源删除可以先提交，receiving/prepared 的合法复制租约保护源字节；业务最终确认仍要求来源活跃并重新核证。取消先提交取消事实，再中止并等待目标 writer，不持 SQL 事务等待 I/O。准备提交未知只收敛仍 receiving，已 prepared 不盲删；业务提交未知根据 confirmed usage 与持久业务结果裁决。重启清理只处理有登记键的残留；合法 prepared 保留至取消或到期，已 confirmed 依 active usage 保护。上传与复制租约分别保留来源历史，读取/释放核证恰有一个匹配反向租约。多查询的来源事实和复制配对读取在同一自有只读连接内完成，finally归还；已由调用者打开的连接保持，不启用全局pool、不扩大事务、不缓存授权，也不将连接跨字节传输持有。

## 用途与读取

用途 owner 贡献 `IFileOwnerPolicy` 和 `FileOwnerPolicyRequirement`。启动时必需 policy 缺失、重复 key 或重复 requirement 都拒绝装配。Files 负责 actor、lease、持久状态和双向使用配对；用途 policy 负责实际业务资格、关联及首次创建证明。

`ReadOwnedStagedAsync` 接收完整暂存集合，由 Files 按 `MaxReadBatchSize` 内部分批查询并按输入次序返回。空白、重复及非法 token 在查询前拒绝；空集合仍核验 actor 和登记用途。集合内他人或错用途 token 的不可见结果优先于本人过期、取消或已消费冲突，取消及持久故障继续传播。此预读不预留或消费暂存，最终 Confirm 仍在原业务事务内锁定并重验。

Files 不另设暂存集合总量上限；业务 owner 约束整个输入（当前 Document 最多 101 项），完整结果及身份校验占用 O(N) 空间。`MaxReadBatchSize` 限制内部每次数据库读取，不能据此声称整个调用具有固定内存或总工作量上界。

读取和开流由真实 owner 建立本阶段私有上下文，FileId/UsageId/LinkId 不能代替规范业务对象。Document 下载通过实际按钮进入，在原读取与文件回链位置独立执行 read/download；反馈与下载互不作为允许凭据。复制使用 owner 私有 copy scope，分别证明当前来源与创建目标，File policy 保留自身完整回链、批read和 documentObjectCopy 主动检查；ConfirmCopy 在同一事务核真实 Added 关联。幂等重试核原 actor/lease 与持久回链，不重放复制写入或先前裁决；旧无上下文 ABI 和中央创建 context 不恢复。

安全元数据 `ReadUsageMetadataAsync` 仍限制单次输入不超过 `MaxReadBatchSize`，超界拒绝，由调用方分批，同用途 policy 批量验证。开流另行判定 read/download；可信目标的持久关联损坏、物理内容缺失或长度不匹配是明确故障。返回流由调用方释放，请求取消也关闭真实文件句柄。

原 `/api/files` 产品端点继续 `DenyAll`。历史 `IFileService` 及内存仓储只保留该冻结 Controller 的依赖，不作为业务生命周期消费面。没有 StorageKey、裸 FileId 或静态路径下载入口。

## Local 存储与回收

当前只支持 Local 和单 Host。配置的私有根必须显式为绝对路径，根及其祖先不能经文件系统链接逃逸，不能放在 `wwwroot` 或当前 Host 公开的 `Platform:PluginInstallRoot/runtime/modules`、`Platform:PluginInstallRoot/public` 根及其子目录。配置校验也只读解析公开根的链接目标，阻止公开根指向私有内容；发行根内其他私有目录不因名称或父路径误拒。服务器生成扁平键，CreateNew 和不可覆盖改名保护既有字节。

进程级 singleton `FileIoGate` 覆盖全部 writer 句柄、改名和删除。取消和到期先用短事务标记 pending，退出事务后 abort writer，再等待同键 gate；不能持数据库事务等 I/O。Files 使用文件身份的事务锁，业务图锁先于 Files 锁。

`FileCleanupWorker` 在启动和周期执行有界恢复；`FileCleanupService.RunOnceAsync` 为相同扫描的确定性入口。它只处理数据库已登记的键，不扫描存储根。active usage 和合法未确认 lease 阻止回收；最后 release 的候选不等待 confirmed lease 的 TTL。I/O 失败保留 pending 重试，删除成功保留文件及使用 tombstone。confirmed lease 保留消费历史。

## 配置

`FilesOptions` 从 `Files` 配置段绑定并启动校验：

| 参数 | 默认值 |
| --- | --- |
| StorageRoot | 必填绝对私有目录 |
| MaxUploadSizeBytes | 104857600 |
| UploadLeaseHours | 24 |
| CleanupIntervalMinutes | 5 |
| CleanupBatchSize | 100 |
| MaxReadBatchSize | 128，范围 1–1024 |

大小、租期和批量必须为正；请求上限需要为额外 multipart 预算保留空间，租期必须能表示为 UTC DateTime，周期必须在 PeriodicTimer 底层可表示范围内。配置校验只读检查根及祖先，不创建目录。格式、附件数量与版次规则由业务 owner 配置和执行。

## 验证与回退

Files.Tests 覆盖真实 Local I/O、非 seek 和大小边界、路径及链接、配置、gate、事务登记和持久状态恢复。SQLite 状态测试不替代真实业务 owner 的 SQL Server 锁、HTTP 授权、正式 Host 迁移及 OS 重启证据。

回退关闭业务文件入口，保留已发布迁移、记录、使用和存储内容。不能用旧 Host、Down 或清空存储根回退；已经物理删除的字节需要从备份恢复。
