# CLAUDE.md

本文档为 Claude Code 在 `NewP` 仓库内工作时提供项目级协作指引。

## 1. 当前项目判断

当前仓库不是“纯文档仓库”或“概念性脚手架仓库”，而是已经具备真实后端、前端、模块、测试与迁移文档的开发仓库。

当前阶段判断如下：

- Phase 0 已完成：旧平台资产盘点、处置矩阵、V1 范围冻结已形成文档。
- Phase 1 已完成：后端 Host / Kernel / SDK 与前端 Shell / Core / SDK 主干已落地。
- Phase 2 已完成：本地登录、默认管理员、登录闭环、主界面壳、受保护页面访问已打通。
- Phase 3 已部分完成：扩展契约、Demo 模块贯通、Shell 模块装配已具备，但阶段总结仍需继续沉淀。
- Phase 4 已有首批内建模块实现：`Authorization`、`ConfigCenter`、`Audit`、`Files` 已有代码、测试和文档基础，仍在持续完善。

因此，后续工作默认不再围绕“证明骨架存在”展开，而是围绕：

- 细化模块设计
- 补齐阶段设计与阶段总结
- 完善模块实现
- 加固验证链路
- 有节奏地推进迁移跑道

## 2. 当前最重要的规则

### 详细设计先行

任何新的功能、模块、阶段性改造或行为变更，在正式实施前都必须先有详细设计文档。

没有详细设计文档时，只允许做：

- 信息收集
- 问题排查
- 方案讨论
- 设计草稿

不允许直接进入正式实现。

详细设计文档默认放在：

- `docs/designs/`

模板见：

- `docs/designs/detailed-design-template.md`

### 继续遵守模块边界

Host 只负责：

- 启动
- 组合
- 认证接入
- 路由、菜单、权限、配置描述符聚合
- 模块加载

Host 不负责：

- 模块业务逻辑
- 权限规则细节
- 配置中心内部逻辑
- 审计存储细节

模块必须通过契约接入，不能通过直接改 Shell 或直接跨模块耦合来实现功能。

### 迁移仍然要先文档化

旧平台相关工作仍然必须遵循：

1. 先盘点
2. 再分类
3. 再明确目标形态
4. 最后才实施迁移或重写

不要把旧平台代码、启动逻辑或元数据结构直接搬进新平台。

## 3. 推荐阅读顺序

开始具体工作前，优先阅读这些文件：

1. `AGENTS.md`
2. `README.md`
3. `Plan.md`
4. `docs/current-status.md`
5. `docs/README.md`
6. `docs/architecture/scope-v1.md`
7. `docs/architecture/architecture.md`
8. `docs/architecture/module-contract.md`
9. `docs/architecture/module-boundaries-v1.md`

如果任务涉及某个具体模块，再继续看：

- `docs/modules/<module>-boundary-v1.md`
- `docs/designs/<module>-design.md`
- `docs/modules/<module>-implementation.md`
- `docs/verification/verification-matrix.md`

如果任务涉及旧平台输入，再补充阅读：

- `docs/migration/oldp-asset-inventory-v1.md`
- `docs/migration/oldp-asset-disposition-v1.md`
- `docs/migration/migration-strategy.md`

## 4. 当前仓库里的真实结构

后端：

- `apps/host-api`
- `server/kernel`
- `server/sdk`
- `server/modules/Authorization`
- `server/modules/ConfigCenter`
- `server/modules/Audit`
- `server/modules/Files`
- `server/modules/Demo`

前端：

- `frontend/packages/shell`
- `frontend/packages/core`
- `frontend/packages/sdk`
- `frontend/packages/ui`
- `frontend/modules/authorization`
- `frontend/modules/config-center`
- `frontend/modules/audit`
- `frontend/modules/files`
- `frontend/modules/demo`

测试：

- `tests/Kernel.Tests`
- `tests/Authorization.Tests`
- `tests/ConfigCenter.Tests`
- `tests/Audit.Tests`
- `tests/Files.Tests`

解决方案与主入口：

- `ICP-Platform.sln`
- `frontend/package.json`

因此，不要再假设“仓库还没有真实代码或测试基础”。

## 5. 当前文档体系

文档目录已经重组，新增或更新文档时应优先放到正确分类下：

