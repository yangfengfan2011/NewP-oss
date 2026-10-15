# Workspace

Workspace 拥有公共空间、当前账号个人空间及其根文件夹。入口使用既有自定义功能声明 `workspace.spaces.access`，包含确保当前账号个人空间和读取根；它不授予普通内容操作权限。公共初始化额外要求 LocalAdmin，读取接口不创建记录。

## Language

**空间（Space）**：导航入口及强制准入范围。Public 对具备入口资格的已认证用户开放，Personal 只匹配可信当前账号 `IdentityAccount`；LocalAdmin 也不能绕过他人的 Personal 准入。

**根文件夹（Root Folder）**：空间 `RootFolderId` 唯一关联的真实 WorkspaceFolder。根与普通文件夹共表，不另建根标志；本期根不能经普通 CRUD 删除、改名或替换。

**来源空间（Origin Space）**：文件夹不可变的创建来源 `OriginSpaceId`，由服务端赋值，不作为公开详情字段。根初始化预分配空间身份，先保存根再保存空间；两次保存属于同一平台事务。

## 边界

- WorkspaceSpace、WorkspaceFolder 及 SQL Server migration stream 由模块 authoring 拥有。主体固定为账号，不保存 `SubjectModelKey`；唯一索引为 `(SpaceKind, SubjectId)`，公共空间的 `SubjectId` 为 NULL，个人空间为可信当前账号 ID，NULL 也参加唯一约束；RootFolderId 非空、唯一且使用 Restrict 外键。OriginSpaceId 仅索引，不形成循环物理外键。
- 初始化使用事务级 SQL Server 应用锁和数据库唯一约束，成功后重复命令复用原身份。已有主体配对或根来源不一致时拒绝，不修复或新建替代根。命令提交后读取拒绝或展示失败返回不含根标识的成功回执，不反转成功事实。
- 空间准入与 Authorization 的正式对象读取计划取交集。专用资源通过既有 Contracts 的 `DataScopeResource` 显式贡献，不为取得读取裁决而开放普通 CRUD。Authorization 拥有资源目录整合及身份、活动 owner、真实模型、主键和重复校验；Workspace 不维护另一套字段目录。
- 根详情使用真实 Folder EID，并由空间唯一归属验证其准入；非根不能借此读取。已知出现位置恢复仍同时验证 SpaceId 与 RootFolderId，不自动改选另一条路径。字段白名单与审计 raw/display 使用共享 Standard CRUD 详情服务及正式 Model planner。
- 左树使用共享 Tree/PageRef 宿主和模块 Provider，只有真实公共、个人两个根。根与普通文件夹共用标准内容列表，属性通过共享只读详情打开。完整空间、根与有序 LID 链同时用于选择、展开、右侧输入、地址恢复及面包屑；面包屑只回溯已加载层级，祖先点击沿用组合离开保护，不为呈现读取未加载分支。恢复失效时明确提示并安全回到公共根，不选择同一实体的另一父路径。Tree View 的 linkSource 采用平台唯一树查询目录；模块 Provider 显式 personal/ensure 后委托通用 roots/children/path/search，已知关系及次序变化使搜索从第一页重读。名称搜索保留每个完整出现链，先空间准入、逐层 read，再按根及 TreeOrdinal/LID 先序计数分页；空词返回空页，错误/取消不伪装成完整结果。主体切换释放整个组合实例、选择及未完成读取。

## 直接文件夹内容

WorkspaceFolderContent 是正式 Link，父端由 authoring 固定为 WorkspaceFolder，子端由必填 ChildModelKey 判别；允许组合只来自 linkedTypes。正式目录登记 WorkspaceFolder、Part、Document 三个真实端点；读取/显示契约来自其所属模块，Workspace 显式依赖 Part 与 Document。LID、PEID、CEID 与关系审计由服务端填写；关系不保存 SpaceId、父位置、名称或对象审计影子值。

零组件和图文档不提供独立侧栏菜单；用户选择文件夹后，从“新建”父按钮选择新建文件夹、零组件或图文档，统一使用共享右侧 Drawer 和“保存并关闭”，页面不再提供“添加已有对象”。内容列表的名称作为属性入口，操作菜单提供复制引用、编辑、删除及移除引用；所属模块的 routeOnly 路由及功能位置继续服务于详情准入与属性打开。

