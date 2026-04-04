# ICP 新平台

这是 ICP 平台的新一代实现，采用模块化单体架构。

## 目录结构

```text
/apps
  /host-api          # 后端宿主 API
  /admin-web         # 管理后台（预留）
  /module-playground # 模块演练场
/server
  /kernel            # 平台内核（抽象与契约）
  /modules           # 内建模块
    /Demo            # 演示模块
    /Authorization   # 权限授权（待实现）
    /ConfigCenter    # 配置中心（待实现）
    /Audit           # 审计（待实现）
  /sdk               # 服务端 SDK
/frontend
  /packages
    /shell           # 前端平台壳
    /sdk             # 前端 SDK
    /ui              # 共享 UI 组件（待实现）
    /core            # 核心前端能力（待实现）
/tests               # 测试
/docs                # 文档
/scripts             # 脚本
/.codex              # Codex 配置
```

## 快速开始

### 后端

```bash
# 恢复依赖
dotnet restore

# 构建
dotnet build

# 运行 Host API
cd apps/host-api
dotnet run

# 访问 Swagger
# https://localhost:5000/swagger
```

### 前端

```bash
cd frontend

# 安装依赖
pnpm install

# 启动开发服务器
pnpm dev

# 访问
# http://localhost:3000
```

## 当前状态

- ✅ 仓库骨架已建立
- ✅ 已完成范围、盘点、处置矩阵和模块边界文档
- ✅ 已明确第一阶段目标是“最小可运行平台”
- ⏳ 后端和前端骨架需要继续按文档实现和收口
- ⏳ 最小登录能力（Identity Lite / LocalAuth）待实现
- ⏳ Demo Module 全链路待实现
- ⏳ Authorization / ConfigCenter / Audit 模块待实现

## 第一阶段目标

第一阶段不是"重写整个平台"，而是完成：

1. ✅ 建立范围与路线约束
2. 🚧 建立新平台主干与模块契约
3. ⏳ 建立最小登录能力
4. ⏳ 打通一个 Demo Module 全链路
5. ⏳ 做出首批平台公共模块雏形

## V1 范围

### 纳入范围
- Host / Kernel / Contracts / SDK
- Frontend Shell
- Identity Lite / LocalAuth
- Demo Module
- Authorization / ConfigCenter / Audit
- 一个轻量旧资产导入 POC

### 明确排除
- WinApp 重写
- Studio 重写
- 文件服务重写
- 深度文件迁移
- 工作流重设计
- 报表与设计器重设计

详见 `docs/scope-v1.md`

## 核心文档

- `AGENTS.md` - 项目身份、架构规则、迁移规则
- `CLAUDE.md` - Claude Code 工作指引
- `Plan.md` - 分阶段实施计划
- `docs/scope-v1.md` - V1 范围冻结
- `docs/phase-2-minimum-runnable-plan.md` - 第二阶段实施计划
- `docs/module-boundaries-v1.md` - 首批模块边界总览
- `docs/architecture.md` - 架构概览
- `docs/module-contract.md` - 模块契约规范

## 架构原则

- 模块边界明确
- 优先使用扩展契约而非直接耦合
- 平台内核与产品逻辑分离
- 宿主组合，模块声明
- 保持变更可验证、可回滚
