# CI/CD 基础流水线设计

## 1. 背景

- 当前仓库 `.github/workflows/` 为空，代码提交后无自动验证
- AI 主导开发需要提交后自动反馈构建/测试状态，减少人工干预
- 项目已有 `scripts/build.ps1` 和 `scripts/test.ps1`，逻辑已沉淀但未自动化

## 2. 目标

- 实现 push / PR 触发的自动构建与测试
- 覆盖后端（dotnet build + dotnet test）和前端（pnpm install + pnpm build）
- 提供清晰的 pass/fail 信号

## 3. 非目标

- 不涉及部署（CD 部分）
- 不涉及密钥管理或基础设施编排
- 不涉及多数据库集成测试（仅 InMemory 单元测试）
- 不引入容器化构建
- 不添加代码覆盖率报告（后续可扩展）

## 4. 范围

- `.github/workflows/ci.yml`：主流水线
- 仅影响 CI 配置，不修改项目代码或脚本

## 5. 设计约束

- 目标框架 .NET 10.0，需使用支持 .NET 10 的 runner
- 前端使用 pnpm，需在 CI 中安装
- Node.js 版本需与项目一致（LTS 20+）
- 保持流水线简洁，不过度设计

## 6. 总体方案

使用 GitHub Actions，单 workflow 文件，分 3 个 job：

1. **backend-build-test**：dotnet restore → dotnet build → dotnet test
2. **frontend-build**：pnpm install → pnpm build
3. 两个 job 并行执行以加速

触发条件：
- `push` 到 `main` 分支
- 所有 Pull Request

### 为什么选 GitHub Actions

- 仓库已在 GitHub 上
- 无需额外基础设施
- 与 PR 状态检查原生集成

## 7. 核心流程

```
Push / PR → GitHub Actions 触发
  ├─ Job: backend-build-test (ubuntu-latest)
  │   ├─ Checkout
  │   ├─ Setup .NET 10.0
  │   ├─ dotnet restore
  │   ├─ dotnet build ICP-Platform.sln --no-restore
  │   └─ dotnet test ICP-Platform.sln --no-build
  │
  └─ Job: frontend-build (ubuntu-latest)
      ├─ Checkout
      ├─ Setup Node.js 20
      ├─ Setup pnpm
      ├─ pnpm install --frozen-lockfile
      └─ pnpm build
```

## 8. 错误处理与风险

- **风险**：.NET 10 preview SDK 在 GitHub runner 上可能不可用 → 使用 `actions/setup-dotnet` 指定版本
- **风险**：pnpm lockfile 不一致 → 使用 `--frozen-lockfile` 严格模式
- **回滚**：删除 workflow 文件即可彻底回滚，无副作用

## 9. 验证方案

- 创建 workflow 文件后，推送到分支触发执行
- 检查 GitHub Actions 面板中 job 状态
- 确认后端 build + test 均绿
- 确认前端 build 均绿

## 10. 实施拆分

- 步骤 1：创建 `.github/workflows/ci.yml`
- 步骤 2：推送验证