文件夹内创建由 Workspace 的 createPart/createDocument 功能动作准入，并交集当前完整位置、父文件夹 edit 与所属对象 create 数据范围。Workspace 对象行为静态声明调用 part.part/document.document 的 create，统一执行器在一个事务与审计作用域内保存所属实体和内容 Link；字段规范化、校验、唯一约束及对象审计仍由所属模块标准行为负责。创建回执不包含对象标识，后续列表读取独立裁决；刷新失败不能重放创建。已有引用 API 保留为独立功能授权能力，不再贡献页面按钮。回退这两个创建入口不删除已创建实体或关系。

图文档的每个主文件和附件使用同一个专属手工 multipart 上传入口：先读取严格 Position 和 values JSON，再把单个 file 流交给 Files.Contracts。接收前验证完整出现位置、祖先 read、当前父 edit 与 Document.Contracts 的候选 create；最终保存取得原文件夹图锁后再次验证，暂存成功不是权限或位置凭证。最终 JSON 使用专属 DTO，仅传业务字段、mainUploadId 和可空的 attachmentUploadIds，不接受 actor、owner、File/Usage 身份、角色或次序。附件数量、角色及集合去重由 Document 拥有，旧 raw Files 入口保持冻结。

上传和取消属于独立 Files 生命周期操作；一次保存经 Document.Contracts 把 Document 图、全部文件使用确认及初始目录 Link 纳入原对象事务。Workspace 不读取 Document 或 Files 私有表，也不自行 Flush 文件使用。主文件单选、附件多选，使用现有 Ant Design 控件逐项呈现真实进度、失败、重试和移除；上传并发固定为一，全部已暂存且主文件存在才允许保存。上传成功只代表暂存；关闭或切换位置/主体中断旧上传并收敛本次未提交集合，迟到响应不能恢复旧选择。已开始保存、已成功或结果未知的集合禁止自动重发保存或取消可能已确认的使用。

前端上传会话由 Workspace 私有 module 唯一持有队列、暂存 token、在途上传、保存结果及清理资格；业务字段仍归输入草稿，上传读取当前值，保存固定本次字段与文件集合。输入视图只订阅只读快照并发出选择、移除和重试意图；createDocument handler 必须取得真实、未结束且匹配当前位置与功能的会话，不保留无会话创建 fallback。会话引用仅在 SDK 的不透明输入中按内存身份传递，HTTP 仍显式投影既有业务字段和 uploads，后端 metadata-only 创建不受影响。

会话随输入实际挂载建立，普通重渲染和语言变化不重建；React effect 重挂载建立新实例，旧实例结束后不能复活。实际卸载（包含现有 Drawer 隐藏后销毁输入）只结束交互和上传，不取消已发保存；迟到保存只更新原会话，已结束会话即使收到明确失败也不能再次提交，未能确认取消的暂存沿 TTL 收敛。Shell 继续拥有动作互斥、离开保护、错误裁决和成功交接；编码重复的精确可修正 409 保留原草稿，其他冲突沿原规则处理，成功只交回一次原 operation/change，不由会话控制刷新。

出现位置由 SpaceId、RootFolderId 与有序 LID 链组成。WorkspacePositions 薄适配平台 LinkTreeReader 的精确链解析，批量读取该链，核对空间准入、唯一根归属、父子类型与衔接、无重复节点及每层对象读取资格；该路径不调用搜索或枚举替代父链。正向内容查询、树直属层、已知路径恢复和全部内容写入均执行该检查；可读的裸 Folder EID 不能代替出现位置。

文件夹独立详情、单选候选与 Link 端点共享强制范围：对象可读，且来源空间可准入，或存在一条从可准入根出发、祖先皆可读的路径。OriginSpaceId 不公开且不可修改；私人来源的普通文件夹可以通过公共引用共享，移除最后公共路径后重新计算范围。来源主体仍能发现失去全部引用的文件夹并重新添加，所有正式空间根排除于候选。候选以 EID 去重，Link 查询仍逐 LID 返回。

普通文件夹及根详情使用固定公开投影，拒绝字段选择等未声明 query 参数；候选只允许关键词和标准页码/页大小，内部来源不能通过请求字段扩展到响应。

