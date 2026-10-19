# Document

Document 拥有稳定主身份、初始 DocumentVersion/DocumentIteration 及当前 regular 版次的文件关联，不包含修订、工作副本、BOM 或专业属性。文件字节与使用生命周期由 Files 拥有。`Document` 是真实 Entity，身份和审计由平台维护；`ECODE`、`ENAME` 显式采用 builtin，`Description` 使用可空长文本语义。

图文档编码必填且本模型内唯一，名称必填但不唯一。标准字段策略规范化名称与编码，模块在数据库侧按同一列比较规则预检编码；数据库唯一索引是并发保护事实源，竞争复用标准 409 冲突，真副本在数据库核实同码后定位 ECODE 错误。说明可缺省，最多 500 字，服务端拒绝超长值。

稳定资源 `document.document` 提供 `/api/document/documents` 标准创建和读取，页面 `/documents` 使用共享标准列表与创建 Drawer，`document.document.detail` 提供只读属性 Drawer 和正式 `/documents/:id` 详情。创建提交后不可读或展示失败继续返回不含对象标识的成功回执，客户端只重试读取。

侧栏不提供独立图文档入口；用户从 Workspace 选中文件夹后新建，由 Workspace 在同一对象操作事务内调用 `document.document` 创建行为并建立内容关系。原页面路由保持 routeOnly；功能入口通过 `parentModuleId: Workspace`、`parentKey: workspace` 显式归入工作空间，详情和全部子操作共享这条授权祖先链，不再发布独立顶层授权入口。数据资源、对象行为和接口执行归属仍由 Document 拥有。

## 完整真副本

`documentObjectCopy` 是独立于引用 `documentCopy` 和 download 的数据动作。Workspace 经 `IDocumentCopies` 核证真实 current version/regular、主对象与 regular 业务字段及完整 FileLink 集合，持久保存 owner 的不透明确认；Workspace 不读 Document/Files 私有表。确认后 current、字段、角色、顺序或任一来源使用变化均拒绝，不猜其他历史或换出现位置。authoring 和实际持久模型的全部字段显式分类；新增字段必须先决定复制、重置或拒绝。

`document.document.copy` 在最终原事务内再次核证确认，复用正常 Create 生成新 master、首版和 regular；不复制旧身份、技术指针、审计或历史。无文件文档仍走同一合同。完整主附件在图事务外逐项流式物理准备，再由本 owner 的真实 Added FileLink 与 copying creation proof 确认独立使用。全部正常候选校验在准备字节前执行，最终每个 Create 与权限仍重验。Workspace 的同模型集合编排逐个消费本 owner，多个 Document 只由一个外层事务提交；本 owner 不自行提交或解释其他文档的准备。既有合法文件不套用当前上传准入配置，但明确复制资格、来源活跃及回链不变量始终执行。

删除对象只释放自己的独立使用，正常下载继续要求原 read/download；移除 Workspace 引用不释放文件。多查询只读单元复用自己打开的连接并在成功/失败/取消后归还，不关闭调用者连接；不会扩大事务或跨字节 I/O 持有连接。Files 负责取消、未知准备提交、进程恢复及物理回收，Document 从持久确认推导每个文件 request key；不以请求内存清单恢复。回退关闭真副本动作并保留记录、使用、内容与已发布前向迁移，不能让旧 Host 消费新 frontier 或通过 Down 删除准备事实。

Document 的 LatestVersionId 与 DocumentVersion 的 CurrentIterationId 是服务端 nullable 标量指针；归属使用 DocumentVersion.DocumentId、DocumentIteration.VersionId 的自有 Restrict FK，不建立反向循环 FK。当前只有首版（ordinal 1、label 1、state new）及 regular 版次（ordinal 1、dataset generic）。所有独立与 Workspace 创建共用正式 creation recorder，在原事务维护主对象、首版和快照；所属 Create 负责 Flush 与原仓储规范重读。更新同步当前 regular 的编码、名称、说明及可信更新审计，指针缺失或回链损坏明确失败，不猜其他版次。元数据输入拒绝服务端指针及下级身份。

创建、更新与删除采用所属模块的标准对象行为，不开放横切。Workspace 通过稳定行为键组合更新与物理删除，字段规则及编码唯一性仍归对象拥有者；普通 HTTP 更新与删除端点保持关闭。插入与仓储规范重读在唯一平台事务内，规范重读失败整体回滚；提交后的读取裁决和展示故障由标准服务返回成功回执。

