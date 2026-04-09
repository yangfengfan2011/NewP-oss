# Tests

平台测试与验证入口。

## 结构

- `Audit.Tests`
- `Authorization.Tests`
- `ConfigCenter.Tests`
- `Files.Tests`
- `Kernel.Tests`
- `Modeling.Tests`
- `ModuleTemplateSample.Tests`
- `Persistence.Tests`
- `integration/`

## 常用命令

```powershell
dotnet test
pnpm --dir .\frontend test
```

## 多数据库持久化 Smoke

`Phase 6.5` 提供三套可复跑的 PowerShell 入口：

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\integration\persistence\Run-SqlServerSmoke.ps1
powershell -ExecutionPolicy Bypass -File .\tests\integration\persistence\Run-OracleSmoke.ps1
powershell -ExecutionPolicy Bypass -File .\tests\integration\persistence\Run-MySqlSmoke.ps1
```

通用脚本会执行：

1. `dotnet tool restore`
2. 构建 `HostApi`
3. 运行 `ConfigCenter.Tests`
4. 运行 `Authorization.Tests`
5. 调用对应迁移项目执行 `dotnet-ef database update`

可通过环境变量覆盖默认连接串：

- `ConnectionStrings__SqlServer`
- `ConnectionStrings__Oracle`
- `ConnectionStrings__MySql`

如果你还想看：

- 宿主如何启用平台持久化
- Provider 如何切换
- migration 项目如何使用

请继续阅读：

- `docs/developer/persistence-usage-guide.md`
