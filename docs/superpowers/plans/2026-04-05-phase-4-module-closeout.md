# Phase 4 模块收口实施计划

> **给代理执行者：** 必需子技能：使用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans`，按任务逐项实现本计划。步骤使用复选框语法（`- [ ]`）进行跟踪。

**目标：** 完成 `Phase 4` 的模块级收口工作，使 `Authorization`、`ConfigCenter`、`Audit`、`Files` 四个内建模块达到“设计、实现说明、测试、构建、运行验证”口径一致的可交付状态。

**架构：** 以“主线程统一口径，实施线程单模块收口”为原则推进。`Codex` 负责维护计划、状态、设计与最终验收；`Claude Code` 按单模块任务包实施允许范围内的代码、实现说明和验证记录变更，并运行任务内验证。`Plan.md`、`docs/current-status.md`、跨模块总口径设计文档默认仅由主线程维护。

**技术栈：** .NET、ASP.NET Core、xUnit、React、TypeScript、Vite、现有模块化单体结构、`Codex + Claude Code` 协同流程

---

### 任务 1：固定 Phase 4 收口矩阵与协同边界

**文件：**
- 新建：`docs/superpowers/plans/2026-04-05-phase-4-module-closeout.md`
- 参考：`Plan.md`
- 参考：`docs/current-status.md`
- 参考：`docs/collaboration/current-collaboration-mode.md`

- [ ] **步骤 1：确认执行顺序**

顺序固定为：

```text
Phase 4-0：统一收口矩阵
Phase 4A-1：Authorization 收口
Phase 4A-2：ConfigCenter 收口
Phase 4B-1：Audit 收口
Phase 4B-2：Files 收口
```

- [ ] **步骤 2：确认主线程文件边界**

以下文件默认只允许主线程维护：

```text
Plan.md
docs/current-status.md
docs/designs/*
docs/collaboration/*
```

- [ ] **步骤 3：确认实施线程可改范围**

实施线程每次只允许修改单个模块相关内容：

```text
server/modules/<Module>/**
frontend/modules/<module>/**
tests/<Module>.Tests/**
docs/modules/<module>-implementation.md
docs/verification/**
```

- [ ] **步骤 4：固定每个模块的统一验收标准**

统一验收标准如下：

```text
1. 详细设计存在并与当前实现不冲突
2. 实现说明与代码一致
3. 后端测试通过
4. 前端构建通过
5. 模块级 API / 集成 / smoke 验证记录存在
```

### 任务 2：收口 Authorization 模块

**文件：**
- 参考：`docs/designs/authorization-design.md`
- 修改：`docs/modules/authorization-implementation.md`
- 可新建：`docs/verification/authorization-module-verification.md`
- 可修改：`server/modules/Authorization/**`
- 可修改：`frontend/modules/authorization/**`
- 可修改：`tests/Authorization.Tests/**`

- [ ] **步骤 1：核对设计与实现说明**

逐项核对：

```text
权限点声明
角色管理
权限分配
用户角色
前端权限上下文与页面接入
```

将实现说明中的“已完成 / 未完成 / 刻意未做 / 已知限制”与当前代码对齐。

- [ ] **步骤 2：补模块级验证记录**

验证记录必须覆盖：

```text
后端 API 路径与核心能力
前端模块入口与页面可达性
角色、权限、用户角色的最小闭环
当前环境下实际跑过的测试与构建命令
```

- [ ] **步骤 3：运行模块验证**

运行：

```powershell
dotnet test tests/Authorization.Tests/Authorization.Tests.csproj
pnpm -C frontend build
```

预期：
- `Authorization.Tests` 通过
- 前端 workspace 构建通过

- [ ] **步骤 4：仅在验证发现问题时做最小修复**

允许修复内容：
- 实现说明与代码不一致
- 测试失效
- 明显的模块接线问题

禁止：
- 跨模块重构
- 修改 `Plan.md`
- 修改 `docs/current-status.md`

### 任务 3：收口 ConfigCenter 模块

**文件：**
- 参考：`docs/designs/configcenter-design.md`
- 修改：`docs/modules/configcenter-implementation.md`
- 可新建：`docs/verification/configcenter-module-verification.md`
- 可修改：`server/modules/ConfigCenter/**`
- 可修改：`frontend/modules/config-center/**`
- 可修改：`tests/ConfigCenter.Tests/**`

- [ ] **步骤 1：核对设计边界与实现说明**

重点核对：

```text
配置项 CRUD
模块 / 环境 / 租户维度组织
敏感配置脱敏
前端页面与模块注册
```

- [ ] **步骤 2：补完整验证记录**

验证记录必须说明：

```text
接口清单
前端入口
脱敏与维度组织的验证方式
当前仍未纳入的能力边界
```

- [ ] **步骤 3：运行模块验证**

运行：

```powershell
dotnet test tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj
pnpm -C frontend build
```

预期：
- `ConfigCenter.Tests` 通过
- 前端 workspace 构建通过

- [ ] **步骤 4：仅处理收口阻塞项**

允许修复：
- 测试/构建失败
- 模块文档与实现明显漂移
- 最小 API 接线问题

### 任务 4：收口 Audit 模块

**文件：**
- 如需新建：`docs/designs/audit-design.md`
- 修改：`docs/modules/audit-implementation.md`
- 可新建：`docs/verification/audit-module-verification.md`
- 可修改：`server/modules/Audit/**`
- 可修改：`frontend/modules/audit/**`
- 可修改：`tests/Audit.Tests/**`

- [ ] **步骤 1：补齐详细设计缺口**

设计至少应覆盖：

```text
访问日志
操作日志
与 Host 的挂钩点
与 Authorization 的衔接边界
当前不做的归档/高级筛选能力
```

- [ ] **步骤 2：同步实现说明**

把实现说明更新为“当前已有实现 + 当前缺口 + 下一阶段再做什么”的收口口径。

- [ ] **步骤 3：运行模块验证**

运行：

```powershell
dotnet test tests/Audit.Tests/Audit.Tests.csproj
pnpm -C frontend build
```

### 任务 5：收口 Files 模块

**文件：**
- 如需新建：`docs/designs/files-design.md`
- 修改：`docs/modules/files-implementation.md`
- 可新建：`docs/verification/files-module-verification.md`
- 可修改：`server/modules/Files/**`
- 可修改：`frontend/modules/files/**`
- 可修改：`tests/Files.Tests/**`

- [ ] **步骤 1：补齐详细设计缺口**

设计至少应覆盖：

```text
上传 / 下载
文件元数据
本地存储提供者抽象
后续对象存储扩展边界
当前不做的预览 / 分片 / 断点续传
```

- [ ] **步骤 2：同步实现说明与验证记录**

把“当前能做什么、依赖什么、还不能做什么”写清楚，并补实际验证命令与结果。

- [ ] **步骤 3：运行模块验证**

运行：

```powershell
dotnet test tests/Files.Tests/Files.Tests.csproj
pnpm -C frontend build
```

### 任务 6：主线程统一复核与阶段收口

**文件：**
- 修改：`Plan.md`
- 修改：`docs/current-status.md`
- 参考：`docs/verification/*.md`

- [ ] **步骤 1：逐模块复核 Claude 提交结果**

复核清单：

```text
是否越界改动
是否满足任务包 acceptance criteria
是否存在文档与代码再次脱节
是否引入跨模块副作用
```

- [ ] **步骤 2：重新做独立验证**

运行：

```powershell
dotnet test tests/Authorization.Tests/Authorization.Tests.csproj
dotnet test tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj
dotnet test tests/Audit.Tests/Audit.Tests.csproj
dotnet test tests/Files.Tests/Files.Tests.csproj
pnpm -C frontend build
```

- [ ] **步骤 3：主线程统一更新阶段口径**

只有在各模块完成复核后，才允许主线程更新：

```text
Plan.md
docs/current-status.md
必要的阶段总结文档
```

- [ ] **步骤 4：输出 Phase 4 当前结论**

结论必须明确：
- 哪些模块已经完成收口
- 哪些模块仍有残留风险
- 是否可以进入 Phase 5 首个迁移 POC
