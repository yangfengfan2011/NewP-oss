# 持久化开发流程

本文档面向仓库内部开发者，说明在当前仓库里新增或修改一个持久化模块时，推荐按什么顺序推进，以及哪些做法属于正常路径。

如果你还不熟悉当前 API 和配置项，建议先看：

- [`persistence-public-api.md`](./persistence-public-api.md)
- [`persistence-usage-guide.md`](./persistence-usage-guide.md)

## 1. 一条推荐主线

当前推荐顺序是：

```text
业务边界 -> 模型语义 -> EF 适配 -> 宿主接入 -> 迁移 / Smoke -> 文档收口
```

不要从“先写一堆 ORM 映射”开始，也不要从“先改宿主和 appsettings”开始。

## 2. 第一步：先定义业务边界

先明确：

- 模块要持久化哪些业务对象
- 哪些是独立实体，哪些只是关系对象
- 仓储契约希望暴露哪些查询和写入能力

这个阶段主要关注：

- 模块 `Models`
- 模块 `Repositories`
- 模块 `Services`

目标是先把“业务需要什么数据能力”说清楚，而不是直接落到具体数据库表结构。

## 3. 第二步：定义模型语义

模型语义层负责表达：

- `Entity` / `Link`
- 字段
- 主键
- 审计字段
- 索引
- 唯一约束

当前相关目录：

- `server/platform-modeling`
- `server/modules/<ModuleName>/Persistence/Descriptors`

首批示例：

- `server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelDefinitions.cs`
- `server/modules/Authorization/Persistence/Descriptors/AuthorizationModelDefinitions.cs`

这个阶段的原则是：

- 先把结构性语义写清楚
- 不在模型语义层写 Provider 分支
- 不把数据库方言细节抬到业务层

如果模型定义有新增或变更，应同步考虑：

- 主键是否存在
- 字段是否重复
- 索引是否引用了真实字段
- `Entity` / `Link` 是否选对

## 4. 第三步：实现 EF 适配

当模型语义稳定后，再补 EF Core 适配实现。

当前主要目录：

- `server/platform-persistence-efcore`
- `server/modules/<ModuleName>/Persistence/Ef`

你通常会做这些事情：

- 编写或修改 EF 仓储实现
- 让仓储基于 `EfPlatformDbContext` 或持久化抽象工作
- 必要时使用 `ISqlQueryExecutor` 承接受控 SQL 查询

这个阶段不建议：

- 在业务服务里直接拼 SQL
- 在业务层判断 `SqlServer` / `Oracle` / `MySql`
- 绕开仓储契约直接把 `DbContext` 暴露成业务主入口

## 5. 第四步：接宿主与组合层

如果模型定义已经变化到会影响平台组合，就要确认组合层和宿主入口是否需要调整。

当前相关位置：

- `server/platform-persistence-composition/PlatformPersistenceModelCatalog.cs`
- `apps/host-api/Extensions/PersistenceBootstrapExtensions.cs`
- `apps/host-api/Program.cs`

当前正常路径是：

1. 模块先补自己的 `*ModelDefinitions`
2. 平台组合层决定是否把它纳入宿主组合
3. 宿主继续通过 `AddPlatformPersistence` 和 `ApplyPlatformPersistenceAsync` 承接

不建议的路径：

- 在 `Program.cs` 里直接手写模块私有持久化装配
- 在宿主里直接感知某个仓储的细节

## 6. 第五步：迁移与 smoke

当模型语义和 EF 适配完成后，再进入真实数据库验证。

当前相关位置：

- `server/platform-persistence-efcore.migrations.sqlserver`
- `server/platform-persistence-efcore.migrations.oracle`
- `server/platform-persistence-efcore.migrations.mysql`
- `tests/integration/persistence`

推荐顺序：

1. 先跑基础测试
2. 再跑对应迁移
3. 最后跑 smoke

当前最常用命令：

```powershell
dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj -v minimal
dotnet test .\tests\Persistence.Tests\Persistence.Tests.csproj -v minimal
powershell -ExecutionPolicy Bypass -File .\tests\integration\persistence\Run-SqlServerSmoke.ps1
```

如果改动触及首批落库模块，通常还应补跑：

- `tests/ConfigCenter.Tests`
- `tests/Authorization.Tests`

## 7. 第六步：文档与状态收口

持久化改动完成后，不要只停在“代码能跑”。

通常还应该检查：

- `docs/current-status.md` 是否需要更新当前阶段口径
- `tests/README.md` 是否需要补新的验证入口
- 是否需要补使用说明或开发流程文档

如果改动的是模型语义生成方式或约束，还应同步检查：

- `docs/architecture/ai-metadata-authoring-guide.md`

## 8. 常见越层问题

下面这些做法当前不推荐：

- 在业务服务里直接写 Provider 分支
- 在模块里直接决定 migration 程序集
- 在模型语义层混入数据库方言
- 在宿主里硬编码模块私有持久化实现细节
- 把 `EfPlatformDbContext` 当成业务层通用仓储替代品

如果你发现自己必须这么做，先停一下，通常说明边界应该重新审视。

## 9. 一个最小检查清单

当你新增或修改一个持久化模块时，至少确认：

1. 业务边界和仓储契约是清楚的
2. 模型语义定义已更新
3. EF 适配实现已更新
4. 需要的话，组合层已更新
5. 对应迁移和 smoke 已验证
6. 文档和状态入口已收口

## 10. 当前仓库里最值得参照的样本

建议优先参考：

- `ConfigCenter`
- `Authorization`

原因是：

- 这两个模块已经覆盖了模型定义、EF 仓储、组合层接入、迁移和 smoke 闭环
- 它们就是当前 `Phase 6.5` 的首批真实落库样本

## 11. 相关文档

- API 边界：[`persistence-public-api.md`](./persistence-public-api.md)
- 使用方式：[`persistence-usage-guide.md`](./persistence-usage-guide.md)
- 本地三库准备：[`../guides/local-multi-db-container-setup.md`](../guides/local-multi-db-container-setup.md)
- 当前测试入口：[`../../tests/README.md`](../../tests/README.md)
