# 当前项目状态

本文档用于给出仓库当前的统一口径，避免项目状态分散在会话摘要、阶段文档和模块文档中。

## 阶段完成度

### Phase 0：盘点与处置

状态：已完成

已沉淀：

- 旧平台资产盘点
- 资产处置矩阵
- V1 范围冻结

对应文档：

- [migration/oldp-asset-inventory-v1.md](migration/oldp-asset-inventory-v1.md)
- [migration/oldp-asset-disposition-v1.md](migration/oldp-asset-disposition-v1.md)
- [architecture/scope-v1.md](architecture/scope-v1.md)

### Phase 1：仓库基础主干

状态：已完成

已落地：

- `ICP-Platform.sln`
- `apps/host-api`
- `server/kernel`
- `server/sdk`
- `frontend/packages/shell`
- `frontend/packages/core`
- `frontend/packages/sdk`
- 基础脚本与基础文档

验证口径：

- 后端可构建
- 前端可安装
- 前端可构建

对应文档：

- [phases/scaffold-summary.md](phases/scaffold-summary.md)
- [verification/scaffold-verification.md](verification/scaffold-verification.md)

### Phase 2：最小可运行平台

状态：已完成

已落地：

- LocalAuth / Identity Lite 最小登录能力
- 登录页、登录 API、当前用户 API、登出 API
- 默认管理员账号
- 前端认证上下文与路由守卫
- 登录后进入 Shell
- Demo 受保护页面访问闭环

验证口径：

- 能打开登录页
- 能用默认账号登录
- 登录后能进入主界面
- 能访问受保护 Demo 页面
- 登出后不能继续访问受保护页面

对应文档：

- [phases/phase-2-minimum-runnable-plan.md](phases/phase-2-minimum-runnable-plan.md)
- [verification/quick-verification.md](verification/quick-verification.md)

### Phase 3A：扩展契约定义

状态：已完成

已落地：

- 前后端模块注册契约
- 菜单、路由、权限等基础扩展契约
- 契约相关基础架构文档

未完全沉淀：

- 更明确的契约验证收口

对应文档：

- [architecture/module-contract.md](architecture/module-contract.md)
- [architecture/module-boundaries-v1.md](architecture/module-boundaries-v1.md)

### Phase 3B：Demo 贯通与契约验证

状态：已部分完成

已落地：

- Shell 菜单/路由/权限聚合
- Demo 模块端到端贯通

未完全沉淀：

- 更完整的契约验证说明

对应文档：

- [verification/demo-module-verification.md](verification/demo-module-verification.md)

### Phase 3C：阶段总结与文档收口

状态：已完成

已落地：

- 独立的 Phase 3 阶段总结
- 更完整的契约验证说明
- 与计划对齐的阶段收口文档

对应文档：

- [phases/phase-3-extension-contracts-summary.md](phases/phase-3-extension-contracts-summary.md)
- [verification/phase-3-contract-verification.md](verification/phase-3-contract-verification.md)

### Phase 4A：Authorization / ConfigCenter 完善

状态：已完成

已收口内容：

- `Authorization` 已完成详细设计、实现说明与模块级验证记录收口
- `ConfigCenter` 已完成详细设计、实现说明与模块级验证记录收口
- 两个模块的后端测试与前端构建已重新验证通过

对应文档：

- [designs/authorization-design.md](designs/authorization-design.md)
- [designs/configcenter-design.md](designs/configcenter-design.md)
- [modules/authorization-implementation.md](modules/authorization-implementation.md)
- [modules/configcenter-implementation.md](modules/configcenter-implementation.md)
- [verification/authorization-module-verification.md](verification/authorization-module-verification.md)
- [verification/configcenter-module-verification.md](verification/configcenter-module-verification.md)

### Phase 4B：Audit / Files 完善

状态：已完成

已收口内容：

- `Audit` 已补齐详细设计、实现说明与模块级验证记录
- `Files` 已补齐详细设计、实现说明与模块级验证记录
- 两个模块的后端测试与前端构建已重新验证通过

对应文档：

- [designs/audit-design.md](designs/audit-design.md)
- [designs/files-design.md](designs/files-design.md)
- [modules/audit-implementation.md](modules/audit-implementation.md)
- [modules/files-implementation.md](modules/files-implementation.md)
- [verification/audit-module-verification.md](verification/audit-module-verification.md)
- [verification/files-module-verification.md](verification/files-module-verification.md)

### Phase 5：首个旧资产迁移 POC

状态：已完成

已落地：

- 迁移策略文档
- 元数据迁移策略文档
- Phase 5 首个导入 POC 设计文档
- Setting 导入 POC 迁移文档
- Setting 导入 POC 验证记录

已验证：

- 真实样本：`CompanyRuleSetting.setting`
- 导入入口：`POST /api/config/import/setting`
- HostApi 构建成功
- ConfigCenter 测试 14 个通过
- 导入的 `OtherCompanyCanOperate` 查询可用
- 分组查询 `ConfigCenter/CompanyRuleSetting` 可用
- 现有前端配置页已具备承接 `grouped` 查询结果的能力，但本轮未单独做浏览器级人工验证

已知限制：

- **中文 Description 编码不稳定**：中文 Description 在当前脚本化 smoke 中存在编码不稳定风险

AI-First 对齐：

- Phase 5 的核心输出是第一条 AI 可复制执行的迁移模板，不仅是单个样本导入
- 已完成样本冻结、规则冻结、执行冻结、验证冻结、复用冻结五类交付

对应文档：