可达范围用 SQL Server 命令内的 readable、visited 与 frontier 集合计算，汇聚路径不会递归枚举；集合与参数只属于本次命令，不向应用进程完整物化，不建立持久闭包或全局缓存。对象读取条件先由正式 EF 查询翻译；Link reader 的可信 SQL scope 将同一集合交集到锚点、关联端点及 hasChildren，并在计数和分页前生效。空间规则留在 Workspace，平台不认识空间类别。

新建子文件夹只接受 ENAME 与可选 Description（最多 500 字）。平台事务同时保存实体与内容 Link；父文件夹 edit 动作与完整新候选 create 动作分别裁决。创建、添加和移除按相同顺序取得跨 Host 的事务级文件夹图锁，再校验完整位置与当前父动作。添加和移除通过正式 Link 端点目录重新判定真实目标读取范围，无任何 Link 的可读独立对象仍可由各自标准 GET 候选发现；最后引用移除后仍可重加。文件夹添加另以完整存储图（包括不可见节点及跨空间关系）检查自环和多级环；同级名称沿 SQL Server 字符串比较，组合唯一索引另保证同父同类型同对象唯一。图锁覆盖并发互成环与 create/add 同级名竞争，不是权限凭证。

添加/移除只变更引用并返回受控成功回执，不修改目标实体审计。移除按当前完整位置验证实际关系父端与目标读取后只删除指定 LID，保留实体、其他入向关系及共享后代。创建提交后结果不可读或展示失败只返回不含标识的成功回执，不重放写入。普通对象编辑与删除由下述位置命令提供，任意 Link CRUD 仍不开放。

右侧消费通用 Link 查询及共享 Standard CRUD List View。公共字段目录来自 Link authoring，字段白名单、条件、授权与端点强制条件在数据库计数和分页前执行；列表以 LID 标识行，打开属性使用规范模型键及真实对象 EID：Folder 使用共享 Drawer，Part/Document 由受控 View 绑定解析正式所属路由并重新准入，不把 LID 传给实体详情。对象类型筛选、编码/名称关键词、名称/编码/更新时间排序、分页、列设置及刷新复用标准列表。平台树直属层与 hasChildren 消费同一正式端点 read 和 source LID 范围，仅完整读取当前文件夹层，不受右列表筛选影响。workspace.content 的来源策略与树读取支持共同委托同一纯关系读取实现；位置型 Validate 只留给正式关系列表请求，不形成 Positions 与源策略的依赖环。旧 Workspace children/path 仅为既有调用方的薄兼容投影，待消费者全部迁入 /api/trees/workspace.structure 后移除，不保留第二套查询实现或私有 search。unknown-change 交接给既有组合协调器，刷新已加载范围，读取故障不改变已提交事实。

## 复制、剪切引用与根接收

新进入空间根的所有内容只能是文件夹：文件夹内创建 Part/Document、添加引用及批量粘贴都在服务端统一拒绝根目标；Document 上传预检在任何文件 IO 前拒绝根位置。普通文件夹仍接收 Folder、Part、Document。已有根 Part/Document 不自动删除或迁移，继续沿原读取、编辑、下载、移除及复制到普通文件夹路径；没有 Workspace 位置的所属对象直接创建不受此准入规则影响。

树复制绑定当前普通文件夹出现位置的单个末端 LID；行菜单绑定实际点击行；页工具栏 `copySelected` 只采样当前页 1–100 个不同 LID，必须同父。`POST /api/workspace/content/clipboard/validate` 只接受 copy 模式、完整 sourcePosition 及 linkIds；重验完整祖先 read、源关系端点及对象所属 copy 数据资格，不要求源父 edit。三个 owner 分别声明 `workspaceCopy`、`partCopy`、`documentCopy`，均 requiresRead；行为分别是 `data.workspace.copy`、`data.part.copy`、`data.document.copy`，词条由各 owner 互补贡献到既有 DataScope 词典。只读预检在本请求复用已有 scoped 连接，结束或取消归还自己打开的连接，不建立事务或缓存授权结果。

剪切使用独立的 `POST /api/workspace/content/clipboard/cut/validate` 和正式 `workspace.folder.cut` 功能位置，只接受 cut 模式。预检不写数据，重验完整源位置、源父 edit、端点 read 和对象既有 edit，不要求 copy 或 delete。copy 的功能位置不能授权 cut 预检，前次预检不是粘贴凭证。

