# 持久化开发者手册补全文档设计

## 1. 背景

`Phase 6.5` 的多数据库持久化实现已经完成首批落地：

- `platform-modeling` 提供模型语义定义与校验器
- `platform-persistence` 提供 Provider 无关的持久化内核抽象
- `platform-persistence-efcore` 提供 EF Core 第一批适配器
- `platform-persistence-composition` 聚合首批平台模型定义
- `HostApi` 已通过 `AddPlatformPersistence` 与 `ApplyPlatformPersistenceAsync` 接入启动期持久化
- `SqlServer` / `Oracle` / `MySql` 三套迁移项目与 smoke 脚本已经可复跑

当前仓库已经有一批与持久化相关的设计、运行记录和验证文档，但它们主要服务于“方案设计与阶段收口”，还没有形成一套面向仓库内部开发者的统一使用手册。

这导致开发者虽然能从代码和分散文档中拼出结论，但仍然缺少以下直接答案：

- 平台当前对内部开发者暴露了哪些持久化公共 API
- 宿主、模块和测试应该如何使用这些入口
- 新增或修改一个持久化模块时，推荐按什么开发顺序推进

因此需要补一组正式的仓库内部开发者手册，作为 `Phase 6.5` 实现落地后的直接说明入口。

## 2. 目标

- 为仓库内部开发者补齐一组正式的持久化手册入口。
- 把“公共 API”“如何使用”“开发流程”三类内容分开沉淀，避免继续混在设计稿和状态文档中。
- 手册内容直接锚定当前真实代码入口，而不是重复抽象设计稿。
- 让后续新增模块、修改模型语义、接入 EF 仓储、切换 Provider、执行迁移和 smoke 时，都有明确查阅入口。

## 3. 非目标

- 本轮不扩展成完整运维手册、部署手册或故障排查大全。
- 本轮不重写 `Phase 6.5` 设计稿，不把设计文档改造成教程。
- 本轮不承诺补齐所有未来模块的持久化说明，首批仍以当前已落地的 `ConfigCenter` 与 `Authorization` 为示例。
- 本轮不新增新的持久化能力；重点是整理并固化已实现能力的开发者认知入口。

## 4. 受众与使用场景

目标读者是仓库内部开发者，主要包括：

- 需要在宿主中启用或理解平台持久化的后端开发者
- 需要为模块新增模型语义定义、EF 仓储或迁移支持的开发者
- 需要运行 smoke、验证本地多数据库闭环的开发者

核心使用场景包括：

1. 我想知道当前持久化层有哪些稳定入口，分别干什么。
2. 我想在宿主程序里启用平台持久化，应该看哪里。
3. 我想为新模块接入持久化，应该按什么顺序修改代码与文档。
4. 我想切换 `SqlServer` / `Oracle` / `MySql`，应该改哪些配置和命令。
5. 我想知道本轮持久化开发时，哪些动作属于正常路径，哪些属于越层实现。

## 5. 方案选择

### 5.1 方案 A：只补一篇总文档

做法：

- 新增一篇总文档，把公共 API、使用方式、开发流程全部放进去。

优点：

- 交付最快。

缺点：

- 文档容易快速膨胀成大杂烩。
- API 说明、操作说明和流程约束会混在一起，检索体验差。
- 后续维护时更容易产生重复和失效段落。

### 5.2 方案 B：三篇正式手册 + 少量入口链接

做法：

- 分别新增“公共 API”“使用指南”“开发流程”三篇文档。
- 在现有入口文档中补最少量交叉链接。

优点：

- 文档边界清晰。
- 更适合仓库内部开发者按需查阅。
- 后续补充内容时不容易互相污染。

缺点：

- 比单篇文档多维护几个文件。

### 5.3 选择结论

本轮选择 **方案 B**。

原因：

- 用户明确希望补的是“仓库内部开发者手册”。
- 这类文档更强调查阅效率和职责边界，而不是单篇完整叙述。
- 当前仓库已经有设计稿和状态文档，开发者手册应该承担“落地使用说明”，不再混合承载全部背景。

## 6. 文档结构设计

本轮新增三篇文档，统一放在 `docs/guides/` 下：

### 6.1 `docs/guides/persistence-public-api.md`

定位：

- 说明当前平台对内部开发者暴露的稳定持久化入口与职责边界。

重点内容：

- 宿主级入口
  - `AddPlatformPersistence`
  - `ApplyPlatformPersistenceAsync`
