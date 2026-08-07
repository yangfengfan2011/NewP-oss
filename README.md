# ICP 新平台

这是 ICP 平台的新一代实现仓库，采用“平台主壳 + 内建模块 + 前后端 SDK”的模块化单体架构。

## 历史里程碑摘要

以下条目记录仓库已经走过的阶段，不代表当前路线图或新任务入口：

- Phase 0 已完成：旧平台资产盘点、处置矩阵与 V1 范围冻结文档已形成
- Phase 1 已完成：后端 Host / Kernel / SDK、前端 Shell / Core / SDK 骨架已落地并可构建
- Phase 2 已完成：本地登录、默认管理员、登录闭环、受保护页面访问、前后端最小可运行链路已打通
- Phase 3 已完成基础接入：扩展契约、Demo 模块贯通、Shell 模块装配能力已具备
- Phase 4 已完成：Authorization、ConfigCenter、Audit、Files 已完成详细设计、实现说明、验证记录与测试/构建收口
- Phase 5 已完成：首个旧资产迁移 POC 已落地，并形成可复跑的迁移模板交付
- Phase 6 已完成：后端和前端组件化模板已落地，端到端示例模块已验证，集成文档已完善

当前协作规则见 [AGENTS.md](AGENTS.md)。新任务以当前 GitHub issue/spec、当前源码与测试，以及按需触发的 Matt Pocock skill 为入口。

旧阶段图与状态流水分别保留在 [docs/phases/post-phase-6-stage-map.md](docs/phases/post-phase-6-stage-map.md) 和 [docs/current-status.md](docs/current-status.md)，仅供历史追溯。

## 仓库结构

```text
/apps
  /host-api
  /admin-web
  /module-playground
/server
  /kernel
  /sdk
  /modules
/frontend
  /packages
  /modules
/tests
/docs
/scripts
```

## 快速开始

### 初始化依赖

Windows:

```powershell
.\scripts\bootstrap.ps1
```

macOS / Linux:

```bash
./scripts/bootstrap.sh
```

### 一键启动开发环境

Windows:

```powershell
.\scripts\dev.ps1
```

macOS / Linux:

```bash
./scripts/dev.sh
```

停止开发环境：

Windows:

```powershell
.\scripts\stop-dev.ps1
```

macOS / Linux:

```bash
./scripts/stop-dev.sh
```

启动后默认访问地址：

- Host API: `http://localhost:5000`
- Frontend Shell: `http://localhost:3000`

默认本地账号：

- `admin / admin123`

## 常用命令

构建：

Windows:

```powershell
.\scripts\build.ps1
```

macOS / Linux:

```bash
./scripts/build.sh
```

测试：

Windows:

```powershell
.\scripts\test.ps1
```

macOS / Linux:

```bash
./scripts/test.sh
```

## 文档入口

- 文档总览：[docs/README.md](docs/README.md)
- 组件化开发指南：[docs/developer/componentized-development.md](docs/developer/componentized-development.md)
- 架构与范围：[docs/architecture/](docs/architecture/)
- 历史阶段文档：[docs/phases/](docs/phases/)
- 模块文档：[docs/modules/](docs/modules/)
- 历史迁移文档：[docs/migration/](docs/migration/)
- 验证文档：[docs/verification/](docs/verification/)
- 开发与协作文档：[docs/guides/](docs/guides/)、[docs/collaboration/](docs/collaboration/)

## 当前任务入口

1. 先阅读 [AGENTS.md](AGENTS.md)。
2. 再读取当前 GitHub issue/spec，并核对目标相关的源码、配置和测试。
3. 涉及 Issue、分流或领域文档时，按需读取 [docs/agents/](docs/agents/)。
4. 涉及 Host、Shell、Contracts、SDK 或模块接入时，读取 [组件化护栏](docs/architecture/componentization-guardrails.md)。
5. 只有追溯既有决策时，才读取历史设计、阶段、迁移和验证记录。

## 说明

- NewP 不以复刻、翻译或渐进替换旧平台为目标。`OldP/` 只在明确的迁移、兼容或行为取证任务中用于识别外部约束，不作为架构、数据模型、接口或 UI 的设计来源。
- `docs/prompts/`、`docs/bootstrap/`、`docs/superpowers/` 和旧协作文档仅保留历史流程证据，不作为当前工作流入口。
- `docs/current-status.md` 与 `docs/phases/post-phase-6-stage-map.md` 仅作为历史归档保留，不再作为当前事实源。
- 新规格与任务遵守 GitHub issue/spec 或已调用 skill 的产物约定；长期规则进入对应的架构、开发或上下文 ADR 文档。

## 组件化护栏

- 开发入口：[docs/developer/componentized-development.md](docs/developer/componentized-development.md)
- 完整护栏：[docs/architecture/componentization-guardrails.md](docs/architecture/componentization-guardrails.md)
- 新模块优先通过模板、`Contracts / SDK` 和受控入口接入。
- 常规前端模块接入只更新 `frontend/packages/shell/src/builtin-runtime-plugins.ts`。
- 任务需要修改其他 Host 或 Shell 内部实现时，先按 `AGENTS.md` 的设计门禁形成并确认设计工件。