`POST /api/workspace/content/paste` 额外接受完整 targetPosition；使用自己的正式 paste 功能，在一次文件夹图锁事务内按 mode 重验全部源资格、目标链与目标父 edit，再按 snapshot 次序新增引用。cut 同时精确删除指定源 LID，其他入向关系保留；copy 保留源 LID。Folder 沿 `WorkspaceTreeOrder.NextOrdinalAsync` 追加，Part/Document 的 TreeOrdinal 保持 NULL。新引用有新 LID，EID、非空文件夹子内容、Document 版次、主附件关联、Files usage 和字节保持不变。重复、同名、环路、批内冲突、并发竞争及提交前失败整批回滚；输入中的客户端值不替代任何服务端重验。cut 到同一真实父在全部资格重验后返回 `{ succeeded: true, noChange: true }`，无关系、次序或审计写入；此判断先于新落入的根限制，因此旧根 Part/Document 也兼容。copy 同父仍按重复引用拒绝。

成功 copy 逐条通过 Audit.Contracts 登记 `workspace.reference.copied`，资源为 `workspace.content`，目标为本次服务端新 LID；字段只保留来源 LID、来源父以及新关系 PEID、ChildModelKey、CEID 的真实标识。可信主体、UTC 时间和 Trace 由既有 Audit 取得，记录与引用由原平台事务统一保存提交；预检、资格失败、同父 copy 拒绝及事务回滚不留下业务记录。字段标签由 Workspace 互补贡献到既有 BusinessChangeView 中英词典，不依赖对象名称或新履历页面。

剪贴板只属于当前树/页面实例，由 Workspace 私有类型化会话拥有，右侧列表与树菜单共享。切换目录保留；主体变化、reload 或实际销毁清空。copy/cut 预检成功才替换旧 snapshot；迟到响应不能覆盖新选择或复活已结束实例。已知失败保留可修正 snapshot，成功 copy/paste 保留；cut 粘贴确认成功（含 noChange）清空。paste 结果未知时明确提示核对源与目标并锁住旧 snapshot，不自动重放，缺失合法成功回执也按未知处理。已提交后的刷新失败只重试读取。clearClipboard 是复用正式 copy 功能资格的本地动作，没有虚构 HTTP 清空端点；明确清空或成功新 copy/cut 可替换未知状态。同一实例会话的状态同时用于树菜单、列表菜单及工具栏，cut snapshot 存在时在原剪切与粘贴动作标签持续显示“待移动”，不派生新功能位置；预检/粘贴期间其他写入和输入开启入口禁用。菜单预览不准备输入或发送写入。

批量移除使用 `POST /api/workspace/content/references/remove`，输入为完整 position 与当前页 1–100 个不同 LID；正式 `removeSelected` 确认弹层明确保留对象及文件。单条 DELETE 兼容入口委托同一内核：图锁事务内重验完整位置、父 edit 和端点 read 后只删除这些 LID，不要求对象 edit/delete/copy，不调用对象删除或 Files release；最后引用及非空文件夹引用均可移除，一条失败整批回滚。成功后按 unknown-change 刷新同一真实父的已加载出现位置与列表。

## 对象编辑与删除

普通文件夹树节点贡献已有编辑与移除引用动作，右侧内容 List View 通过 `rowContextMenu` 引用已有编辑、删除与移除。根仍使用 `folder` nodeType 和既有功能位置；Provider 以 `contextMenuKeys: ["paste", "clearClipboard"]` 收窄根菜单，根不能复制或排序。`workspace.folder.reference` 从完整节点位置取末端 LID 作为动作 recordId，其源父位置为链去掉末端 LID；空链根及非法链拒绝。`referenceObjectId` 只供共享动作读取真实 Folder EID，并保留动作 row 的 LID/ObjectModelKey；它不进入写入 DTO，也不替代 edit-values、PUT 或移除命令对真实关系端点和完整源父位置的重验。普通实体详情继续按 EID 读取。

内容行编辑通过当前完整位置与 LID 重新读取可编辑字段，在共享 Drawer 修改。文件夹编辑名称及说明；零组件、图文档编辑编码、名称及说明。服务端从真实关系推导 EID 和模型，不接受客户端替换类型；完整位置、父文件夹 edit、端点 read 与目标 edit/delete 数据权限每次重验。空间根继续禁止编辑和删除。

