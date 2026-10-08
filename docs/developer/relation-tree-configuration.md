# 配置标准关系树

已有 Tree View 可设置 `providerKey: "platform.relations"`，无需为该树注册前端 Provider 或实现四个查询 API。完整可运行配置见 [OrganizationStructure.view.json](../../modules/Organization/metadata/views/OrganizationStructure.view.json)。平台从活动内建模块的已签发 metadata 读取配置；普通页面引用和节点内容仍使用现有 PageRef、输入解析器及资源动作。

## 节点配置

在 `relationSource.nodes` 中为每个 `nodeTypes[].key` 声明一项：

| 字段 | 含义 |
| --- | --- |
| `key` | 与节点类型一致；节点身份为 `类型:主键值` |
| `resourceKey`、`model` | 同模块已注册 Standard CRUD 资源及其模型；复用查看权限和读取字段边界 |
| `idField` | 模型唯一的单字段字符串主键 |
| `labelField`、`codeField` | 显示名称与编码，也是子串搜索字段 |
| `sortField` | 字符串排序字段；随后按编码、节点类型、节点身份稳定排序 |
| `enabledField` | 可选布尔启用字段；省略视为启用，配置该字段不会自动过滤停用节点 |
| `contextFields` | 输出上下文名到模型字段的映射，保留值类型和 null |
| `parents` | 有序父关系，每项声明 `nodeType`、外键 `field`；第二项起须设置 `fallback: true` |
| `rootWhenParentNull` | 自关联模型可设置 true，使全部候选父字段为空的节点成为根；无父关系的类型天然是根 |
| `filters` | 可选静态 AND 条件，声明 `field`、`operator`、`value`；支持 `=`、`contains`、`isNull`、`isNotNull`，空值操作不需要 value |

所有字段必须在对应资源的列表读取白名单内，并存在于当前模型。身份、名称、编码和排序当前使用字符串字段；节点身份在 SQL 中最多 500 字符，按模型主键最大长度与类型前缀静态验证。模型及外键关系不从前端请求提交，不支持表名、字段名或表达式覆盖。

公司—部门配置先使用 `ParentDepartmentId` 指向部门，空值时再使用 `CompanyId` 指向公司。父键非空但目标不存在时不会尝试后备关系。复合外键的公司归属等其他分量也参加父节点匹配。

静态条件与资源 Query Customizer 产生的条件相交；后者当前可使用无分组或 AND/OR 分组的 `=`、`!=`、大小比较、`contains`、`isNull`、`isNotNull`。Query Customizer 中 `=`、`contains` 的 null 值表示未赋值的可选查询，与 Standard CRUD 一致，忽略该条件；静态 filters 不套用此规则，`isNull`、`isNotNull` 始终保留。不支持的条件失败，不退回全量读取。配置应保证可见节点的祖先也可见；过滤导致路径不完整时明确报错，不自动提升后代。

## 请求与页面输入

树页面仍须得到显式 `treeScope: { kind: "global" }`。当前配置以全局范围为基础，通过静态 filters 缩小集合；动态业务范围使用自定义 Provider。

固定服务端路由为 `/api/trees/{viewKey}/roots`、`children`、`search`、`path`：

- 所有请求带 `scope=global`。
- `children` 和 `path` 带 `nodeKey`。
- `search` 带非空 `keyword`、正整数 `page` 和 `pageSize`；标准界面每页 20 条，加载更多追加下一页。
- 未知请求字段、重复参数或无效范围拒绝执行；不能通过请求指定 model、字段或 SQL。

根与直属集合不分页，搜索分页在数据库执行。点击搜索结果会重新读取当前路径与必要祖先分支，因此搜索页大小不限制定位过程加载的同级节点数量。宽分支需要按真实规模测量。

共享树右侧在原标题上方显示当前位置面包屑，从已加载节点及父子层级按不透明 `nodeKey` 回溯，不增加网络请求或读取未展开分支。点击祖先使用与树节点选择相同的组合离开保护；节点身份、搜索、详情和 URL 规则保持各自 Provider 与页面契约。

配置静态 filters 的树在成功写入后返回 `reload` 变更：宿主自动重读根、已加载分支和当前路径，由数据库重新判定节点成员关系，仍保留新增选中及删除回退。`located` 只重读受影响分支；`unknown` 用于无法可靠解释的变更，宿主保守重读根、全部已加载分支和当前路径。分类异常或重读失败时保留刷新警告及只读重试，重试不会重复业务写入。

## 自定义业务

特殊数据来源、动态范围或复杂业务规则可保留自己的 `providerKey` 与 `TreeProviderBinding`，实现 `roots / children / search / path / resolveChange`，并在胶囊中声明该 binding。自定义 Provider 不同时声明 `relationSource`；它继续拥有数据查询、节点身份、权限、范围及变更位置解释。通用树宿主和节点 PageRef 无需替换。

标准配置当前面向内建模块、SQL Server 和已注册 CRUD 资源；不提供跨模块表访问、外部插件树配置、多父级或同级分页。决策及回退边界见 [ADR-0047](../adr/0047-standard-relation-trees-use-configured-providers.md)。

## 查询生成与性能复跑

平台按配置生成针对物理模型的 SQL；在相同读取语义下，对照专用实现验证数据库成本。父节点身份直接来自外键，`HasChildren` 使用实际子表上的 `EXISTS`。单字段外键省略额外父集合连接；复合外键仍匹配全部分量。上下文以原生列返回并在服务端组装，不在数据库逐行生成 JSON。递归结果不返回内部的循环检测字符串。

性能与语义测试使用真实 Organization 模型，在测试独占数据库建立 100、1,000、10,000 节点。先执行功能检查及预热，再交替两套实现的先后顺序，采样六轮。保留原始样本；报告中位数、范围、逻辑读取和数据库接收字节数，不能以一次往返或功能测试通过代替性能结论。该数据形状包含宽分支和深度 10 的路径，不代表任意树形状或生产并发容量。

从仓库根执行：

```powershell
$env:NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION = '1'
dotnet test tests/Organization.Tests/Organization.Tests.csproj -c Release --filter 'FullyQualifiedName~OrganizationTreeRepresentativeScaleRecordsActualSqlCost|FullyQualifiedName~RelationTree_' --logger 'console;verbosity=detailed'
```

默认使用本机 SQL Server LocalDB；已有环境可通过测试约定的 `NEWP_ORGANIZATION_SQLSERVER_ADMIN_CONNECTION` 提供连接。测试会创建并清理独占数据库。阶段测量报告与原始日志保留在 `.codex-temp`，不作为长期容量保证。