- [designs/phase-5-first-import-poc-design.md](designs/phase-5-first-import-poc-design.md)
- [migration/setting-import-poc.md](migration/setting-import-poc.md)
- [verification/setting-import-poc-verification.md](verification/setting-import-poc-verification.md)

### Phase 6：开发体验与模板化

状态：已完成

已落地：

- 后端组件化模板设计与实现
- 仓库内后端模板骨架（`server/templates/backend-module-template`，已补齐 `.csproj`）
- `ModuleTemplateSample` 前后端端到端示例模块
- `tests/ModuleTemplateSample.Tests` 已补回并验证通过（8 个测试全部通过）
- 前端组件化模板设计与实现
- 仓库内前端模板骨架（`frontend/templates/frontend-module-template`，默认包含最小状态接口联通页）
- 前端模板构建验证通过
- `scripts/new-module.ps1` / `scripts/new-module.sh` 可执行脚手架
- 前端模块接入指南（`frontend/docs/module-integration-guide.md`）
- 前端架构说明文档（`frontend/README.md`）
- 对应测试与验证记录已按真实结果修正

已验证：

- 后端模板可复制并创建新模块
- 前端模板可复制并创建新模块
- ModuleTemplateSample 已集成到 HostApi 并构建成功
- 前端整个工作区构建成功（包含模板包与 `module-template-sample`）
- `new-module.ps1 -ModuleName Phase6ScaffoldSmoke -SkipIntegration` 已真实生成并构建通过
- `new-module.ps1 -ModuleName Phase6IntegrationSmoke` 已真实验证宿主接入路径
- `new-module.sh --module-name Phase6ShellSmoke --skip-integration` 已在当前 WSL 环境下通过包装器调用 PowerShell 脚手架并完成生成与构建验证

对应文档：

- [designs/post-phase-6-foundation-and-migration-design.md](designs/post-phase-6-foundation-and-migration-design.md)
- [verification/phase-6-backend-component-template-verification.md](verification/phase-6-backend-component-template-verification.md)
- [verification/phase-6-frontend-component-template-verification.md](verification/phase-6-frontend-component-template-verification.md)
- [verification/phase-6-closeout-verification.md](verification/phase-6-closeout-verification.md)
- [verification/phase-6-frontend-component-template-verification.md](verification/phase-6-frontend-component-template-verification.md)

### Phase 6.6：运行层插件平台最小闭环

状态：已完成（Phase 1 最小闭环）

已落地：

- 后端插件清单模型与插件目录扫描能力
- 基于 `plugin.json` 的入口程序集解析与宿主版本兼容校验
- 宿主从 `modules/*/plugin.json` 发现并装载内建模块
- 开发期内建模块清单已放入 `apps/host-api/modules/*`
- `Program.cs` 已切离 `*Module.dll` 扫描与 `ConfigCenter.dll` 特判
- 插件打包脚本与插件包校验脚本已落地
- 统一插件包打包脚本与校验脚本已落地
- 统一插件包安装目录、状态存储与版本化安装能力已落地
- 已启用插件到后端 load-view 与前端运行时 manifest 的投影能力已落地
- `PluginOps` 与 `manage-unified-plugin` 运维入口已落地，可执行 install / enable / disable / uninstall / rollback
- `Audit` / `Authorization` / `ModuleTemplateSample` 已验证可产出标准插件目录
- 面向仓库外部开发的后端插件模板已落地
- `new-module.ps1` / `new-module.sh` 已支持外部插件模式

已验证：

- `dotnet test tests/Kernel.Tests/Kernel.Tests.csproj`
- `dotnet build apps/host-api/HostApi.csproj`
- Host 启动日志显示通过插件目录发现并装载 6 个内建模块
- `scripts/pack-plugin.ps1` 与 `scripts/verify-plugin-package.ps1` 已对现有模块完成真实打包校验
- `scripts/pack-unified-plugin.ps1` 与 `scripts/verify-unified-plugin-package.ps1` 已对 `Audit` 完成真实打包校验
- `scripts/manage-unified-plugin.ps1` 已对 `Audit` 完成真实 install / enable / disable / uninstall / rollback 操作，并验证运行时投影视图与状态目录同步变化
- 宿主已在 `Platform__PluginInstallRoot=artifacts/installed-plugins` 下完成安装结果驱动的烟测启动
- 外部插件样例已通过脚手架生成、构建、打包与校验链路验证

当前边界：

- `ProjectReference` 仍保留在 `HostApi.csproj` 中，作为开发期 DLL 产出过渡机制
- 统一插件包打包脚本当前仍以 `Audit` 为 Phase 1 验证对象，不代表所有模块都已收口
- 哈希/签名校验、更完整的前端插件治理与版本感知依赖仍未纳入本轮
- 外部插件开发当前仍使用仓库内 `server/sdk/Sdk.csproj` 作为开发期 SDK 引用

对应文档：

- [architecture/plugin-package-format.md](architecture/plugin-package-format.md)
- [architecture/plugin-runtime-loading.md](architecture/plugin-runtime-loading.md)
- [architecture/plugin-compatibility-policy.md](architecture/plugin-compatibility-policy.md)
- [guides/plugin-packaging.md](guides/plugin-packaging.md)
- [guides/unified-plugin-package-operations.md](guides/unified-plugin-package-operations.md)
- [developer/external-plugin-development.md](developer/external-plugin-development.md)
- [guides/external-plugin-debugging.md](guides/external-plugin-debugging.md)
- [verification/phase-6-6-runtime-plugin-platform-verification.md](verification/phase-6-6-runtime-plugin-platform-verification.md)

