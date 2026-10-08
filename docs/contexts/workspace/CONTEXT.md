# Workspace

Workspace 拥有公共空间、当前账号个人空间及其根文件夹。入口使用既有自定义功能声明 `workspace.spaces.access`，包含确保当前账号个人空间和读取根；它不授予普通内容操作权限。公共初始化额外要求 LocalAdmin，读取接口不创建记录。

## Language

**空间（Space）**：导航入口及强制准入范围。Public 对具备入口资格的已认证用户开放，Personal 只匹配可信当前账号 `Identity/IdentityAccount`；LocalAdmin 也不能绕过他人的 Personal 准入。

**根文件夹（Root Folder）**：空间 `RootFolderId` 唯一关联的真实 WorkspaceFolder。根与普通文件夹共表，不另建根标志；本期根不能经普通 CRUD 删除、改名或替换。

**来源空间（Origin Space）**：文件夹不可变的创建来源 `OriginSpaceId`，由服务端赋值，不作为公开详情字段。根初始化预分配空间身份，先保存根再保存空间；两次保存属于同一平台事务。

## 边界

- WorkspaceSpace、WorkspaceFolder 及 SQL Server migration stream 由模块 authoring 拥有。公共空间 NULL 主体组合与个人账号组合均参加唯一索引；RootFolderId 非空、唯一且使用 Restrict 外键。OriginSpaceId 仅索引，不形成循环物理外键。
- 初始化使用事务级 SQL Server 应用锁和数据库唯一约束，成功后重复命令复用原身份。已有主体配对或根来源不一致时拒绝，不修复或新建替代根。命令提交后读取拒绝或展示失败返回不含根标识的成功回执，不反转成功事实。
- 空间准入与 Authorization 的正式对象读取计划取交集。专用资源通过既有 Contracts 的 `DataScopeResource` 显式贡献，不为取得读取裁决而开放普通 CRUD。Authorization 拥有资源目录整合及身份、活动 owner、真实模型、主键和重复校验；Workspace 不维护另一套字段目录。
- 根详情使用真实 Folder EID，并由空间唯一归属验证其准入；非根不能借此读取。已知出现位置恢复仍同时验证 SpaceId 与 RootFolderId，不自动改选另一条路径。字段白名单与审计 raw/display 使用共享 Standard CRUD 详情服务及正式 Model planner。
- 左树使用共享 Tree/PageRef 宿主和模块 Provider，只有真实公共、个人两个根。根与普通文件夹共用标准内容列表，属性通过共享只读详情打开。Tree View 可显式关闭搜索；未声明时现有树继续开启。主体切换释放整个组合实例、选择及未完成读取。

## 直接文件夹内容

WorkspaceFolderContent 是正式 Link，父端由 authoring 固定为 Workspace/WorkspaceFolder，子端由必填 ChildModelKey 判别；允许组合只来自 linkedTypes。正式目录登记 Workspace/WorkspaceFolder、Part/Part、Document/Document 三个真实端点；读取/显示契约来自其所属模块，Workspace 显式依赖 Part 与 Document。LID、PEID、CEID 与关系审计由服务端填写；关系不保存 SpaceId、父位置、名称或对象审计影子值。

出现位置由 SpaceId、RootFolderId 与有序 LID 链组成。位置解析批量读取该链，核对空间准入、唯一根归属、父子衔接、无重复节点及每层对象读取资格。正向内容查询、树直属层、已知路径恢复和全部内容写入均执行该检查；可读的裸 Folder EID 不能代替出现位置。

文件夹独立详情、单选候选与 Link 端点共享强制范围：对象可读，且来源空间可准入，或存在一条从可准入根出发、祖先皆可读的路径。OriginSpaceId 不公开且不可修改；私人来源的普通文件夹可以通过公共引用共享，移除最后公共路径后重新计算范围。来源主体仍能发现失去全部引用的文件夹并重新添加，所有正式空间根排除于候选。候选以 EID 去重，Link 查询仍逐 LID 返回。

普通文件夹及根详情使用固定公开投影，拒绝字段选择等未声明 query 参数；候选只允许关键词和标准页码/页大小，内部来源不能通过请求字段扩展到响应。

可达范围用 SQL Server 命令内的 readable、visited 与 frontier 集合计算，汇聚路径不会递归枚举；集合与参数只属于本次命令，不向应用进程完整物化，不建立持久闭包或全局缓存。对象读取条件先由正式 EF 查询翻译；Link reader 的可信 SQL scope 将同一集合交集到锚点、关联端点及 hasChildren，并在计数和分页前生效。空间规则留在 Workspace，平台不认识空间类别。

新建子文件夹只接受 ENAME 与可选 Description（最多 500 字）。平台事务同时保存实体与内容 Link；父文件夹 edit 动作与完整新候选 create 动作分别裁决。创建、添加和移除按相同顺序取得跨 Host 的事务级文件夹图锁，再校验完整位置与当前父动作。添加和移除通过正式 Link 端点目录重新判定真实目标读取范围，无任何 Link 的可读独立对象仍可由各自标准 GET 候选发现；最后引用移除后仍可重加。文件夹添加另以完整存储图（包括不可见节点及跨空间关系）检查自环和多级环；同级名称沿 SQL Server 字符串比较，组合唯一索引另保证同父同类型同对象唯一。图锁覆盖并发互成环与 create/add 同级名竞争，不是权限凭证。

添加/移除只变更引用并返回受控成功回执，不修改目标实体审计。移除按当前完整位置验证实际关系父端与目标读取后只删除指定 LID，保留实体、其他入向关系及共享后代。创建提交后结果不可读或展示失败只返回不含标识的成功回执，不重放写入。普通 rename/delete 和任意 Link CRUD 均未开放。

右侧消费通用 Link 查询及共享 Standard CRUD List View。公共字段目录来自 Link authoring，字段白名单、条件、授权与端点强制条件在数据库计数和分页前执行；列表以 LID 标识行，打开属性使用规范模型键及真实对象 EID：Folder 使用共享 Drawer，Part/Document 由受控 View 绑定解析正式所属路由并重新准入，不把 LID 传给实体详情。对象类型筛选、编码/名称关键词、名称/编码/更新时间排序、分页、列设置及刷新复用标准列表。树直属层与 hasChildren 共用查询规划，仅完整读取当前文件夹层，不受右列表筛选影响。unknown-change 交接给既有组合协调器，刷新已加载范围，读取故障不改变已提交事实。

## 非目标与回退

尚不提供部门/项目空间、空间管理、代看个人空间、反向位置 UI 或完整多父状态恢复。后续能力继续在已确认父规格中逐片接入。

回退优先关闭 Workspace 导航、模块及其显式资源贡献，保留已创建的空间/文件夹数据；不得自动 Down、清表或让旧 Host 读取超出其 migration frontier 的数据库。
