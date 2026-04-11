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

1. **WP2 AI-first 规范落地**：把 AI-first 从方向性原则提升为日常执行标准与验收标准。设计文档已产出（`designs/ai-first-execution-standard-design.md`），实现尚未启动。
2. **WP3 文档与状态一致性治理**：让状态文档、计划文档、设计文档、验证记录形成统一口径。设计文档已产出（`designs/post-phase-6-status-alignment-design.md`）。
3. **WP4 最小迁移连续性**：不扩大战线，只维持 `Dictionary`、`Enumeration` 的迁移准备度。
4. **运维体验优化**：将 destructive-action 确认流程与 warning severity 统一到 shell 级交互规范中。

对应路线设计：

- [designs/post-phase-6-governance-and-ai-first-roadmap-design.md](designs/post-phase-6-governance-and-ai-first-roadmap-design.md)

### 文档链路已知缺口

| 能力 | 缺口 |
|------|------|
| 多数据库持久化 Phase 6.5 | 缺独立验证文档 |
| 插件运维中心 Phase 6.7 | 缺独立实现说明文档、缺独立验证文档 |

这些缺口应由后续专项任务补齐。

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
