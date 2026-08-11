# 内建模块五分钟接入

## 1. 生成

```powershell
pwsh -NoProfile -File .\scripts\new-module.ps1 -ModuleName ExampleModule -DisplayName "示例模块"
```

只生成文件、暂不接入 HostApi 时增加 `-SkipIntegration`。

## 2. 修改三个所有者

- `modules/ExampleModule/newp.plugin.json`：身份、版本、依赖和 behavior key。
- `modules/ExampleModule/metadata/**`：路由、菜单、权限引用、模型和本地化。
- `server/modules/ExampleModule/` 与 `frontend/modules/example-module/`：只实现对应 behavior binding 和业务能力。

前端 `src/index.ts` 导出 `FrontendBehaviorBindings`，不声明 `FrontendModule`、路由、菜单或权限。

## 3. 验证

```powershell
dotnet build .\server\modules\ExampleModule\ExampleModule.csproj
dotnet build .\apps\host-api\HostApi.csproj
corepack pnpm --dir frontend --filter @icp-modules/example-module build
```

最后确认 Capsule 能通过 `CanonicalModulePlanCompiler`，且 Shell 能从 `/plugins/active-module-runtime.json` 解析全部 binding。

完整边界见[组件化开发指南](componentized-development.md)和[组件化护栏](../architecture/componentization-guardrails.md)。