### Phase 6.5：多数据库持久化（混合架构）

状态：已完成

已落地：

- `platform-modeling` 已落地，包含模型语义定义、校验器与对应测试
- `platform-persistence` 已落地，包含 Provider 无关的持久化选项、方言与迁移抽象
- `platform-persistence-efcore` 已落地，包含 EF Core 投影、查询执行器、迁移运行器与注册扩展
- `platform-persistence-composition` 已落地，用于聚合首批平台模型定义
- `ConfigCenter` / `Authorization` 已补齐模型定义与 EF 仓储实现
- `HostApi` 已接入 `AddPlatformPersistence` 与启动期迁移入口
- 三套迁移项目已生成：`SqlServer` / `Oracle` / `MySql`
- `tests/integration/persistence` 已补齐三套 PowerShell smoke 脚本
- `Persistence.Tests`、`ConfigCenter.Tests`、`Authorization.Tests` 与 `Kernel.Tests` 中受影响验证已重新跑通
- 本机 `SqlServer` / `MySql` / `Oracle` 容器上的真实 `database update + HostApi` smoke 已全部通过

当前首批范围：

- `ConfigCenter`
- `Authorization`

当前边界：

- `Audit` 与 `Files` 暂不纳入本轮首批落库范围
- 当前默认配置与 `Development` 配置都已具备 `SqlServer / Oracle / MySql` 连接串入口
- 三库 smoke 依赖本机 Docker 容器与连接串保持可用；更换环境后需要重新执行脚本确认

开发者手册入口：

- `docs/developer/persistence-public-api.md`
- `docs/developer/persistence-usage-guide.md`
- `docs/developer/persistence-development-workflow.md`

对应文档：

- [superpowers/specs/2026-04-09-phase-6-5-multi-db-persistence-design.md](superpowers/specs/2026-04-09-phase-6-5-multi-db-persistence-design.md)
- [superpowers/plans/2026-04-09-phase-6-5-multi-db-persistence.md](superpowers/plans/2026-04-09-phase-6-5-multi-db-persistence.md)
- [architecture/ai-metadata-authoring-guide.md](architecture/ai-metadata-authoring-guide.md)
- [guides/local-multi-db-container-setup.md](guides/local-multi-db-container-setup.md)
- [verification/phase-6-5-multi-db-persistence-verification.md](verification/phase-6-5-multi-db-persistence-verification.md)

### Post-Phase-6：Metadata Config Center Foundation

状态：已完成并收口

已落地：

- 独立后端模块 `server/modules/MetadataConfigCenter`
- 独立前端模块 `frontend/modules/metadata-config-center`
- 基于 `modules/*/metadata/metadata.manifest.json` 的模块发现与 `SettingDefinition` authoring 闭环
- `SettingDefinition` 的读取、保存与“已保存未应用 / Git 未同步”状态返回
- `ConfigCenter` 与 `MetadataConfigCenter` 的边界收口：前者只承载 `ConfigItem` 值层，后者只承载定义层 authoring
- Shell 已将 `metadata-config-center` 作为 host-managed 模块接入，保证运行时 manifest 存在但未安装对应插件包时，工作台入口仍可注册

已验证：

- `dotnet test .\\tests\\MetadataConfigCenter.Tests\\MetadataConfigCenter.Tests.csproj -nr:false -v minimal`
- `dotnet test .\\tests\\ConfigCenter.Tests\\ConfigCenter.Tests.csproj --filter "ConfigServiceTests|LegacySettingImportServiceTests|ConfigCenterModuleTests" -nr:false -v minimal`
- `pnpm --filter @icp-platform/module-metadata-config-center build`
- `pnpm --filter @icp-platform/module-config-center build`
- `pnpm --filter @icp-platform/shell build`
- `pnpm --dir .\\frontend\\packages\\shell exec vitest --run src/__tests__/registerModules-plugin-ops.test.ts`
- 已执行一次最小 API smoke：在 `Persistence__AutoMigrate=false` 下启动 `HostApi`，成功读取 `ConfigCenter` 模块设置定义并完成保存，返回状态为 `已保存未应用`

当前边界：

- foundation 首版只打通 `SettingDefinition` 定义层 authoring，不扩展到 `Dictionary / Enumeration` 的复杂工作台
- `应用` 与 `Git 同步` 仍保持显式分离；当前闭环只验证“保存后未应用”的状态链路
- 当前手工 smoke 通过关闭启动期迁移来绕开数据库依赖，验证的是定义层 authoring 闭环，不代表值层数据库 smoke 被替代

对应文档：

- [designs/metadata-config-center-design.md](designs/metadata-config-center-design.md)
- [designs/configcenter-design.md](designs/configcenter-design.md)
- [verification/metadata-config-center-foundation-verification.md](verification/metadata-config-center-foundation-verification.md)

## 当前可用能力

### 后端

- Host API 可构建
- 模块系统可装配
- Demo / Authorization / ConfigCenter / Audit / Files / ModuleTemplateSample 模块已有代码基础
- 宿主可通过插件目录与插件清单装载内建后端模块
- 插件平台已具备依赖治理、卸载、版本保留与 rollback 原子恢复的后端闭环

### 前端

- Shell 可运行
- 登录闭环已接通
- Demo / Authorization / ConfigCenter / Audit / Files / ModuleTemplateSample 模块已接入前端工作区
- 前端模板骨架可复制并独立构建
- 前端最小端到端示例模块可独立构建
- PowerShell / Shell 脚手架可生成新的最小前后端模块骨架
- clean 环境下已验证 `pnpm install --frozen-lockfile` 与 `pnpm build` 可通过

