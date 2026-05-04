# 验证矩阵

本文档统一收拢当前仓库中“应该怎么验证”的口径。

## 阶段级验证

### Phase 1

- `dotnet build ICP-Platform.sln`
- `cd frontend && pnpm install`
- `cd frontend && pnpm build`

参考：

- [scaffold-verification.md](scaffold-verification.md)

### Phase 2

- 登录页可访问
- 默认管理员可登录
- 登录后进入 Shell
- 当前用户接口可用
- 受保护 Demo 页面可访问
- 登出后无法继续访问受保护页面

参考：

- [quick-verification.md](quick-verification.md)

### Phase 3

- Demo 模块可被后端发现
- Demo 模块可被前端注册
- 菜单、路由、权限可聚合显示

参考：

- [demo-module-verification.md](demo-module-verification.md)

### Phase 6.5

- `dotnet build .\apps\host-api\HostApi.csproj -v minimal`
- `dotnet test .\tests\Persistence.Tests\Persistence.Tests.csproj -v minimal`
- `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj -v minimal`
- `dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj -v minimal`
- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj -v minimal`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\integration\persistence\Run-SqlServerSmoke.ps1`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\integration\persistence\Run-MySqlSmoke.ps1`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\integration\persistence\Run-OracleSmoke.ps1`

参考：

- [phase-6-5-multi-db-persistence-verification.md](phase-6-5-multi-db-persistence-verification.md)

### Phase 6.6

- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj -v minimal`
- `dotnet build .\apps\host-api\HostApi.csproj -v minimal`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\pack-plugin.ps1 -ModuleName Audit -Force`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-plugin-package.ps1 -PackagePath .\artifacts\plugin-packages\Audit`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\pack-unified-plugin.ps1 -ModuleName Audit -Configuration Release -Force`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-unified-plugin-package.ps1 -PackagePath .\artifacts\plugin-packages\Audit`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\manage-unified-plugin.ps1 -Action install -PackagePath .\artifacts\plugin-packages\Audit -InstallRoot .\artifacts\installed-plugins`

参考：

- [phase-6-6-runtime-plugin-platform-verification.md](phase-6-6-runtime-plugin-platform-verification.md)

### Phase 6.7

- `pnpm --dir .\frontend\packages\shell test`
- `pnpm --dir .\frontend build`
- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter "FullyQualifiedName~PluginOperationsAdminServiceTests|FullyQualifiedName~PluginOperationsControllerTests|FullyQualifiedName~HostApiPluginManifestRouteTests" -nr:false -v minimal`
- `dotnet build .\apps\host-api\HostApi.csproj`

参考：

- [phase-6-7-plugin-operations-center-verification.md](phase-6-7-plugin-operations-center-verification.md)

### WP1

- `dotnet build tests/Kernel.Tests/Kernel.Tests.csproj -nr:false -v minimal`
- `dotnet build tools/PluginOps/PluginOps.csproj -nr:false -v minimal`
- `dotnet build apps/host-api/HostApi.csproj -nr:false -v minimal`
- `dotnet test tests/Kernel.Tests/Kernel.Tests.csproj --filter "FullyQualifiedName~PluginRuntimeAssemblyLoaderTests|FullyQualifiedName~PluginOperationsAdminServiceTests|FullyQualifiedName~UnifiedPluginPackageLifecycleOperationsTests|FullyQualifiedName~UnifiedPluginPackageInstallerTests|FullyQualifiedName~PluginOperationsLifecycleTests|FullyQualifiedName~PluginOperationsControllerTests|FullyQualifiedName~PluginOpsIntegrationTests" -nr:false -v minimal`
- `pnpm --dir frontend/packages/shell exec vitest --run src/__tests__/plugin-ops-client.test.ts src/__tests__/plugin-ops-page.test.tsx`
- `pnpm --dir frontend --filter @icp-platform/shell build`

参考：

- [plugin-governance-enhancement-verification.md](plugin-governance-enhancement-verification.md)

### WP2

- `dotnet test tests/Kernel.Tests/Kernel.Tests.csproj --filter FullyQualifiedName~AiFirstDesignValidationScriptTests -nr:false -v minimal`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path @('.\docs\designs\detailed-design-template.md', '.\docs\architecture\ai-first-execution-standard.md')`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1`

参考：

- [ai-first-execution-standard-phase-7-verification.md](ai-first-execution-standard-phase-7-verification.md)

### WP3

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\designs\post-phase-6-status-alignment-design.md`
- `powershell -NoProfile -Command "Select-String -Path '.\AGENTS.md','.\docs\phases\post-phase-6-stage-map.md','.\docs\verification\verification-matrix.md' -Pattern 'post-phase-6-stage-map|已完成并收口|phase-6-5-multi-db-persistence-verification|phase-6-6-runtime-plugin-platform-verification|phase-6-7-plugin-operations-center-verification' | ForEach-Object { '{0}:{1}:{2}' -f $_.Path, $_.LineNumber, $_.Line.Trim() }"`
- `powershell -NoProfile -Command "$paths = '.\docs\verification\phase-6-5-multi-db-persistence-verification.md','.\docs\verification\phase-6-6-runtime-plugin-platform-verification.md','.\docs\modules\plugin-ops-implementation.md','.\docs\verification\phase-6-7-plugin-operations-center-verification.md'; $missing = $paths | Where-Object { -not (Test-Path $_) }; if ($missing) { $missing; exit 1 }"`
- `git status --short -- AGENTS.md docs/phases/post-phase-6-stage-map.md docs/verification/verification-matrix.md docs/modules/plugin-ops-implementation.md docs/verification/phase-6-5-multi-db-persistence-verification.md docs/verification/phase-6-6-runtime-plugin-platform-verification.md docs/verification/phase-6-7-plugin-operations-center-verification.md docs/verification/post-phase-6-status-alignment-phase-1-verification.md docs/verification/post-phase-6-status-alignment-phase-2-verification.md`

