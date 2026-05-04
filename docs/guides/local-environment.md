# 本地开发环境

本文档给出当前 `NewP` 仓库的本地开发环境建议与最小启动步骤。

## 推荐版本

- .NET SDK: 8.x LTS
- Node.js: 20.x LTS 或团队统一的 LTS 版本
- 包管理器: `pnpm` 10.x

## 关键路径

- 解决方案文件：`ICP-Platform.sln`
- 后端入口：`apps/host-api`
- 前端工作区：`frontend`

## 首次启动

### 1. 后端

```bash
dotnet restore
dotnet build ICP-Platform.sln
cd apps/host-api
dotnet run
```

默认访问：

- `http://localhost:5000`
- `https://localhost:5001`

### 2. 前端

```bash
cd frontend
pnpm install
pnpm dev
```

默认访问：

- `http://localhost:3000`

## 默认账号

- 用户名：`admin`
- 密码：`admin123`

## 常用验证

### 后端构建与测试

```bash
dotnet build ICP-Platform.sln
dotnet test ICP-Platform.sln
```

### 前端构建

```bash
cd frontend
pnpm build
```

### 前端 clean check

```bash
cd frontend
pnpm clean
pnpm install --frozen-lockfile
pnpm build
```

## 推荐阅读

- 仓库规则：[../../AGENTS.md](../../AGENTS.md)
- 当前阶段图：[../phases/post-phase-6-stage-map.md](../phases/post-phase-6-stage-map.md)
- 快速验证：[../verification/quick-verification.md](../verification/quick-verification.md)
- 架构总览：[../architecture/architecture.md](../architecture/architecture.md)
