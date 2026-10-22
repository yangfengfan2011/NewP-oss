# Identity Foundation 纵向验收

`IdentityFoundation_CompletesEveryScenario` 现有入口 使用全新 disposable SQL Server catalog、正式维护命令与开发启动脚本、正式 HTTP API 和真实 Chrome，覆盖 GitHub Issue #41 的端到端验收范围。runner 不提供临时端点，不直接写业务表，也不以替身结果作为通过依据。

## 运行

Runner 会依次读取 `NEWP_ACCEPTANCE_SQLSERVER_ADMIN_CONNECTION`、`NEWP_DEV_SQLSERVER_CONNECTION_STRING` 和本地 `apps/host-api/appsettings.Development.json`。连接身份必须具备创建和删除 catalog 的权限；连接值不会写入证据。已配置本地 Development 连接时可直接运行：

```powershell
pwsh.exe -NoLogo -NoProfile -File scripts/accept.ps1 -Scenario IdentityFoundation_CompletesEveryScenario
```

需要临时覆盖时只在当前进程设置 `NEWP_ACCEPTANCE_SQLSERVER_ADMIN_CONNECTION`，无需修改受跟踪配置。Runner 始终切换到 `master` 并创建本轮唯一的 disposable catalog，不会把配置中的应用 catalog 作为验收数据库。

该现有入口仍包含下述六类行为，只在本次变化确实需要该范围时显式选择，不作为父级收尾或所有 Identity 功能的默认总验收。验收场景应按证据缺口保持有界；旧 `-Gate IdentityFoundation` 接口已退役。脚本设置测试环境开关并使用精确过滤器，核对实际执行结果。失败即测试失败，异常消息与栈就是诊断，不需要另找结论文件。排错用的现场快照写在 `.codex-temp/acceptance/identity-foundation/<run-id>/`。

## 六类证据

| 场景 | 证据 | 关键断言 |
| --- | --- | --- |
| `identity-foundation.preflight` | `identity-foundation/preflight.json` | 新 catalog 在 bootstrap 前返回 Not Ready；正式离线命令只生成一个本地管理员和一次性临时凭据；首次改密后 Host/Shell Ready |
| `identity-foundation.api` | `identity-foundation/api.json` | 账号全生命周期、一次性秘密、受限与普通会话、多会话撤销、Person 无组织与永久绑定、停用联动、四类实体审计 |
| `identity-foundation.browser` | `identity-foundation/browser.json`；开启呈现采集时另有 `browser.png` | 真实浏览器创建账号、秘密关闭后不可再见、首次改密路由保护与普通会话建立 |
| `identity-foundation.restart` | `identity-foundation/restart.json` | 正常 Host 重启后旧 Cookie 仍有效，SQL Server 状态和既有 key ring key 保留 |
| `identity-foundation.security` | `identity-foundation/security.json` | 防伪稳定失败、单次安全重试、未知结果不重试、Cookie/outage/key ring、限流/可信代理、required built-ins、旧链不可达 |
| `identity-foundation.persistence` | `identity-foundation/persistence.json` | Identity、Authentication、Organization 的 SQL Server smoke、并发/冲突/过期/purge、阶段故障注入与事务回滚 |

每轮只允许清理由本轮 ownership 文件确认的进程、key ring 和 catalog，`remainingDisposableCatalogs` 必须为 `0`。归属校验失败时资源会被保留并让测试失败，交由人工确认这套库/目录到底属于谁。

## 已验证基准

2026-08-10 使用 runner `fc0b684ad043db5f300ff97e` 完成六场景，`result.json` verdict 为 `passed`；真实浏览器截图、Host restart、三组 SQL Server integration/fault-injection、ownership cleanup、tree verification 和 secret scan 全部通过。