- `docs/architecture/`：范围、架构、契约、边界
- `docs/designs/`：详细设计
- `docs/phases/`：阶段计划与阶段总结
- `docs/modules/`：模块边界与实现概览
- `docs/migration/`：旧平台盘点、处置、迁移策略
- `docs/verification/`：构建、验证、验收矩阵
- `docs/guides/`：本地环境与开发指南
- `docs/collaboration/`：协作说明
- `docs/prompts/`、`docs/bootstrap/`：执行流程与协作资产

如果文档内容长期有效，优先进入分类目录；不要继续把核心文档散落在 `docs/` 根目录。

## 6. 当前应如何理解各模块边界

### Identity Lite / LocalAuth

负责：

- 登录
- 退出登录
- 当前用户识别
- 默认管理员与本地种子能力
- 最小登录态恢复

不负责：

- 完整身份平台
- 用户中心
- 组织树
- 单点登录、LDAP、OAuth、OIDC 深度集成

### Authorization

负责：

- 权限点定义
- 角色与权限关系
- 用户角色关系
- 后端授权判断
- 前端菜单、路由、页面级权限控制

不负责：

- 认证本身
- 完整组织和租户模型

### ConfigCenter

负责：

- 配置项模型
- 配置查询与修改
- 轻量 schema 同步
- 字典、枚举承载入口预留

不负责：

- 登录逻辑
- 审计实现
- 大而全配置发布平台

### Audit

负责：

- 访问日志
- 操作日志
- 审计写入与查询入口

不负责：

- 权限判断
- 配置管理

### Files

负责：

- 文件元数据与文件服务基础能力
- 文件访问入口与后续存储抽象扩展位

不负责：

- 旧文件服务的整体原样重写
- 与 WinApp/Studio 直接绑定的历史宿主能力

## 7. 当前阶段下不该做的事

当前默认不要做：

- 把局部任务膨胀成完整平台重设计
- 顺手引入 WinApp、Studio、旧文件服务重写
- 顺手兼容大量旧 `.aspx` 页面
- 直接复制 `OldP` 代码或旧启动逻辑
- 在没有详细设计的情况下直接写新功能
- 把多个模块的大改、迁移和新功能混在一次改动里

## 8. 每次开始工作前先自检

开始前先确认：

1. 这项工作是否在当前范围内。
2. 这项工作对应的详细设计文档是否已经存在。
3. 这次改动是实现、设计补齐，还是验证补齐。
4. 是否会破坏模块边界。
5. 是否需要同步更新实现文档、验证文档或阶段状态文档。

如果第 2 点答案是否定的，先补设计再实现。

## 9. 每次完成工作后必须给出的信息

输出中应明确说明：

- 改了什么
- 影响了哪些文件或模块
- 跑了哪些验证
- 哪些通过，哪些未通过
- 还剩什么风险
- 下一步建议是什么

## 10. 推荐验证习惯

根据改动范围，尽量执行这些检查：

```bash
# 后端
dotnet build ICP-Platform.sln
dotnet test

# 前端
cd frontend
pnpm install
pnpm build
```

如果改动涉及登录、权限、配置或模块装配，还应补充：

- Host 可启动
- Frontend Shell 可启动
- 默认管理员可登录
- `current-user` 可返回当前用户
- 登录后可见主界面
- 受保护页面权限行为符合预期

## 11. 参考旧平台时的原则

需要参考 `OldP` 时：

1. 先看 `../OldP/ApiDoc/`
2. 再看 `../OldP/Source/`
3. 最后看 `../OldP/Runtime/`

始终记住：

- 旧平台代码是参考来源，不是复制来源。
- 旧元数据是迁移输入，不是默认原样保留的目标模型。

## 12. 当前最值得继续补的文档

如果接下来继续整顿文档，优先方向是：

- Phase 3 阶段总结
- `Audit` 详细设计
- `Files` 详细设计
- Shell 模块装配 / 注册机制详细设计
- 更完整的迁移执行文档

以上内容应与 `docs/current-status.md`、`docs/README.md` 和各模块实现文档保持一致。

## 13. 提交约定

- 后续新增的 Git 提交信息默认使用简体中文，除非用户明确要求使用其他语言。
- 默认生成的设计、计划、说明等项目文档必须使用简体中文，除非用户明确要求使用其他语言。
