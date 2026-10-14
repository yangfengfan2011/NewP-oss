# 本地开发环境

本页只记录本地开发约定。工具精确版本以项目文件为事实源：后端读取各 `.csproj`，Node.js 与 pnpm 读取 `frontend/package.json`；初始化脚本会校验这些约束。

## 初始化与启动

首次拉取或 lockfile 变化后运行：

```powershell
.\scripts\bootstrap.ps1
```

日常启动和停止统一使用受控脚本；它们只管理当前 NewP worktree 拥有的进程：

```powershell
.\scripts\dev.ps1
.\scripts\stop-dev.ps1
```

Host 与 Shell 默认地址分别为 `http://127.0.0.1:5000` 和 `http://127.0.0.1:3000`。开发服务器只监听 IPv4 loopback；访问 `localhost` 时 Shell 会重定向到 `127.0.0.1`，避免系统优先尝试未监听的 `::1`。启动失败时先读取 `.codex-temp/dev/runs/<run>/result.json`、`stages.jsonl` 和对应 stderr 日志。

Files 要求显式绝对私有 `Files:StorageRoot`。`dev.ps1` 优先沿用进程中已设置的 `Files__StorageRoot`，否则固定使用当前 worktree 的 `.codex-temp/dev/files`；`stop-dev.ps1` 只停所属进程，保留该内容目录。更换 root 前先停止开发 Host，重启必须继续使用同数据库、同 root。直接 `dotnet run` 或正式发布入口由调用者显式配置该值，模块不按发行物路径、`AppContext` 或 `wwwroot` 产生默认 root。回退新入口仍保留数据库文件身份与内容，不把代码回退当作字节恢复。

## 本地 SQL Server

开发 Host 优先使用 `NEWP_DEV_SQLSERVER_CONNECTION_STRING`，未设置时由 Host 正常加载 `apps/host-api/appsettings.Development.json`。已经配置本地连接时无需为每次启动重复设置环境变量。

当前仓库处于[实验基线期](../adr/0042-development-uses-a-resettable-migration-baseline.md)。日常开发、定向 API 验证和浏览器 smoke 可以复用仍匹配当前迁移基线的开发 catalog，不为每次运行新建数据库。相互独立的验证运行使用各自的数据前缀并在运行结束后清理；属于同一连续交付链且需要共享状态的任务使用交付链级前缀，保留到整条交付链完成或放弃时统一清理。这些数据不构成跨迁移基线的数据保留承诺；基线整体替换后，旧开发 catalog 不享有升级兼容，不能用 Required 字段回填或放松 Persistence Gate 跨越已被替换的基线。若连续任务需要保留当前有效 history 前缀中的数据，按 [Required 字段生命周期](../adr/0043-required-fields-use-explicit-write-and-migration-lifecycles.md) 追加迁移；当前不使用数据库永久 `DEFAULT`。

Host 不会因为 Development 环境或 `PERSISTENCE_DATABASE_REBUILD_REQUIRED` 自动删库。需要重建时，先运行 `.\scripts\stop-dev.ps1`，再核对连接指向的精确 catalog、NewP ownership marker、repository/worktree identity、数据可丢弃性和保护状态；只有全部一致且未受保护时，才通过受信任的数据库管理入口显式删除并新建空 catalog，随后重新启动 Host 让正式 migration 链创建 schema。不得重建 Acceptance、验收证据、显式保护或来源不明的数据库。

自动化测试默认使用独立的 `NewP_Test`，不使用开发 catalog，也不为普通回归每次创建临时库。共享实现位于 `tests/TestSupport/SqlServer/ReusableSqlServerDatabase.cs`，适用于 Identity、Authentication、Organization 的普通 SQL 集成测试、业务 HTTP fixture、Host 重启、插件路由与功能目录测试，以及 Personnel / Authorization 常规 Acceptance。

连接优先使用 `NEWP_TEST_SQLSERVER_CONNECTION_STRING`（必须明确选择 `NewP_Test`），其次取 `NEWP_DEV_SQLSERVER_CONNECTION_STRING`，最后取 `apps/host-api/appsettings.Development.json`。后两项只复用服务器与登录身份，数据库强制切换到 `NewP_Test`。不需要另存一份密码，也不读取 `.env`。

