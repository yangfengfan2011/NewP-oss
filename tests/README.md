# 测试与验收入口

测试项目以 `ICP-Platform.sln`、`tests/**/*.csproj` 和 `frontend/package.json` 为事实源，不在本文维护项目枚举副本。

## 开发循环

优先运行本次变更涉及的项目或过滤测试：

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter <测试名或类别>
corepack pnpm --dir frontend test -- <测试过滤条件>
```

## 仓库级入口

```powershell
.\scripts\verify.ps1 -Quick
.\scripts\verify.ps1
```

`verify.ps1` 提供仓库级检查，没有 CI 兜底；按实际影响或明确交付约定选择，局部改动优先定向验证。`-Quick` 的实际跳过范围以脚本为准。每个阶段的结果与日志写入 `.codex-temp/verify/`；未传 `-AcceptanceScenario` 不运行验收。

## Acceptance

```powershell
.\scripts\verify.ps1 -AcceptanceScenario WorkspaceSmoke_UsesFixedRelease
# 或
.\scripts\accept.ps1 -Scenario WorkspaceSmoke_UsesFixedRelease
```

Acceptance Gate 是具体有界验收场景，不是模块功能全集。通过 `-Scenario` 指定 `AcceptanceGateTests` 中的精确方法名，也可显式列出有限场景；用 `-Presentation` 按需采集成功呈现。脚本启用对应测试的环境开关并核对所选测试实际通过。旧 `-Gate`、`-AcceptanceGate`、`All` 与通配入口已退役并明确拒绝。SQL Server、冷启动、建库、迁移、恢复和权限边界测试分别按变化选择，不附加到页面冒烟或父级收尾。
