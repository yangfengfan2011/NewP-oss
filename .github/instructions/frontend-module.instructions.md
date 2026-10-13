---
description: "Use when creating or modifying frontend modules in frontend/modules/. Covers FrontendModule contract, route/menu/permission declaration, and Shell integration."
applyTo: "frontend/modules/**"
---
# 前端模块开发规则

## 模块结构

```
frontend/modules/<module-name>/
├── src/
│   ├── index.ts                    # 导出 FrontendModule 对象
│   ├── pages/                      # 页面组件
│   └── services/                   # API 调用层
├── package.json
├── tsconfig.json
└── dist/
```

## FrontendModule 契约

每个模块必须导出符合 `FrontendModule` 接口的默认对象，包含：

- `metadata`：moduleId / displayName / version
- `routes`：路由配置（path / component / permissions）
- `menus`：菜单配置（id / label / path / icon / order / permissions / children）
- `permissions`：权限声明（code / name / category）
- `i18n`（可选）：国际化资源
- `initialize()` / `cleanup()`（可选）：生命周期钩子

## 路由约定

- 路径使用 kebab-case：`/authorization/roles`
- 路径以模块 ID 为前缀：`/<module-id>/...`
- 需要认证的路由设置 `requireAuth: true`
- 需要权限的路由设置 `permissions` 数组

## 菜单约定

- `order` 控制排序，数字越小越靠前
- 子菜单通过 `children` 嵌套
- 菜单项的 `permissions` 控制可见性

## 边界规则

- 依赖 `@icp-platform/sdk` 和 `@icp-platform/core`
- 不直接修改 Shell 内部代码
- 不引入全局可变状态
- 不跨模块直接 import 其他模块的内部实现

## Shell 集成

- 开发模式：模块通过 `import.meta.glob` 自动发现
- 生产模式：通过 `frontend-plugin-manifest.json` 清单加载
- 内建模块在 `frontend/packages/shell/src/builtin-runtime-plugins.ts` 注册

## 参考示例

- 最简示例：`frontend/modules/example/demo/`
- 完整示例：`frontend/modules/platform/authorization/`
- 模板：`frontend/templates/frontend-module-template/`
