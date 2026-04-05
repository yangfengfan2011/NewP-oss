# 验证矩阵

本文档统一收拢当前仓库中“应该怎么验证”的口径。

## 阶段级验证

### Phase 1

- `dotnet build ICP-Platform.sln`
- `cd frontend && pnpm install`
- `cd frontend && pnpm build`

参考：

- [scaffold-verification.md](scaffold-verification.md)

### Phase 2

- 登录页可访问
- 默认管理员可登录
- 登录后进入 Shell
- 当前用户接口可用
- 受保护 Demo 页面可访问
- 登出后无法继续访问受保护页面

参考：

- [quick-verification.md](quick-verification.md)

### Phase 3

- Demo 模块可被后端发现
- Demo 模块可被前端注册
- 菜单、路由、权限可聚合显示

参考：

- [demo-module-verification.md](demo-module-verification.md)

## 模块级验证

### Kernel

- `dotnet test tests/Kernel.Tests/Kernel.Tests.csproj`

### Authorization

- `dotnet test tests/Authorization.Tests/Authorization.Tests.csproj`

### ConfigCenter

- `dotnet test tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj`

### Audit

- `dotnet test tests/Audit.Tests/Audit.Tests.csproj`

### Files

- `dotnet test tests/Files.Tests/Files.Tests.csproj`

## 前端验证

### 常规构建

```bash
cd frontend
pnpm build
```

### Clean 环境构建

```bash
cd frontend
pnpm clean
pnpm install --frozen-lockfile
pnpm build
```

## 建议使用方式

- 改架构/脚手架：优先看阶段级验证。
- 改模块实现：先跑模块级测试，再补阶段级 smoke。
- 改前端依赖或构建链：额外执行 clean 环境构建。