Part/Document 通过所属标准 update/delete 行为执行字段规则、编码唯一性和可信审计；其普通 HTTP 更新/删除仍不开放。Workspace 文件夹改名在同一图锁下检查所有真实直接父文件夹的同名冲突，不返回不可见路径。实体编辑不改变 Link 审计。

删除对象会原子清理其全部入向引用；移除引用仍只移除当前 LID。批量入口只提交当前页勾选的 1–100 个不同 LID，混合类型在同一对象操作事务中执行，任一权限拒绝或写入失败整批回滚。任何删除前先检查整批文件夹是否非空；非空文件夹必须先清空，不能借同批其他对象的删除绕过。引用清理与同名冲突在 SQL 执行，不完整物化入向关系。边界与回退见 [ADR-0054](../../adr/0054-workspace-object-edit-delete-and-list-selection.md)。

## 文件夹树次序

`WorkspaceFolderContent.TreeOrdinal` 是服务端拥有的 nullable Int64，只对文件夹关系赋值；Part/Document 保持 NULL。`(PEID, TreeOrdinal)` 的过滤唯一索引保护非空次序。旧关系通过模块 migration 按真实父分组、文件夹名称和 LID 排名回填，保留旧数据；初始化后改名不重新排列。所有文件夹 create/add 与后续引用命令在同一图锁事务内消费 `WorkspaceTreeOrder.NextOrdinalAsync`，追加到真实父分支末尾；溢出明确拒绝并回滚，不形成业务数量上限。

直属树读取在数据库交集正式读取范围，按 TreeOrdinal、LID 返回文件夹并计算可读子存在性。右侧列表仍消费自己的 Link 查询排序、筛选与分页，不能按树次序返回，也不公开次序列。树菜单通过 List View 的非呈现 `featureActions` 引用 `moveUp`/`moveDown`，正式功能位置为 `contextMenu`、`staticScope=folder`，共用 `workspace.folder.reorder` 行为；根不贡献这些菜单，搜索结果不能作为排序入口。

`POST /api/workspace/content/tree-order` 只接受 `parentPosition`、单个 `linkId`、`direction(up|down)`，不接受未知成员、重复成员、任意邻居或序号。图锁后重验空间、完整父链、父 edit、所选与相邻文件夹 read；所选文件夹 edit 不是排序条件。交换两条可读关系的原次序，隐藏关系不变。唯一索引换位的临时 NULL 仅在事务内部存在；最终两条关系使用同一操作的 LUpdatedBy/LUpdatedAt，实体审计字段不变。共享父的全部出现位置同步，不同父的入向关系独立。

首尾返回 `moved=false` 与 `boundary=first|last`，没有关系或审计写入。正式 handler 消费为中英文提示和已确认 `noChange` 回执，不刷新树或列表；正常换位通过既有 unknown change 刷新已加载出现位置与当前列表。回退关闭排序功能位置或 Workspace 导航，保留关系数据；已提交 migration 不自动 Down，恢复旧 frontier 使用经过验证的迁移前备份。

## 直接引用文件夹

Workspace 在自己的反向 List View 声明三类正式详情的只读贡献，复用共享详情入口、PageRef 与标准列表，Part/Document 不反向依赖 Workspace。目标详情先执行所属读取准入；入口另要求 Workspace 导航资格，其缺失不影响原详情。反向 GET 使用同一 workspace.content 协议，不要求或接受出现位置；锚点及父文件夹各自执行正式读取与强制范围，父列筛选、计数、稳定排序和分页均在 SQL 内完成。

列表按直接入向 LID 返回，保留原始父子引用，公共列及显示取父文件夹；不包含私人入向空间、面包屑或未经验证的跳转。父行只能重新打开父对象属性，不能自动选择树上的任一出现位置。贡献 Modal 的查询状态和取消生命周期按真实对象及贡献身份隔离，关闭后保留原详情与位置。

## 非目标与回退

尚不提供部门/项目空间、空间管理、代看个人空间或一次导出全部根路径。

回退优先关闭 Workspace 导航、模块及其显式资源贡献，保留已创建的空间/文件夹数据；不得自动 Down、清表或让旧 Host 读取超出其 migration frontier 的数据库。
