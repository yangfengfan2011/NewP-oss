# CLAUDE.md

本文件为 Claude Code 在 `NewP` 目录工作时提供项目级指引。

## 1. 当前最重要的结论

当前仓库不是“纯文档仓库”，而是已经有真实工程骨架的开发仓库。

你在开始任何工作前，必须先接受下面这几个事实：

1. 当前最重要的目标是 **第二阶段：最小可运行平台**
2. 这个阶段的成功标准不是“框架看起来完整”，而是：
   - 后端能启动
   - 前端能启动
   - 有登录页
   - 能用默认管理员登录
   - 登录后进入主界面
   - 能访问受保护的 Demo 页面
3. 当前明确 **不做**：
   - WinApp 重写
   - Studio 重写
   - 文件服务重写
   - 工作流、报表、设计器
   - 大规模旧页面兼容

## 2. 你现在所处的阶段

按当前规划，仓库重点已经切换到：

- Phase 0：旧平台盘点与处置矩阵，已形成文档
- Phase 1：仓库基础骨架，已具备初步工程结构
- **Phase 2：最小可运行平台，当前应优先推进**

这意味着：
- 你不需要再花主要精力讨论“要不要这样做”
- 你应该优先把第二阶段闭环做出来

## 3. 你必须先读的文件

优先级从高到低：

1. `AGENTS.md`
2. `docs/scope-v1.md`
3. `Plan.md`
4. `docs/phase-2-minimum-runnable-plan.md`
5. `docs/module-boundaries-v1.md`
6. `docs/identity-lite-module-boundary-v1.md`
7. `docs/authorization-module-boundary-v1.md`
8. `docs/configcenter-module-boundary-v1.md`
9. `docs/audit-module-boundary-v1.md`
10. `docs/prompts/00_第二阶段执行说明.md`

如果你的任务涉及旧平台输入，再补读：
- `docs/oldp-asset-inventory-v1.md`
- `docs/oldp-asset-disposition-v1.md`

## 4. 第二阶段的硬目标

第二阶段必须交付下面这些能力：

- `apps/host-api` 可启动
- 前端入口可启动
- 登录页可访问
- 登录接口可用
- 当前用户接口可用
- 退出登录接口可用
- 默认管理员或本地种子账号可用
- 登录后进入主界面
- 主界面有菜单与路由容器
- Demo Module 菜单可见、页面可访问
- 未登录不能直接访问受保护页面

如果做不到这些，第二阶段就还没完成。

## 5. 仓库中的真实结构

当前已经存在这些关键目录：

### 后端
- `apps/host-api`
- `server/kernel`
- `server/sdk`
- `server/modules/Demo`
- `server/modules/Authorization`
- `server/modules/ConfigCenter`
- `server/modules/Audit`
- `server/modules/Files`

### 前端
- `frontend/package.json`
- `frontend/packages/shell`
- `frontend/packages/core`
- `frontend/packages/sdk`
- `frontend/packages/ui`

### 测试
- `tests/Kernel.Tests`
- `tests/Authorization.Tests`
- `tests/ConfigCenter.Tests`
- `tests/Audit.Tests`
- `tests/Files.Tests`

### 解决方案
- `ICP-Platform.sln`

因此，不要再假设“还没有工程文件”。

## 6. 第二阶段推荐任务顺序

如果你要连续推进第二阶段，请按这个顺序做：

1. 后端 Host 骨架与模块契约
2. 前端 Shell 与前端 SDK
3. 最小登录能力
4. 默认管理员与种子数据
5. 登录后主界面与权限守卫闭环
6. Demo Module 端到端贯通
7. 最小可运行平台验证
8. 验证与修复

对应提示词：
- `docs/prompts/04_后端模块契约与Host骨架.md`
- `docs/prompts/05_前端Shell与前端SDK.md`
- `docs/prompts/05_1_最小登录与身份能力.md`
- `docs/prompts/05_2_默认管理员与种子数据.md`
- `docs/prompts/05_3_登录后主界面与权限守卫闭环.md`
- `docs/prompts/07_DemoModule端到端贯通.md`
- `docs/prompts/07_1_最小可运行平台验证.md`
- `docs/prompts/12_验证与修复.md`