- EF Core 适配层入口
  - `AddEfCorePersistence`
- 配置模型
  - `PersistenceOptions`
  - `PersistenceProvider`
  - `PersistenceAdapterKind`
- 核心抽象
  - `IModelDefinitionProvider`
  - `ISqlDialect`
  - `ISqlQueryExecutor`
  - `IPersistenceMigrationRunner`
  - `EfPlatformDbContext`
  - `PlatformPersistenceModelCatalog`

写法要求：

- 每个入口都要写清楚“它是什么”“谁应该依赖它”“谁不应该直接依赖它”。
- 以代码文件为锚点，不复制大段源码。

### 6.2 `docs/guides/persistence-usage-guide.md`

定位：

- 说明开发者如何在当前仓库里使用这套持久化能力。

重点内容：

- 宿主启用方式
- `appsettings` 与环境变量的关键配置项
- Provider 切换方式
- 三套迁移项目的角色
- 如何执行 `dotnet-ef database update`
- 如何执行三套 smoke 脚本
- 当前首批适用范围与已知边界

写法要求：

- 以“我现在要做什么”为主线组织内容。
- 优先给出当前仓库可直接执行的路径、命令和入口。

### 6.3 `docs/guides/persistence-development-workflow.md`

定位：

- 说明新增或修改一个持久化模块时，推荐遵循的研发顺序与边界。

重点内容：

- 推荐开发顺序
  1. 先定义业务边界
  2. 再定义模型语义
  3. 再实现持久化适配
  4. 再接宿主与迁移
  5. 最后做测试与 smoke
- 典型改动清单
- 常见越层问题
- 文档与验证收口要求

写法要求：

- 不是理论设计稿，而是开发时可直接照着执行的流程清单。
- 必须明确“正常路径”和“不建议路径”。

## 7. 与现有文档的关系

本轮不会替换现有文档，而是补一个更贴近开发动作的入口层：

- 设计背景继续保留在 `docs/superpowers/specs/2026-04-09-phase-6-5-multi-db-persistence-design.md`
- AI 元数据约束继续保留在 `docs/architecture/ai-metadata-authoring-guide.md`
- 本地三库准备继续保留在 `docs/guides/local-multi-db-container-setup.md`
- 本机运行记录继续保留在 `docs/guides/2026-04-09-local-multi-db-runtime-record.md`
- smoke 入口继续保留在 `tests/README.md`

新文档的职责是把这些内容串起来，让开发者知道：

- 先看哪篇
- 操作时该跳到哪篇
- 深入背景时再看哪篇

## 8. 交叉引用策略

为避免继续分散，本轮会补最少量入口链接：

- 在 `docs/current-status.md` 的 `Phase 6.5` 段落增加“开发者手册入口”提示
- 在 `tests/README.md` 的 persistence smoke 段落增加对 `persistence-usage-guide.md` 的引用

如果必要，也可以在后续小补一条：

- `server/platform-modeling/README.md` 中增加对开发流程文档的引用

但本轮以最小改动为原则，不做大规模 README 重写。

## 9. 文档风格约束

本轮开发者手册统一遵循：

- 面向仓库内部开发者
- 优先解释当前代码入口和实际操作
- 使用当前仓库中的真实路径、真实命令和真实 Provider 名称
- 不把设计背景、运行记录和使用指南混成一篇
- 避免把“未来可能支持什么”写成“当前已经支持什么”

## 10. 成功标准

完成后，应满足以下结果：

1. 开发者可以从一组明确文档中找到：
   - 当前稳定持久化入口
   - 宿主接入与使用方式
   - 模块开发流程
2. `Phase 6.5` 的实现不再只依赖设计稿和会话摘要来理解。
3. `tests/README.md` 与 `docs/current-status.md` 能把读者导向新的持久化手册入口。
4. 文档内容与当前已落地代码一致，不夸大当前范围。

## 11. 实施范围

本轮预计涉及：

- 新增：
  - `docs/guides/persistence-public-api.md`
  - `docs/guides/persistence-usage-guide.md`
  - `docs/guides/persistence-development-workflow.md`
- 修改：
  - `docs/current-status.md`
  - `tests/README.md`

## 12. 后续计划衔接

本设计确认后，下一步应进入实施计划，拆出：

- 文档梳理与 API 清单整理
- 三篇手册落盘
- 交叉引用补齐
- 最小一致性验证

实施时应继续以当前真实代码为准，不新增未实现承诺。