专用库首次不存在时创建并写入数据库级 `NewP.AutomatedTests=disposable-schema` 标记，以后保留同一个 catalog。已有同名但无标记的数据库会拒绝接管。测试使用 SQL Server 会话应用锁跨进程独占整个 fixture；等待超过 15 分钟失败，不另建临时库绕过。各 fixture 的模型子集不同，因此当前在租用与归还时重置测试表、外键及序列，由原 fixture / Host 重新建表或执行迁移；这是 catalog 复用，不是 schema 或测试数据保留。测试必须先关闭自己的 Host 和数据库连接，再归还租约；SSMS 等工具的空闲连接不阻止复用，清理不强制踢掉连接，活动事务冲突由 SQL Server 报错或超时。验收归属检查或停止失败时保留本次 schema，仅关闭持锁连接；失败现场和异常中断留下的测试表会在下一次成功租用时清理。

只有必须独立控制 catalog、迁移历史、数据库级状态或启动故障的测试继续使用一次性库：Persistence SQL smoke、migration-manifest 纵向重放、Organization 前向迁移、Host 初始缓存故障、IdentityFoundation / ExampleRequiredField Acceptance，以及 production cold-start。普通事务回滚与并发业务操作可以在专用库的独占租约内验证。新增临时库 fixture 必须注明专用库不能满足的具体前提；“自动化测试”或“正式验收”本身不构成新建理由。

仓库根目录的 `.env` 被 Git 忽略，但 `scripts/dev.ps1` 当前不会自动解析它。`NEWP_DEV_ADMIN_ECODE` 与 `NEWP_DEV_ADMIN_PASSWORD` 是可选的应用管理员登录凭据，只用于认证 smoke，不是 SQL Server 用户名和密码；使用前必须由受信任的本地入口注入当前进程环境。数据库连接仍由上述连接字符串来源提供。

需要一次性库的 Acceptance 按以下顺序解析管理连接：

1. `NEWP_ACCEPTANCE_SQLSERVER_ADMIN_CONNECTION`
2. `NEWP_DEV_SQLSERVER_CONNECTION_STRING`
3. `apps/host-api/appsettings.Development.json` 的 `ConnectionStrings:SqlServer`

这些隔离场景只复用服务器与登录身份，将管理连接切换到 `master`，随后创建并清理本轮唯一的临时 catalog；配置身份必须具备建库和删库权限。普通测试需要租约、专用库 schema 清理和建表权限，首次创建专用库还需要建库权限。连接值不进入命令参数、证据或日志。旧模块级 `NEWP_*_SQLSERVER_ADMIN_CONNECTION` 只保留给尚需一次性库的场景，已接入共享租约的测试统一使用上述专用测试连接规则。

## 本地管理员

全新数据库不会获得固定明文密码。目标空数据库必须已经存在；先停止开发服务，再在交互式终端执行离线 bootstrap：

```powershell
.\scripts\bootstrap-local-admin.ps1
```

脚本会为维护进程设置 Development 环境，并把 `NEWP_DEV_SQLSERVER_CONNECTION_STRING` 映射到 Host 标准连接配置；不会输出连接值。命令只显示一次随机临时凭据；首次登录后必须修改。已有数据库继续使用其中现存的本地管理员凭据；实验基线重建后的空数据库需要重新执行 bootstrap。

## 验证入口

- 开发循环：运行当前票据涉及的项目或过滤测试。
- 仓库测试：`.\scripts\verify.ps1 -Quick`。
- 全量验证：`.\scripts\verify.ps1`。
- 纵向验收：`.\scripts\accept.ps1 -Gate <Gate>`，等价于设置 `NEWP_RUN_ACCEPTANCE=1` 后 `dotnet test tests/acceptance/Acceptance.Tests`。闸门默认跳过，不会拖慢日常 `dotnet test`。

专用测试库的最小回归（包括连续复用与并发租约拒绝）：

```powershell
$env:NEWP_RUN_TEST_DATABASE_INTEGRATION = '1'
dotnet test tests/HostApi.Tests/HostApi.Tests.csproj -c Release --filter FullyQualifiedName~ReusableSqlServerDatabaseTests
Remove-Item Env:NEWP_RUN_TEST_DATABASE_INTEGRATION
```

回退本次测试连接策略时恢复相关 fixture 和共享测试支撑的代码即可；`NewP_Test` 可保留为空库，无需删除或修改开发库。不要把业务数据或手工保留数据放入专用测试库。

完整验证结果位于 `.codex-temp/verify/runs/<run>/result.json`，每个阶段有独立日志；不需要从截断的终端输出推断结果。

## 推荐阅读

- 仓库规则：[../../AGENTS.md](../../AGENTS.md)
- 架构总览：[../architecture/architecture.md](../architecture/architecture.md)