## 7. 你应该如何理解各模块边界

### Identity Lite / LocalAuth

只负责：
- 登录
- 当前用户
- 退出登录
- 最小登录态机制
- 默认管理员种子账号

不负责：
- 完整用户中心
- 组织树
- SSO / LDAP / OAuth 深度接入

### Authorization

只负责：
- 权限点
- 授权检查
- 菜单/路由权限控制

不负责：
- “当前是谁”的登录识别

### ConfigCenter

只负责：
- 配置项
- 字典
- 枚举承载入口
- 轻量导入能力

不负责：
- 登录逻辑
- 审计逻辑

### Audit

只负责：
- 访问日志
- 操作日志
- 统一审计写入与查询入口

不负责：
- 权限判断
- 配置管理

## 8. 必须遵守的架构规则

### Host

Host 只负责：
- 启动
- 组合
- 认证上下文接入
- 路由、菜单、权限声明聚合
- 模块加载

Host 不负责：
- 模块业务逻辑
- 权限细节
- 审计存储细节
- 配置中心逻辑

### 模块

模块必须：
- 通过契约接入
- 不直接修改 Shell 内部
- 不跨模块直接依赖具体基础设施实现

### 迁移

迁移必须：
- 先分类，再实施
- 先文档化，再落代码
- 不允许把旧框架启动逻辑直接搬进新宿主

## 9. 本阶段绝对不要做的事

不要做：
- 把任务扩大成完整身份平台
- 顺手引入 WinApp、Studio、文件服务重写
- 顺手兼容大量旧 `.aspx` 页面
- 把旧 `OldP` 代码整包复制进来
- 同时混做“架构重构 + 功能新增 + 迁移改造”

## 10. 你每次执行任务前都要自问

开始前先确认：

1. 这个任务是否在 `docs/scope-v1.md` 范围内？
2. 这个任务是否有明确成功标准？
3. 这轮是否真的需要改代码？
4. 这轮有没有无意间扩大成完整平台设计？
5. 这轮完成后，最小可运行平台闭环是不是更近了一步？

如果答案不清楚，先收敛任务，再动手。

## 11. 你每次完成任务后必须输出什么

必须报告：
- 改了什么
- 影响了哪些文件和目录
- 执行了哪些验证
- 哪些通过，哪些没通过
- 当前剩余风险
- 下一步建议

## 12. 推荐给 Claude Code 的工作方式

### 单线程方式

适合：
- 当前任务边界清楚
- 需要连续实现

优先使用：
- 一个主线程
- 末尾再统一验证

### subagents 方式

如需并行辅助，请使用：
- `docs/prompts/13_通用subagents触发模板.md`

子代理尽量只做：
- 只读探索
- 契约审查
- 文档核对
- 最后阶段测试修复

### worktree 方式

如需拆分大任务，请使用：
- `docs/prompts/14_通用Worktree任务模板.md`

建议拆法：
- `host-foundation`
- `shell-login`
- `demo-module`

## 13. 常用验证命令

根据任务至少尝试：

```bash
# 后端
dotnet build
dotnet test

# 前端
pnpm install
pnpm build
pnpm test
```

如果任务涉及启动闭环，还应验证：
- Host 能启动
- 登录页可访问
- 默认管理员可登录
- 登录后能看到主界面
- Demo 页面可访问

## 14. 参考旧平台时的原则

需要参考 `OldP` 时：
- 先查 `../OldP/ApiDoc/`
- 再查 `../OldP/Source/`
- 再查 `../OldP/Runtime/`

但必须记住：
- 旧代码是参考来源，不是复制来源
- 旧元数据是迁移输入，不是默认原样保留对象

## 15. 第二阶段完成后才允许做什么

只有当“最小可运行平台”闭环完成后，才建议继续推进：
- `Authorization` 深化
- `ConfigCenter` 导入 POC
- `Audit` 深化
- 元数据迁移策略落地

在此之前，不要把主力放到复杂模块上。

