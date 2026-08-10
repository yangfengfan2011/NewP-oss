# 前端架构说明

## 概览

当前前端采用模块化结构，Shell 负责运行时装配，SDK 定义 behavior binding 契约，各业务模块通过 Capsule 驱动的稳定 key 接入宿主。

## 目录结构

```text
frontend/
├── packages/
│   ├── shell/      # 前端宿主
│   ├── sdk/        # 前端模块契约
│   ├── core/       # 核心前端能力
│   └── ui/         # 共享 UI 控件与页面模式块
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
- `@icp-platform/ui` 已开始承载真实模块复用的基础控件与结构块。
- `frontend/modules/module-template-sample` 与 `scripts/new-module.*` 属于后续扩展能力，不是 2026-04-06 原始 Phase 6 模板闭环的核心验收项。

## 新模块接入

推荐使用脚手架一次生成根 Capsule、metadata 与前后端 behavior binding：

```powershell
pwsh -NoProfile -File .\scripts\new-module.ps1 -ModuleName ExampleModule -DisplayName "示例模块"
```

模块的受控 authoring 入口是：

- `modules/<Module>/newp.plugin.json`（身份、版本、依赖与 behavior key）
- `modules/<Module>/metadata/**`（路由、菜单、权限引用与模型）
- `frontend/modules/<module>/src/index.ts`（仅绑定稳定实现 key）

Shell 只消费 Host 编译的 `active-module-runtime.json`，常规模块接入不再修改 Shell 内部注册表。

只需要生成文件、暂不接入 HostApi 时使用 `-SkipIntegration`；它不会省略 Capsule 或 behavior binding。

## 构建验证

```bash
cd frontend
pnpm --filter @icp-platform/ui build
pnpm --filter @icp-templates/frontend-module-template build
pnpm build
```

## 设计约束

- 模块接入保持 capsule 显式声明，不在 Shell 中维护第二份清单。
- 模块目录命名使用 kebab-case。
- 模块说明和新增注释统一使用中文。
- Phase 6 原始目标是“模板可复用、构建链路可验证、接入边界清晰”。
- 示例模块、脚手架和更重的自动化能力可以继续演进，但不应反向改写模板阶段的边界定义。
