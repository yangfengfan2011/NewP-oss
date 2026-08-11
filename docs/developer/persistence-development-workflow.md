# 持久化开发流程

当前持久化主链路是：候选 Canonical Module Plan → Compiled Persistence Plan → 共享运行时模型与模块自有 provider migration streams。`PlatformPersistenceModelCatalog` 和三个全局 migration 项目只保留为 Legacy Baseline 兼容事实，不是新增模块的默认接入点。

开始前先阅读[Platform Persistence Context](../contexts/platform-persistence/CONTEXT.md)。

## 新增或修改模块模型

1. 在已确认设计中写清模型 owner、物理表、provider 范围、迁移前沿、失败信号和回滚思路。
2. 修改 `modules/<ModuleId>/metadata/models/*.model.json`；模块 metadata 是模型 authoring 真源。
3. 运行模型校验和 DTO 同步，不手改 `DTOs/Generated/*.g.cs`。
4. 在模块自有 migration 项目中增加不可变的线性迁移流，例如 `server/modules/<ModuleId>/Migrations.SqlServer/`。
5. 让 migration manifest、target model digest 与模型投影一致，并通过候选计划编译。
6. 补充模块、持久化和 Host readiness 的定向测试。

首个公开外部插件 profile 只支持 SQL Server。Oracle 或 MySQL 的新增迁移必须有单独设计与真实 provider 验证，不能因为 Legacy Baseline 存在三个 provider 项目就机械生成三份迁移。

## 迁移规则

- 正常激活只自动执行 safe-expand：创建 schema/table/index，或增加 nullable/有安全默认值的列。
- `Down`、Rename、Drop、破坏性 Alter 和任意 SQL 不进入普通插件激活路径。
- 内建模块的旧基线切换必须使用已验证的 built-in provenance；外部模块不能自报破坏性权限。
- 数据库超前于旧活动快照时，旧版本仍需兼容已提交的 safe-expand 前缀；失败不自动回滚数据库。
- migration 成功后仍要等待整个插件候选原子激活，不能单独写入活动状态。

## 本地连接

开发与定向测试可以直接使用已经配置的本地 SQL Server 连接：

1. `NEWP_DEV_SQLSERVER_CONNECTION_STRING`
2. `apps/host-api/appsettings.Development.json` 的 `ConnectionStrings:SqlServer`

Acceptance 会复用该服务器和登录身份，但创建本轮唯一的临时 catalog；连接值不得进入命令参数、日志或提交内容。

## 验证

```powershell
# 同步受管 DTO
dotnet run --project .\server\tools\model-dto-sync\ModelDtoSync.csproj -- .
dotnet run --project .\server\tools\model-dto-sync\ModelDtoSync.csproj -- --check .

# 定向测试后执行仓库冻结验证
.\scripts\verify.ps1
```

涉及真实 DDL 时，还必须从空 catalog、已有受支持基线、失败重试和 Host restart/readiness 四个入口验证；不能用生成了 migration 文件代替数据库证据。
