# ModuleTemplateSample 兼容样本

该模块保留早期 `ModuleBase`、代码内身份/权限/配置声明和前后端贯通样例，只用于回归兼容行为，不是新增模块模板。

新增模块使用：

```powershell
pwsh -NoProfile -File .\scripts\new-module.ps1 -ModuleName ExampleModule -DisplayName "示例模块"
```

当前模板位于 `server/templates/backend-module-template` 与 `frontend/templates/frontend-module-template`，开发边界见 `docs/developer/componentized-development.md`。
