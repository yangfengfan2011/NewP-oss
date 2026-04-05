# ICP 新平台

这是 ICP 平台的新一代实现仓库，采用“平台主壳 + 内建模块 + 前后端 SDK”的模块化单体架构。

## 当前状态

- Phase 0 已完成：旧平台资产盘点、处置矩阵与 V1 范围冻结文档已形成
- Phase 1 已完成：后端 Host / Kernel / SDK、前端 Shell / Core / SDK 骨架已落地并可构建
- Phase 2 已完成：本地登录、默认管理员、登录闭环、受保护页面访问、前后端最小可运行链路已打通
- Phase 3 已完成基础接入：扩展契约、Demo 模块贯通、Shell 模块装配能力已具备
- Phase 4 已完成：Authorization、ConfigCenter、Audit、Files 已完成详细设计、实现说明、验证记录与测试/构建收口

更完整的项目现状见 [docs/current-status.md](docs/current-status.md)。

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
- 项目现状：[docs/current-status.md](docs/current-status.md)
- 架构与范围：[docs/architecture/](docs/architecture/)
- 阶段文档：[docs/phases/](docs/phases/)
- 模块文档：[docs/modules/](docs/modules/)
- 迁移文档：[docs/migration/](docs/migration/)
- 验证文档：[docs/verification/](docs/verification/)
- 开发与协作文档：[docs/guides/](docs/guides/)、[docs/collaboration/](docs/collaboration/)

## 当前推荐阅读顺序

1. [AGENTS.md](AGENTS.md)
2. [Plan.md](Plan.md)
3. [docs/current-status.md](docs/current-status.md)
4. [docs/architecture/scope-v1.md](docs/architecture/scope-v1.md)
5. [docs/architecture/architecture.md](docs/architecture/architecture.md)
6. [docs/architecture/module-contract.md](docs/architecture/module-contract.md)
7. [docs/architecture/module-boundaries-v1.md](docs/architecture/module-boundaries-v1.md)
8. [docs/phases/phase-2-minimum-runnable-plan.md](docs/phases/phase-2-minimum-runnable-plan.md)

## 说明

- `OldP/` 是旧平台参考来源，不是直接复制来源
- `docs/prompts/` 和 `docs/bootstrap/` 主要服务于协作与执行流程，不是产品主文档
- 后续新增文档请优先放入对应分类目录