候选通过同一标准 GET 列表按编码或名称搜索、数据库分页和窄字段投影；正式功能读取资格与对象读取计划同时生效，可发现没有任何目录引用的独立图文档。当前没有真实 Lookup 字段消费者，因此不开放单独 Options 能力。Workspace 通过稳定资源、模型显示、页面绑定与 Document.Contracts 消费，不依赖 Document 私有实现；目录 Link 和混合内容查询仍由 Workspace 拥有。

文件夹内容入口提供编辑和删除，Workspace 在同一事务清理全部引用；Document 先登记使用释放并清理自有 FileLink，再在数据库清理 Iteration/Version 和 master，后项失败连同先前释放和删除一起回滚。物理内容回收归 Files，业务删除不直接删除字节；独立页面继续只开放创建与读取，启停和导出保持拒绝。标准资源登记要求的对象授权能力使用现有正式功能声明，不引入额外权限系统。

**文件关联（DocumentFileLink）**属于 Document，父端是实际 DocumentIteration，子端是 Files 的 FileRecord；它具有独立 LID，必须保存 FileUsageId。显式 uploads 包含一个 main 和零至一百个 attachment；main 的 Ordinal=0，附件按请求次序为 1..N，所有关联身份由服务端填写。附件列表可空或省略，空 main、非 canonical token、主附件全局重复及超过当前新建配置上限的集合均拒绝；省略 uploads 保留 metadata-only 创建。主附件全部先登记为真实 Added Link，再按服务器 FileId、UploadId 排序确认，显示次序不参与 Files 事务锁序。

Document.Contracts 的文件创建复用原所属 Create，在同一业务事务内建立主对象、首版、regular 版次、实际 Added FileLink 和 Files 使用；创建证明绑定本次实际版次及事务。Files.Contracts 的 Confirm 只登记共享 tracker，全部关联填齐 FileUsageId 后才允许首次 Flush；Release 同样不独立提交。Document 将已按业务规则限量的完整暂存集合一次交给 Files，由 Files 按自有 MaxReadBatchSize 内部分批并统一裁决，整集合身份或用途不可见优先于本人暂存失效；安全元数据仍由 Document 按该上限分批读取。Files 不另设集合总量上限，预读不预留使用，资格检查完成前不登记业务写入，最终 Confirm 仍重验。上传预检复用标准字段、审计及完整候选 create 裁决，只返回验证结果，不能公开候选身份或代替保存时重验。业务提交结果未知时保留持久使用与内容，不自动重放保存或盲目取消暂存。

属性文件列表沿当前身份和真实关联批量读取安全元数据，最多 101 项，按 Ordinal/LID 稳定读取。只公开 FileLink 身份、角色、次序及文件显示信息，不公开 File/Usage 身份或存储键。新建附件配置调低不改变既有合法集合的读取、下载或释放。read 允许而 download 拒绝时仍可看列表；下载分别重验正式功能与数据 download 资格，并核对 Document/版次/FileLink/Usage/File 回链。不可读或错目标安全拒绝；已授权的关联损坏、空或重复使用身份及内容缺失保持明确故障。存储配置、接收意图、取消、TTL 与回收状态的唯一事实见 [Files README](../../../server/modules/Platform/Files/README.md)。

所属对象独立声明 `documentCopy` 数据动作（`data.document.copy`），要求 read，只供引用复制资格裁决，不克隆实体或执行 update/delete。显示词条由本 owner 的 DataScope 词典提供，Workspace 通过正式对象资格接口消费，目录关系与剪贴板仍归 Workspace。

回退优先撤销 Document 导航与资源接入，保留独立实体及迁移历史。代码回退不自动执行 Down 或清表，旧 Host 不得越过已应用的 schema frontier。

本批迁移依次在已发布 metadata migration 后追加 InitialIdentity 和 FileLink，不重写历史。开发阶段已确认丢弃旧 Document 及其 Workspace 引用；InitialIdentity 迁移通过显式空表前提拒绝非空旧 Document，不自动清数据或留下缺失身份的旧行。清理必须精确限定经确认可丢弃的 Document/引用，保留 Part、Folder、其他业务及有效 history 前缀。正式 Host 负责新 schema 与 history，代码回退不执行 Down。