参考：

- [post-phase-6-status-alignment-phase-1-verification.md](post-phase-6-status-alignment-phase-1-verification.md)
- [post-phase-6-status-alignment-phase-2-verification.md](post-phase-6-status-alignment-phase-2-verification.md)

### WP4 Stage 8

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\designs\dictionary-enumeration-migration-preparation-design.md`
- `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "FullyQualifiedName~LegacySettingImportServiceTests|FullyQualifiedName~ConfigServiceTests|FullyQualifiedName~ConfigCenterModelDefinitionTests" -nr:false -v minimal`
- `powershell -NoProfile -Command "$paths = 'OldP/Runtime/1_Server/Base/Config/Dictionary','OldP/Runtime/1_Server/Platform/Config/Dictionary','OldP/Runtime/1_Server/Platform/Config/Enumeration'; foreach($path in $paths){ if(Test-Path $path){ '{0}:FOUND' -f $path } else { '{0}:MISSING' -f $path } }"`
- `powershell -NoProfile -Command "Test-Path .\docs\collaboration\work-packages\wp4-stage-8-admission-review.md"`
- `powershell -NoProfile -Command "Test-Path .\resources\localization\languages.json"`

参考：

- [wp4-stage-8-admission-review-verification.md](wp4-stage-8-admission-review-verification.md)

### WP4 Stage 9

- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~DictionaryToI18nScriptTests -nr:false -v minimal`
- `powershell -NoProfile -ExecutionPolicy Bypass -Command "& '.\scripts\migration\dictionary-to-i18n.ps1' -InputPath @('D:\Github\ICP-X\OldP\Runtime\1_Server\Base\Config\Dictionary\Common.dic','D:\Github\ICP-X\OldP\Runtime\1_Server\Platform\Config\Dictionary\Platform.dic') -OutputRoot '.\resources\localization' -LanguagesConfigPath '.\resources\localization\languages.json'"`
- `powershell -NoProfile -Command "$commonCount = ((Get-Content '.\resources\localization\zhs\dictionary\Common.json' -Raw | ConvertFrom-Json) | Get-Member -MemberType NoteProperty | Measure-Object).Count; $platformCount = ((Get-Content '.\resources\localization\zhs\dictionary\Platform.json' -Raw | ConvertFrom-Json) | Get-Member -MemberType NoteProperty | Measure-Object).Count; \"CommonCount=$commonCount\"; \"PlatformCount=$platformCount\""`
- `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/localization-sample.test.tsx src/__tests__/home-page-shortcuts.test.tsx`
- `pnpm --dir .\frontend build`

