# Phase 6 收口验证记录

## 目标

验证 Phase 6 是否已经满足“模板可复用、示例可联通、脚手架可执行”的阶段完成标准。

## fresh 验证命令

- `dotnet build .\server\modules\ModuleTemplateSample\ModuleTemplateSample.csproj`
- `dotnet build .\apps\host-api\HostApi.csproj`
- `dotnet build .\server\templates\backend-module-template\TemplateModule.csproj`
- `cd .\frontend`
- `pnpm --filter @icp-templates/frontend-module-template build`
- `pnpm --filter @icp-modules/module-template-sample build`
- `pnpm build`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\new-module.ps1 -ModuleName Phase6ScaffoldSmoke -SkipIntegration`
- `dotnet build .\server\modules\Phase6ScaffoldSmoke\Phase6ScaffoldSmoke.csproj`
- `pnpm --filter @icp-modules/phase6-scaffold-smoke build`
- `bash -lc "cd /mnt/d/Github/ICP-X/NewP && ./scripts/new-module.sh --module-name Phase6ShellSmoke --skip-integration"`
- `dotnet build .\server\modules\Phase6ShellSmoke\Phase6ShellSmoke.csproj`
- `pnpm --filter @icp-modules/phase6-shell-smoke build`

## 本次结果

- `ModuleTemplateSample` 后端项目构建成功
- `HostApi` 构建成功
- 后端模板项目 `TemplateModule.csproj` 构建成功
- 前端模板包构建成功
- `module-template-sample` 前端示例模块构建成功
- 前端工作区整体构建成功
- `new-module.ps1` 已真实生成 `Phase6ScaffoldSmoke`，生成后的前后端模块均构建成功
- `new-module.ps1` 默认集成路径已真实生成 `Phase6IntegrationSmoke`，并通过 `HostApi` 构建与新前端包构建验证
- `new-module.sh` 已在当前 Windows + WSL 环境下通过包装器调用 PowerShell 脚手架，真实生成 `Phase6ShellSmoke`，生成后的前后端模块均构建成功

## 备注

- 当前 `.sh` 验证过程中会输出一段 WSL 本地环境告警噪音，但命令本身返回成功，未影响脚手架生成与后续构建结果

## 结论

- Phase 6 已完成
- 后端模板、前端模板、真实端到端示例模块和可执行脚手架均已具备
- 下一步应转入 `Phase 6.5` 持久化底座收口
