# 前端工作区

前端使用 React、TypeScript、Vite 和 pnpm workspace。Shell 消费 Host 生成的活动模块运行时目录；模块实现通过稳定 behavior key 接入，不在 Shell 中维护业务模块清单。

## 目录

```text
packages/shell/      宿主布局、全局 Provider 与活动目录装配
packages/sdk/        仓内前端 behavior binding 与标准页面契约
packages/core/       HTTP、认证、授权和本地化基础能力
packages/ui/         共享 UI 控件与页面模式块
packages/plugin-sdk/ 外仓 standard-data.v1 build-only SDK
modules/             内建模块前端实现
templates/           新内建模块模板
```

## 新模块

从仓库根目录运行：

```powershell
pwsh -NoProfile -File .\scripts\new-module.ps1 -ModuleName ExampleModule -DisplayName "示例模块"
```

`frontend/modules/<module-id>/src/index.ts` 只导出 `FrontendBehaviorBindings`。模块身份、路由、菜单、权限和本地化由根 Capsule 与 metadata 拥有。完整规范见 `frontend/docs/module-integration-guide.md`。

## 验证

依赖安装统一走仓库 `scripts/bootstrap.ps1`。定向构建和仓库构建示例：

```powershell
corepack pnpm --dir frontend --filter @icp-modules/example-module build
corepack pnpm --dir frontend test
corepack pnpm --dir frontend build
```

普通模块不得引用 `packages/shell/src/*`，也不得复制 `FrontendModule` 兼容声明作为新模板。