参考：

- [wp4-stage-9-dictionary-static-resources-verification.md](wp4-stage-9-dictionary-static-resources-verification.md)

### WP4 Stage 10

- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~EnumerationToStaticResourcesScriptTests -nr:false -v minimal`
- `powershell -NoProfile -ExecutionPolicy Bypass -Command "& '.\scripts\migration\enumeration-to-static-resources.ps1' -EnumerationPath 'D:\Github\ICP-X\OldP\Runtime\1_Server\Platform\Config\Enumeration\Enumerations.enum' -DictionaryPath 'D:\Github\ICP-X\OldP\Runtime\1_Server\Platform\Config\Enumeration\Enumerations.dic' -OutputRoot '.\resources' -LanguagesConfigPath '.\resources\localization\languages.json'"`
- `powershell -NoProfile -Command "$structureCount = (Get-ChildItem '.\resources\enumerations' -Filter *.json -File | Measure-Object).Count; $zhsCount = (Get-ChildItem '.\resources\localization\zhs\enumerations' -Filter *.json -File | Measure-Object).Count; $enCount = (Get-ChildItem '.\resources\localization\en\enumerations' -Filter *.json -File | Measure-Object).Count; $gender = ((Get-Content '.\resources\localization\zhs\enumerations\Gender.json' -Raw | ConvertFrom-Json).'Gender.Female'); $selectModeExists = Test-Path '.\resources\localization\zhs\enumerations\SelectMode.json'; \"StructureCount=$structureCount\"; \"ZhsCount=$zhsCount\"; \"EnCount=$enCount\"; \"GenderFemale=$gender\"; \"SelectModeLocalizationExists=$selectModeExists\""`

参考：

- [wp4-stage-10-enumeration-static-resources-verification.md](wp4-stage-10-enumeration-static-resources-verification.md)

### Metadata Config Center Foundation

- `dotnet test .\tests\MetadataConfigCenter.Tests\MetadataConfigCenter.Tests.csproj -nr:false -v minimal`
- `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "ConfigServiceTests|LegacySettingImportServiceTests|ConfigCenterModuleTests" -nr:false -v minimal`
- `pnpm --filter @icp-platform/module-metadata-config-center build`
- `pnpm --filter @icp-platform/module-config-center build`
- `pnpm --filter @icp-platform/shell build`
- `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/registerModules-plugin-ops.test.ts`
- 按 [metadata-config-center-foundation-verification.md](metadata-config-center-foundation-verification.md) 中的 smoke 步骤，在 `Persistence__AutoMigrate=false` 下启动 `HostApi`，验证 `GET /metadata-config/modules`、`GET /metadata-config/modules/ConfigCenter/settings`、`PUT /metadata-config/modules/ConfigCenter/settings`

参考：

- [metadata-config-center-foundation-verification.md](metadata-config-center-foundation-verification.md)

### Enumeration 消费层 UI / 接入

- `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/localization-sample.test.tsx src/__tests__/enumeration-sample.test.tsx`
- `pnpm --dir .\frontend build`

参考：

- [enumeration-consumer-ui-integration-verification.md](enumeration-consumer-ui-integration-verification.md)

## 模块级验证

### Kernel

- `dotnet test tests/Kernel.Tests/Kernel.Tests.csproj`

### Authorization

- `dotnet test tests/Authorization.Tests/Authorization.Tests.csproj`

### ConfigCenter

- `dotnet test tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj`

### Audit

- `dotnet test tests/Audit.Tests/Audit.Tests.csproj`

### Files

- `dotnet test tests/Files.Tests/Files.Tests.csproj`

## 前端验证

### 常规构建

```bash
cd frontend
pnpm build
```

### Clean 环境构建

```bash
cd frontend
pnpm clean
pnpm install --frozen-lockfile
pnpm build
```

## 建议使用方式

- 改架构/脚手架：优先看阶段级验证。
- 改模块实现：先跑模块级测试，再补阶段级 smoke。
- 改前端依赖或构建链：额外执行 clean 环境构建。