### 测试

- `tests/Kernel.Tests`
- `tests/Authorization.Tests`
- `tests/ConfigCenter.Tests`
- `tests/Audit.Tests`
- `tests/Files.Tests`
- `tests/ModuleTemplateSample.Tests`

## 当前主要风险

- 文档与代码进度曾经出现过不一致，现已开始整顿，但仍需持续维护。
- Phase 5 中文 Description 在当前脚本化 smoke 中存在编码不稳定风险。

## 当前推荐下一步

当前后续路线以平台治理和 AI-first 收口为优先，按以下工作包顺序推进：

1. **WP4 最小迁移连续性**：`Stage 10` 与后续“枚举消费层 UI / 接入”子包已完成并通过，当前冻结范围内的 `Dictionary + Enumeration` 静态资源转换与最小前端消费闭环都已形成。

对应路线设计：

- [designs/post-phase-6-governance-and-ai-first-roadmap-design.md](designs/post-phase-6-governance-and-ai-first-roadmap-design.md)

### 文档链路校准结论

- `Phase 6.5` 已补齐独立验证文档：`verification/phase-6-5-multi-db-persistence-verification.md`
- `Phase 6.6` 已补齐独立验证文档：`verification/phase-6-6-runtime-plugin-platform-verification.md`
- `Phase 6.7` 已补齐独立实现说明与独立验证文档：
  - `modules/plugin-ops-implementation.md`
  - `verification/phase-6-7-plugin-operations-center-verification.md`

## AI-First 校准结论

当前项目整体方向仍然有效，但此前阶段目标更多偏向“把平台做完整”，还没有始终把“让 AI 更容易独立完成工作”放在最高优先级。

现阶段不建议大规模返工，建议采用“保留主干、补强约束、校准阶段目标”的方式推进。

很短的返工清单：

- 必须调整：把 `AI-first` 写入主文档、计划和阶段设计，作为正式评判标准。
- 必须调整：Phase 5 交付除导入功能外，还要包含固定样本、映射规则、固定执行路径和复跑验证记录。
- 建议调整：后续逐步检查已落地模块是否存在超大文件、隐式耦合、局部验证入口不足的问题。
- 建议调整：在正式建设元数据设计器之前，统一按 [architecture/ai-metadata-authoring-guide.md](architecture/ai-metadata-authoring-guide.md) 让 AI 生成模型语义定义，并通过校验器与测试兜底。
- 暂不需要：对 Phase 1 到 Phase 4 的主线成果做推倒重来式返工。

对 Phase 5 的判断：

- 当前方向基本正确，不需要停下重做。
- 但需要把阶段目标从“做出一个迁移样例”升级成“做出第一条 AI 可复制迁移模板”。

### Phase 6.7：插件运维中心 V1

状态：已完成首版闭环

已落地：

- `HostApi` 已提供 `GET /api/plugin-ops/modules`、上传校验、安装、启用、禁用、卸载、回滚等管理员接口
- `frontend/modules/plugin-ops` 已接入 Shell，提供 `/plugin-ops` 页面
- 页面已接入真实 API，可完成：
  - 模块状态查看
  - 统一插件包上传校验
  - install / enable / disable / uninstall / rollback
- 前端侧不再依赖当前未实际发放的额外权限点做显示拦截，改为“登录即可见，后端继续 AdminOnly 兜底”

已验证：

