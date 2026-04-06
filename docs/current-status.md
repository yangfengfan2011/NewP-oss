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

## 当前可用能力

### 后端

- Host API 可构建
- 模块系统可装配
- Demo / Authorization / ConfigCenter / Audit / Files / ModuleTemplateSample 模块已有代码基础

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

1. 启动 `Phase 6.5` 持久化底座收口，优先为 `ConfigCenter` / `Authorization` 建立真实数据库持久化、本地初始化与可复跑验证。
2. 在真实持久化底座上继续推进迁移波次 1，优先扩展 `Dictionary` 和 `Enumeration` 等旧资产类型。
3. 缓存与性能增强放到后续证据驱动阶段，不在持久化之前抢跑。
4. 持续关注 Phase 5 中文 Description 编码不稳定风险，并持续维护计划、状态和模块文档一致性。

对应设计：

- [designs/post-phase-6-foundation-and-migration-design.md](designs/post-phase-6-foundation-and-migration-design.md)

## AI-First 校准结论

当前项目整体方向仍然有效，但此前阶段目标更多偏向“把平台做完整”，还没有始终把“让 AI 更容易独立完成工作”放在最高优先级。

现阶段不建议大规模返工，建议采用“保留主干、补强约束、校准阶段目标”的方式推进。

很短的返工清单：

- 必须调整：把 `AI-first` 写入主文档、计划和阶段设计，作为正式评判标准。
- 必须调整：Phase 5 交付除导入功能外，还要包含固定样本、映射规则、固定执行路径和复跑验证记录。
- 建议调整：后续逐步检查已落地模块是否存在超大文件、隐式耦合、局部验证入口不足的问题。
- 暂不需要：对 Phase 1 到 Phase 4 的主线成果做推倒重来式返工。

对 Phase 5 的判断：

- 当前方向基本正确，不需要停下重做。
- 但需要把阶段目标从“做出一个迁移样例”升级成“做出第一条 AI 可复制迁移模板”。
