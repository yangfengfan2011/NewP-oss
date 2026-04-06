# 前端架构说明

## 概览

当前前端采用模块化结构，Shell 负责装配，SDK 负责定义模块契约，各业务模块通过显式注册接入宿主。

## 目录结构

```text
frontend/
├── packages/
│   ├── shell/      # 前端宿主
│   ├── sdk/        # 前端模块契约
│   ├── core/       # 核心前端能力
│   └── ui/         # 共享 UI 组件（预留）
├── modules/
│   ├── demo/
│   ├── files/
│   └── module-template-sample/
├── templates/
│   └── frontend-module-template/
└── pnpm-workspace.yaml
```

## 当前阶段能力

- Shell 可装配多个前端模块。
- `frontend/templates/frontend-module-template` 提供可构建的前端模块模板。
- `frontend/modules/module-template-sample` 提供真实端到端示例模块。
- `scripts/new-module.ps1` 与 `scripts/new-module.sh` 提供最小模块脚手架。

## 新模块接入

推荐直接使用仓库脚手架：

```powershell
.\scripts\new-module.ps1 -ModuleName ExampleModule
```

脚手架默认会：

- 生成 `frontend/modules/example-module`
- 生成对应的后端模块目录
- 更新 `frontend/packages/shell/src/builtin-runtime-plugins.ts`

如果只想生成骨架，不自动修改宿主接入点，可以加 `-SkipIntegration` 或 `--skip-integration`。

## 构建验证

```bash
cd frontend
pnpm --filter @icp-templates/frontend-module-template build
pnpm --filter @icp-modules/module-template-sample build
pnpm build
```

## 设计约束

- 模块接入保持显式注册，不做隐式扫描。
- 模块目录命名使用 kebab-case。
- 模块说明和新增注释统一使用中文。
- Phase 6 目标是“模板可复用、示例可联通、脚手架可执行”，不在这一层引入额外基础设施抽象。
