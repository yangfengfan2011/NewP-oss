# 前端模块模板规范

本文定义当前仓库新增前端模块时应遵循的模板契约、目录边界和最小验证方式。

## 目标

- 让新增前端模块按 `FrontendModule` 契约接入宿主。
- 固化模板目录结构、必改项和验证命令。
- 避免模块直接依赖宿主内部实现，或把后续自动化能力反向写进模板边界。

## 标准目录

```text
frontend/templates/frontend-module-template/
├── src/
│   ├── index.ts
│   └── pages/
│       └── TemplatePage.tsx
├── package.json
├── tsconfig.json
└── README.md
```

## 必改项

- `package.json` 中的包名
- `metadata.moduleId`
- `metadata.displayName`
- 路由前缀与菜单路径
- 权限编码
- 菜单 `id`

## 可选目录

- `src/components/`
- `src/services/`
- `src/assets/`

只有在模块出现真实需要时才新增这些目录，不默认生成。

## 禁止事项

- 直接引用 `frontend/packages/shell/src/*`
- 把路由、菜单、权限和页面实现混堆到一个超大入口文件
- 默认要求新模块依赖 `@icp-platform/ui`
- 把 live API 联通逻辑写成模板必备内容

如果模块确实使用公共 UI，必须在模块自己的 `package.json` 中显式声明 `@icp-platform/ui` 依赖。

## 宿主接入点

- authoring 入口：`modules/<Module>/newp.plugin.json` 与 `modules/<Module>/metadata/**`
- 运行时消费：Host 编译 `active-module-runtime.json`，Shell 原子加载其声明的全部前端入口

常规前端模块接入只应修改自有 capsule、metadata 和 behavior binding，不直接改 Shell 布局、Provider 结构或内部路由拼装逻辑。

## 推荐标准

- 前端模块目录使用 kebab-case
- 前端包名优先使用 `@icp-modules/<module-name>`
- 权限编码建议使用 `<Module>.<Resource>.<Action>` 或至少 `<Module>.View`
- 路由前缀应与模块目录语义保持一致

## 最小验证

```bash
cd frontend
pnpm --filter @icp-templates/frontend-module-template build
pnpm --filter @icp-modules/<your-module-name> build
pnpm build
```

## 后续自动化说明

仓库中的示例模块和脚手架可以继续作为后续效率工具演进，但它们建立在这份模板契约之上，不替代这份模板规范本身。
