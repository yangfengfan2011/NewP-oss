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

推荐先从仓库模板复制新模块骨架：

```powershell
Copy-Item .\frontend\templates\frontend-module-template .\frontend\modules\example-module -Recurse
```

模块的受控接入点是可提交的根 capsule 与 metadata：

- `modules/<Module>/newp.plugin.json`（身份、版本、依赖与 behavior key）
- `modules/<Module>/metadata/**`（路由、菜单、权限引用与模型）

Shell 只消费 Host 编译的 `active-module-runtime.json`，常规模块接入不再修改 Shell 内部注册表。

仓库里也提供 `scripts/new-module.ps1` 与 `scripts/new-module.sh` 作为后续自动化便利，但它们是建立在模板契约之上的扩展，不替代模板本身。

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
