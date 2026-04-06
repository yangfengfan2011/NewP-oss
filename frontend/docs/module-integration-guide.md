# 前端模块接入指南

本文说明如何把新的前端模块接入当前平台，并与后端模块形成最小闭环。

## 推荐流程

### 1. 使用脚手架生成模块

在仓库根目录执行：

```powershell
.\scripts\new-module.ps1 -ModuleName ExampleModule
```

如果只想先生成文件，不自动更新宿主接入点，可以使用：

```powershell
.\scripts\new-module.ps1 -ModuleName ExampleModule -SkipIntegration
```

Linux 或 macOS 环境可使用：

```bash
./scripts/new-module.sh --module-name ExampleModule
./scripts/new-module.sh --module-name ExampleModule --skip-integration
```

### 2. 查看生成结果

脚手架默认会生成两个目录：

```text
server/modules/ExampleModule
frontend/modules/example-module
```

其中前端模块默认包含：

- 一个最小模块入口
- 一个最小页面
- 一个最小服务文件
- 一个默认请求 `/api/example-module/status` 的联通验证逻辑

### 3. 默认宿主接入

未指定跳过集成时，脚手架还会自动更新：

- `apps/host-api/HostApi.csproj`
- `frontend/packages/shell/src/builtin-runtime-plugins.ts`

这样新模块生成后即可参与宿主构建。

## 手动接入方式

如果不使用自动集成，至少需要手动完成以下两步：

### HostApi 接入

在 `apps/host-api/HostApi.csproj` 中添加：

```xml
<ProjectReference Include="..\..\server\modules\ExampleModule\ExampleModule.csproj" />
```

### Shell 接入

在 `frontend/packages/shell/src/builtin-runtime-plugins.ts` 中添加：

```typescript
export const BUILTIN_RUNTIME_PLUGIN_ENTRIES = [
  // ...
  { moduleId: 'example-module' },
];
```

## 验证命令

建议至少执行以下命令：

```powershell
dotnet build .\server\modules\ExampleModule\ExampleModule.csproj
dotnet build .\apps\host-api\HostApi.csproj
cd frontend
pnpm --filter @icp-modules/example-module build
pnpm build
```

## 说明

- 前端模块目录使用 kebab-case。
- 后端模块目录使用 PascalCase。
- 默认权限编码使用 `<模块名>.View`。
- 默认接口前缀使用 `/api/<kebab-case-module-name>`。
## Controlled Integration Guardrails

- Normal frontend module integration may update `frontend/packages/shell/src/builtin-runtime-plugins.ts`.
- Do not directly modify shell layout, global provider structure, or internal route composition for routine module access.
- If a module really needs more than the controlled entry point, add or update a design doc first.

## Minimum Verification

```powershell
dotnet build .\server\modules\ExampleModule\ExampleModule.csproj
dotnet build .\apps\host-api\HostApi.csproj
cd frontend
pnpm --filter @icp-modules/example-module build
pnpm build
```

The expected path is: the module builds and integrates without modifying other Shell internals.