- `pnpm --dir .\frontend\packages\shell test`
- `pnpm --dir .\frontend build`
- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter "FullyQualifiedName~PluginOperationsAdminServiceTests|FullyQualifiedName~PluginOperationsControllerTests|FullyQualifiedName~HostApiPluginManifestRouteTests"`
- `dotnet build .\apps\host-api\HostApi.csproj`

当前边界：

- 当前网页入口聚焦统一插件包已存在的运维动作，不包含源码一键打包、签名校验或更细粒度的多角色治理
- 前端静态资源与运行时清单会随 install / enable / disable / uninstall / rollback 更新，但后端程序集若已在运行，仍可能需要重启宿主才能按新视图重载

对应文档：

- [superpowers/specs/2026-04-08-plugin-operations-center-v1-design.md](superpowers/specs/2026-04-08-plugin-operations-center-v1-design.md)
- [modules/plugin-ops-implementation.md](modules/plugin-ops-implementation.md)
- [guides/unified-plugin-package-operations.md](guides/unified-plugin-package-operations.md)
- [verification/phase-6-7-plugin-operations-center-verification.md](verification/phase-6-7-plugin-operations-center-verification.md)

### Post-Phase-6：WP1 插件平台治理增强

状态：已完成并已提交主线

已落地：

- 发现阶段已增加插件依赖治理，依赖不满足的插件不会进入程序集装载阶段
- 插件状态机已正式区分 `Installed / Enabled / Disabled / Uninstalled` 语义
- 统一插件包生命周期已补齐 `uninstall`，并已贯通 `PluginOps` CLI、管理脚本与 `DELETE /api/plugin-ops/modules/{moduleId}`
- install 阶段已补依赖预检 warning，enable 阶段已补依赖强校验，uninstall 阶段已补被依赖 warning
- 已实现默认保留最近 2 个版本的版本清理策略，并在 install / enable 后触发旧版本清理
- HostApi / Web 路径的 rollback 已补齐与 CLI 对等的原子恢复保护，控制器级失败注入已验证受控失败与状态恢复

已验证：

- `dotnet build tests/Kernel.Tests/Kernel.Tests.csproj -nr:false -v minimal`
- `dotnet build tools/PluginOps/PluginOps.csproj -nr:false -v minimal`
- `dotnet build apps/host-api/HostApi.csproj -nr:false -v minimal`
- `dotnet test tests/Kernel.Tests/Kernel.Tests.csproj --filter "FullyQualifiedName~PluginRuntimeAssemblyLoaderTests|FullyQualifiedName~PluginOperationsAdminServiceTests|FullyQualifiedName~UnifiedPluginPackageLifecycleOperationsTests|FullyQualifiedName~UnifiedPluginPackageInstallerTests|FullyQualifiedName~PluginOperationsLifecycleTests|FullyQualifiedName~PluginOperationsControllerTests|FullyQualifiedName~PluginOpsIntegrationTests" -nr:false -v minimal`
- 上述 WP1 相关定向测试共 `124` 个全部通过
- `plugin-ops` shell 定向 vitest `6` 个全部通过，`@icp-platform/shell` 构建通过

当前边界：

- 依赖治理当前仍是模块级语义，不支持 version-aware dependency
- 运维页已补齐 uninstall 动作，并以 warning tone 展示 install / uninstall 的治理提示
- rollback 与 enable / disable / uninstall 均已具备 HostApi 控制器级失败注入验证与状态恢复覆盖

对应文档：

- [designs/plugin-governance-enhancement-design.md](designs/plugin-governance-enhancement-design.md)
- [verification/plugin-governance-enhancement-verification.md](verification/plugin-governance-enhancement-verification.md)

### Post-Phase-6：WP2 AI-first 执行标准落地

状态：已完成并收口

已落地：

- 新增正式标准文档 `docs/architecture/ai-first-execution-standard.md`，把分散在 `AGENTS.md`、AI-first 判定准则、组件化护栏和设计模板中的规则收成统一执行入口
- `docs/designs/detailed-design-template.md` 已补齐 AI-first 最小检查清单，明确要求新设计回答“入口、边界、验证、失败信号、回滚”5 项必答项
- `docs/designs/README.md` 已补充提交前定向校验命令，形成设计入口到校验入口的最小流程闭环
- 新增 `scripts/validate-ai-first-design.ps1`，可对指定设计文档执行 5 项必答项与占位词检查
- `tests/Kernel.Tests` 已补齐脚本级回归测试，覆盖“缺项失败”“占位词失败”“模板通过”三类核心行为
- 新增 `scripts/check-ai-first-design-targets.ps1` / `scripts/check-ai-first-design-targets.sh`，将当前受控规范文档集收敛为单一检查入口
- `scripts/test.ps1` / `scripts/test.sh` 已接入 AI-first 设计目标检查段，根测试入口不再完全忽略 WP2 门禁
- `.github/workflows/ci.yml` 已接入 AI-first 设计目标校验步骤，使主线 CI 能检查当前受控规范文档集
- `check-ai-first-design-targets.ps1` 已支持显式传入附加设计文档，并默认自动发现当前工作区中新增或修改的 `docs/designs/*-design.md`
- 受控门禁已从“只校验固定规范入口”扩展到“固定规范入口 + 本轮新增设计文档”，开始覆盖真实新增工作流
- 回归测试已覆盖显式附加设计失败与未跟踪新增设计自动发现失败两类关键路径
- 新增 `docs/architecture/ai-first-design-check-targets.txt`，将受控基础目标集合从脚本硬编码下沉为仓库内显式清单
- `check-ai-first-design-targets.ps1` 已支持 `-TargetsFile`，后续可在不改脚本逻辑的前提下调整受控基础集
- 新增 `scripts/register-ai-first-design-target.ps1` / `scripts/register-ai-first-design-target.sh`，可将新增设计文档以相对路径登记到显式目标清单
- `docs/designs/README.md` 与 `docs/architecture/ai-first-execution-standard.md` 已补充“如何登记到 AI-first 门禁”的命令入口
- 新增 `scripts/new-design-doc.ps1` / `scripts/new-design-doc.sh`，可按模板创建 `<name>-design.md`，并默认将其登记到 AI-first 显式目标清单
- 新增设计的最小标准路径已从“手工复制模板 + 手工登记”收敛为单一脚本入口
- `scripts/new-design-doc.ps1` 已补上模板预校验，避免无效模板被直接创建并登记进 AI-first 目标集
- `tests/Kernel.Tests` 已新增“无效模板不能创建也不能登记”的回归测试，补齐设计创建入口的健壮性验证
- `docs/collaboration/current-collaboration-mode.md` 与 `docs/history/collaboration/github-copilot-working-mode.md` 已明确接入 `ai-first-execution-standard.md`，并把新增设计默认路径收敛为“创建 -> 校验 -> 受控门禁检查”
- `docs/phases/post-phase-6-stage-map.md` 已显式把 AI-first 标准纳入后续工作包与阶段验收入口，不再把主线误导回已完成的 `WP1`
- 新增 `docs/collaboration/work-packages/wp2-ai-first-execution-standard-closeout.md`，将本轮收口范围、允许修改文件与验收标准固定成独立工作包

已验证：

- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~AiFirstDesignValidationScriptTests -nr:false -v minimal`
- `& .\scripts\validate-ai-first-design.ps1 -Path @('.\docs\designs\detailed-design-template.md', '.\docs\architecture\ai-first-execution-standard.md')`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1` 已确认包含 AI-first 检查段并执行成功，但脚本整体仍受仓库既存失败项影响未全绿
- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~AiFirstDesignValidationScriptTests.CheckAiFirstDesignTargets_FailsWhenAdditionalDesignDoesNotMeetChecklist -nr:false -v minimal`
- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~AiFirstDesignValidationScriptTests.CheckAiFirstDesignTargets_UsesExplicitTargetsFile -nr:false -v minimal`
- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~AiFirstDesignValidationScriptTests.RegisterAiFirstDesignTarget_AppendsRelativeDesignPathOnlyOnce -nr:false -v minimal`
- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~AiFirstDesignValidationScriptTests.NewDesignDoc_CreatesDesignFromTemplateAndRegistersTarget -nr:false -v minimal`
- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~AiFirstDesignValidationScriptTests.NewDesignDoc_FailsWhenTemplateDoesNotPassValidationAndDoesNotRegisterTarget -nr:false -v minimal`

收口结论与当前边界：

- 当前受控门禁只覆盖 `detailed-design-template.md` 与 `ai-first-execution-standard.md`，尚未对既有全部设计文档做一次性回填
- 当前“新增设计自动发现”仍基于工作区变化与显式传参，不是历史设计文档的全量治理机制
- 根测试入口虽然已接入 AI-first 检查段，但脚本整体仍受 `Kernel.Tests` 中既存失败项影响，不能把本轮结果误解为整仓测试已恢复稳定
- 更大范围的状态一致性与历史文档链路清理仍属于 `WP3`，不在本轮 `WP2` 收口中展开

对应文档：

- [architecture/ai-first-execution-standard.md](architecture/ai-first-execution-standard.md)
- [collaboration/current-collaboration-mode.md](collaboration/current-collaboration-mode.md)
- [history/collaboration/github-copilot-working-mode.md](history/collaboration/github-copilot-working-mode.md)
- [phases/post-phase-6-stage-map.md](phases/post-phase-6-stage-map.md)
- [collaboration/work-packages/wp2-ai-first-execution-standard-closeout.md](collaboration/work-packages/wp2-ai-first-execution-standard-closeout.md)
- [designs/ai-first-execution-standard-design.md](designs/ai-first-execution-standard-design.md)
- [verification/ai-first-execution-standard-phase-1-verification.md](verification/ai-first-execution-standard-phase-1-verification.md)
- [verification/ai-first-execution-standard-phase-2-verification.md](verification/ai-first-execution-standard-phase-2-verification.md)
- [verification/ai-first-execution-standard-phase-3-verification.md](verification/ai-first-execution-standard-phase-3-verification.md)
- [verification/ai-first-execution-standard-phase-4-verification.md](verification/ai-first-execution-standard-phase-4-verification.md)
- [verification/ai-first-execution-standard-phase-5-verification.md](verification/ai-first-execution-standard-phase-5-verification.md)
- [verification/ai-first-execution-standard-phase-6-verification.md](verification/ai-first-execution-standard-phase-6-verification.md)
- [verification/ai-first-execution-standard-phase-7-verification.md](verification/ai-first-execution-standard-phase-7-verification.md)

### Post-Phase-6：WP3 文档与状态一致性治理

状态：已完成并收口

已落地：

- 第一轮已完成入口统一：
  - `docs/designs/post-phase-6-status-alignment-design.md` 已更新为当前轮口径，不再把 `WP1`、`WP2` 写成“只有设计、尚未实现”
  - `docs/current-status.md` 已切换到 `WP4 Stage 8` 作为当前下一步
- `docs/phases/post-phase-6-stage-map.md` 已显式区分 `Stage 3 = WP3 设计` 与 `Stage 7 = WP3 状态对齐收口`
  - `docs/collaboration/work-packages/wp3-post-phase-6-status-alignment.md` 已按真实范围更新允许修改文件、边界与验证方式
  - `docs/verification/verification-matrix.md` 已补入 `WP2` 与 `WP3` 的定向验证入口
- 第二轮已完成历史链路补齐：
  - `Phase 6.5` 已补齐独立验证文档
  - `Phase 6.6` 已补齐独立验证文档
  - `Phase 6.7` 已补齐独立实现说明与独立验证文档
  - `docs/collaboration/current-collaboration-mode.md` 已更新为 `WP3` 收口后的当前协作口径

已验证：

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\designs\post-phase-6-status-alignment-design.md`
- `powershell -NoProfile -Command "Select-String -Path '.\docs\current-status.md','.\docs\collaboration\current-collaboration-mode.md','.\docs\phases\post-phase-6-stage-map.md','.\docs\verification\verification-matrix.md' -Pattern 'WP4 最小迁移连续性|已完成并收口|phase-6-5-multi-db-persistence-verification|phase-6-6-runtime-plugin-platform-verification|phase-6-7-plugin-operations-center-verification' | ForEach-Object { '{0}:{1}:{2}' -f $_.Path, $_.LineNumber, $_.Line.Trim() }"`
- `powershell -NoProfile -Command "$paths = '.\docs\verification\phase-6-5-multi-db-persistence-verification.md','.\docs\verification\phase-6-6-runtime-plugin-platform-verification.md','.\docs\modules\plugin-ops-implementation.md','.\docs\verification\phase-6-7-plugin-operations-center-verification.md'; $missing = $paths | Where-Object { -not (Test-Path $_) }; if ($missing) { $missing; exit 1 }"`
- `git status --short -- docs/current-status.md docs/collaboration/current-collaboration-mode.md docs/phases/post-phase-6-stage-map.md docs/verification/verification-matrix.md docs/modules/plugin-ops-implementation.md docs/verification/phase-6-5-multi-db-persistence-verification.md docs/verification/phase-6-6-runtime-plugin-platform-verification.md docs/verification/phase-6-7-plugin-operations-center-verification.md docs/verification/post-phase-6-status-alignment-phase-1-verification.md docs/verification/post-phase-6-status-alignment-phase-2-verification.md Plan.md`

当前边界：

- `Plan.md` 路线层口径已对齐，因此整个 `WP3` 过程中都未强行扩写
- `WP3` 收口解决的是文档链路与协作入口一致性，不包含 `WP4` 的正式实施

对应文档：

- [designs/post-phase-6-status-alignment-design.md](designs/post-phase-6-status-alignment-design.md)
- [collaboration/current-collaboration-mode.md](collaboration/current-collaboration-mode.md)
- [phases/post-phase-6-stage-map.md](phases/post-phase-6-stage-map.md)
- [collaboration/work-packages/wp3-post-phase-6-status-alignment.md](collaboration/work-packages/wp3-post-phase-6-status-alignment.md)
- [verification/verification-matrix.md](verification/verification-matrix.md)
- [verification/post-phase-6-status-alignment-phase-1-verification.md](verification/post-phase-6-status-alignment-phase-1-verification.md)
- [verification/post-phase-6-status-alignment-phase-2-verification.md](verification/post-phase-6-status-alignment-phase-2-verification.md)

### Post-Phase-6：WP4 Stage 8 实施准入复核

状态：已完成并通过，当前结论为可进入实现

已完成：

- 已按 `docs/designs/dictionary-enumeration-migration-preparation-design.md` 第 8 节逐条复核 `Dictionary / Enumeration` 正式实施准入条件
- 已核对 Stage 8 直接相关的设计入口、样本入口、规则入口、验证入口与协作入口
- 已补齐 Stage 8 工作包入口与独立验证记录
- 已补齐迁移准备设计中的 AI-first 显式检查项，避免 Stage 8 继续依赖口头补充

已验证：

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\designs\dictionary-enumeration-migration-preparation-design.md`
- `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "FullyQualifiedName~LegacySettingImportServiceTests|FullyQualifiedName~ConfigServiceTests|FullyQualifiedName~ConfigCenterModelDefinitionTests" -nr:false -v minimal`
- `powershell -NoProfile -Command "$paths = 'OldP/Runtime/1_Server/Base/Config/Dictionary','OldP/Runtime/1_Server/Platform/Config/Dictionary','OldP/Runtime/1_Server/Platform/Config/Enumeration'; foreach($path in $paths){ if(Test-Path $path){ '{0}:FOUND' -f $path } else { '{0}:MISSING' -f $path } }"`

当前结论：

- 样本审阅与 Phase 5 迁移模板对齐已具备证据
- `Dictionary` 的权威目标形态已改为静态 i18n 资源，`Enumeration` 的权威目标形态已改为静态结构清单 + 本地化资源
- `ConfigCenter` 不再作为 `Dictionary / Enumeration` 权威多语言数据仓库
- `resources/localization/languages.json` 已作为启用语言集合入口，首批值参考 `OldP/Runtime/1_Server/Config/Apps.language`
- `Stage 8` 已满足正式实施准入条件，可进入下一包

样本复核补充：

- `Common.dic`、`Platform.dic`、`Enumerations.enum`、`Enumerations.dic` 已在 `D:/Github/ICP-X/OldP/...` 下完成最小人工审阅，结构与设计一致
- `Platform.dic` 中虽出现“请输入密码 / Please input password”类界面提示文本，但未发现真实凭据数据

下一步建议：

- `Stage 9`、`Stage 10` 与后续“枚举消费层 UI / 接入”子包已完成，当前 `WP4` 冻结范围内的静态资源转换与最小前端消费已收口
- 如继续推进，建议单独拆包处理真实业务页消费接入或下一批样本扩展

对应文档：

- [designs/dictionary-enumeration-migration-preparation-design.md](designs/dictionary-enumeration-migration-preparation-design.md)
- [collaboration/work-packages/wp4-stage-8-admission-review.md](collaboration/work-packages/wp4-stage-8-admission-review.md)
- [verification/wp4-stage-8-admission-review-verification.md](verification/wp4-stage-8-admission-review-verification.md)

### Post-Phase-6：WP4 Stage 9 Dictionary 静态资源转换与最小 i18n 基座

状态：已完成并通过

已落地：

- 新增 `scripts/migration/dictionary-to-i18n.ps1`，可将冻结样本 `.dic` 转换为静态 JSON 资源
- 新增 `tests/Kernel.Tests/DictionaryToI18nScriptTests.cs`，覆盖启用语言输出、未启用语言跳过与后输入覆盖前输入
- `resources/localization/languages.json` 已作为正式语言配置入口被脚本与前端共同消费
- 已由真实样本生成 `resources/localization/{zhs,en}/dictionary/*.json`
- `frontend/packages/core` 已补齐最小 `LocalizationProvider / useLocalization`
- `frontend/packages/shell` 已接入真实 Dictionary 资源，并在 `Home` 页面展示最小消费样例

已验证：

- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~DictionaryToI18nScriptTests -nr:false -v minimal`
- `powershell -NoProfile -ExecutionPolicy Bypass -Command "& '.\scripts\migration\dictionary-to-i18n.ps1' -InputPath @('D:\Github\ICP-X\OldP\Runtime\1_Server\Base\Config\Dictionary\Common.dic','D:\Github\ICP-X\OldP\Runtime\1_Server\Platform\Config\Dictionary\Platform.dic') -OutputRoot '.\resources\localization' -LanguagesConfigPath '.\resources\localization\languages.json'"`
- `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/localization-sample.test.tsx src/__tests__/home-page-shortcuts.test.tsx`
- `pnpm --dir .\frontend build`

当前结论：

- `Stage 9` 已形成从真实冻结样本到仓库内静态资源的第一条闭环
- `Common.dic` 与 `Platform.dic` 当前共生成每种语言 `7` 个 context 文件
- `Common.json` 在 `zhs` 下共 `51` 个键，`Platform.json` 在 `zhs` 下共 `82` 个键
- 最小 i18n 基座已经能在 Shell 中消费这些静态资源

当前边界：

- 本轮仍只处理 `Dictionary`，未进入 `Enumeration`
- 当前未提供语言切换 UI，只补齐最小 Provider、配置消费与真实资源接入

下一步建议：

- `Stage 10` 已完成；如需继续推进，请转看下方 `Stage 10` 小节中的后续方向

对应文档：

- [collaboration/work-packages/wp4-stage-9-dictionary-static-resources.md](collaboration/work-packages/wp4-stage-9-dictionary-static-resources.md)
- [verification/wp4-stage-9-dictionary-static-resources-verification.md](verification/wp4-stage-9-dictionary-static-resources-verification.md)

### Post-Phase-6：WP4 Stage 10 Enumeration 静态结构清单与本地化资源转换

状态：已完成并通过

已落地：

- 新增 `scripts/migration/enumeration-to-static-resources.ps1`，可将冻结样本 `.enum + .dic` 转换为静态枚举结构与本地化 JSON
- 新增 `tests/Kernel.Tests/EnumerationToStaticResourcesScriptTests.cs`，覆盖结构输出、启用语言过滤、孤立翻译跳过告警与缺翻译回退边界
- 已由真实样本生成 `resources/enumerations/*.json`
- 已由真实样本生成 `resources/localization/{zhs,en}/enumerations/*.json`

当前结论：

- `Stage 10` 已形成从真实冻结样本到仓库内静态枚举结构清单与本地化资源的闭环
- 当前共生成 `32` 个结构文件、`62` 个本地化文件
- `SelectMode` 等缺翻译枚举已按设计保留结构并输出 warning，不阻塞静态清单落地

当前边界：

- 本轮只处理静态 `.enum` + `.dic`，未处理 DB Enumeration
- 本轮未新增枚举消费层 UI、运行时查询 API 或数据库投影

下一步建议：

- 当前 `WP4` 冻结范围已经完成；如继续推进，建议单独拆包处理枚举消费接入或下一批样本扩展

对应文档：

- [collaboration/work-packages/wp4-stage-10-enumeration-static-resources.md](collaboration/work-packages/wp4-stage-10-enumeration-static-resources.md)
- [verification/wp4-stage-10-enumeration-static-resources-verification.md](verification/wp4-stage-10-enumeration-static-resources-verification.md)

### Post-Phase-6：WP4 Enumeration 消费层 UI / 接入

状态：已完成并通过

已落地：

- `frontend/packages/core` 已补齐 `getEnumerationItems / getEnumerationLabel / useEnumeration`
- `frontend/packages/shell` 已接入真实 `Gender / MessageState / SelectMode` 枚举静态资源
- `Home` 页面已新增“枚举资源样例”区块
- `SelectMode` 当前已明确展示为缺翻译时回退到 `ItemName` 的样例

已验证：

- `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/localization-sample.test.tsx src/__tests__/enumeration-sample.test.tsx`
- `pnpm --dir .\frontend build`

当前结论：

- `Enumeration` 已形成从静态结构清单、本地化资源到前端真实消费样例的第一条闭环
- `core` 与 `shell` 的最小接线口径已经稳定，可供后续页面继续复用

当前边界：

- 本轮只做最小样例，不包含独立枚举管理页面、控件封装或代码生成
- 本轮仍未处理 DB Enumeration、运行时查询 API 与数据库投影

下一步建议：

- 如继续推进，建议单独拆包处理真实业务页消费接入或下一批枚举样本扩展

对应文档：

- [designs/enumeration-consumer-ui-integration-design.md](designs/enumeration-consumer-ui-integration-design.md)
- [verification/enumeration-consumer-ui-integration-verification.md](verification/enumeration-consumer-ui-integration-verification.md)

### Post-Phase-6：Organization 结构模块首版闭环

状态：已完成首版闭环

已落地：

- 独立后端模块 `server/modules/Organization`
- 独立前端模块 `frontend/modules/organization`
- `公司 / 部门 / 岗位 / 人员 / 人员-用户关联` 最小主数据闭环
- 人员详情中的账号绑定状态与授权摘要只读展示
- 组织模块三套 provider migration：`SqlServer / MySql / Oracle`
- 对应实现说明、验证记录和状态文档收口

已验证：

- `dotnet test tests/Organization.Tests/Organization.Tests.csproj -nr:false -v minimal`
- `dotnet build apps/host-api/HostApi.csproj -v minimal`
- `pnpm --dir frontend build`

当前边界：

- `Identity Lite` 仍负责用户账号主体；组织模块只做只读账号查询和人员-用户关联
- `Authorization` 仍负责授权角色与权限；组织模块只展示授权摘要，不做角色分配
- 人员中心页当前已落地筛选区与列表容器，但尚未接真实人员列表 API
- 默认 `NullIdentityUserDirectory` 仍是安全降级实现，未接入真实目录时不会写入脏账号绑定

对应文档：

- [designs/organization-structure-design.md](designs/organization-structure-design.md)
- [modules/organization-implementation.md](modules/organization-implementation.md)
- [verification/organization-module-verification.md](verification/organization-module-verification.md)
