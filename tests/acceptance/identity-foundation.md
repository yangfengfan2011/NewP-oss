# Identity Foundation 纵向验收

`IdentityFoundation` gate 使用全新 disposable SQL Server catalog、正式维护命令与开发启动脚本、正式 HTTP API 和真实 Chrome，覆盖 GitHub Issue #41 的端到端验收范围。runner 不提供临时端点，不直接写业务表，也不以替身结果作为通过依据。

## 运行

先为当前进程提供可创建和删除 catalog 的 SQL Server 管理连接；连接值不会写入证据：

```powershell
$env:NEWP_ACCEPTANCE_SQLSERVER_ADMIN_CONNECTION = '<sql-server-admin-connection>'
pwsh.exe -NoLogo -NoProfile -File scripts/accept.ps1 -Gate IdentityFoundation
```

成功时进程退出码为 `0`，并输出 `ACCEPTANCE_RESULT`。产物位于 `.codex-temp/acceptance/runs/<timestamp>-<run-id>/`；`result.json` 是汇总事实，`events.jsonl` 和 `evidence-index.json` 提供事件与文件索引。

## 六类证据

| 场景 | 证据 | 关键断言 |
| --- | --- | --- |
| `identity-foundation.preflight` | `identity-foundation/preflight.json` | 新 catalog 在 bootstrap 前返回 Not Ready；正式离线命令只生成一个本地管理员和一次性临时凭据；首次改密后 Host/Shell Ready |
| `identity-foundation.api` | `identity-foundation/api.json` | 账号全生命周期、一次性秘密、受限与普通会话、多会话撤销、Person 无组织与永久绑定、停用联动、四类实体审计 |
| `identity-foundation.browser` | `identity-foundation/browser.json`、`browser.png` | 真实浏览器创建账号、秘密关闭后不可再见、首次改密路由保护与普通会话建立 |
| `identity-foundation.restart` | `identity-foundation/restart.json` | 正常 Host 重启后旧 Cookie 仍有效，SQL Server 状态和既有 key ring key 保留 |
| `identity-foundation.security` | `identity-foundation/security.json` | 防伪稳定失败、单次安全重试、未知结果不重试、Cookie/outage/key ring、限流/可信代理、required built-ins、旧链不可达 |
| `identity-foundation.persistence` | `identity-foundation/persistence.json` | Identity、Authentication、Organization 的 SQL Server smoke、并发/冲突/过期/purge、阶段故障注入与事务回滚 |

每轮只允许清理由本轮 ownership 文件确认的进程、key ring 和 catalog。最终 `cleanup.status`、gate `cleanupStatus` 必须均为 `completed`，`remainingDisposableCatalogs` 必须为 `0`；任一场景失败使用对应的 `ACC_IDENTITY_FOUNDATION_*_FAILED` 稳定失败码并保持 gate 失败。

## 已验证基准

2026-08-10 使用 runner `fc0b684ad043db5f300ff97e` 完成六场景，`result.json` verdict 为 `passed`；真实浏览器截图、Host restart、三组 SQL Server integration/fault-injection、ownership cleanup、tree verification 和 secret scan 全部通过。
