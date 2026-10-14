# Files 平台文件服务

Files 的正式身份、模型和迁移来自 `frontend/modules/platform/files/newp.plugin.json` 及其 authoring。后端只绑定 `files.backend`，前端能力关闭。

业务模块仅引用 `Contracts/Files.Contracts.csproj` 并消费 `IFileLifecycleService`。Files 自有 `FileRecord`、`FileUploadLease` 和 `FileUsage` 三个 Entity 及独立 SQL Server migration stream；业务模块拥有实际文件关联、角色和格式规则。

## 上传与事务

`StageAsync` 先在独立短事务中持久化 receiving intent 和已知私有键。提交确认后才写 `.part`，单遍计数和 SHA256，不要求输入流可 seek，不整体缓冲。写句柄关闭后完成不可覆盖的本地改名，再由第二短事务提交 ready/staged。

上传失败或提交结果未知时，在原事务退出后的新 scope 重读持久事实。只有仍 receiving 的失败进入回收；未知结果已经 staged 时保留合法 lease，已经 confirmed 且使用 active 时保留内容。

`ConfirmUsageAsync` 和 `ReleaseUsageAsync` 要求已有平台事务，只登记共享 tracker 的变更，不能自行 Flush、Save 或提交。业务 owner 在确认返回后填齐关联的 UsageId，再由外层统一保存和提交。相同已 confirmed 的活跃目标只返回原使用，不能换目标或复活 released 使用。

## 用途与读取

用途 owner 贡献 `IFileOwnerPolicy` 和 `FileOwnerPolicyRequirement`。启动时必需 policy 缺失、重复 key 或重复 requirement 都拒绝装配。Files 负责 actor、lease、持久状态和双向使用配对；用途 policy 负责实际业务资格、关联及首次创建证明。

`ReadOwnedStagedAsync` 接收完整暂存集合，由 Files 按 `MaxReadBatchSize` 内部分批查询并按输入次序返回。空白、重复及非法 token 在查询前拒绝；空集合仍核验 actor 和登记用途。集合内他人或错用途 token 的不可见结果优先于本人过期、取消或已消费冲突，取消及持久故障继续传播。此预读不预留或消费暂存，最终 Confirm 仍在原业务事务内锁定并重验。

Files 不另设暂存集合总量上限；业务 owner 约束整个输入（当前 Document 最多 101 项），完整结果及身份校验占用 O(N) 空间。`MaxReadBatchSize` 限制内部每次数据库读取，不能据此声称整个调用具有固定内存或总工作量上界。

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
