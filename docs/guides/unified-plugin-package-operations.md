# 统一插件包运维流程

本文档描述 Phase 1 统一插件包当前已落地的仓库内运维闭环：打包、校验、安装、启用、禁用、回滚，以及如何检查运行视图是否已同步重建。

## 当前范围

当前闭环聚焦统一插件包的最小可验证路径，不扩展到签名、卸载、审计记录或完整依赖治理。

当前仓库内实际入口：

- `scripts/pack-unified-plugin.ps1` / `scripts/pack-unified-plugin.sh`
- `scripts/verify-unified-plugin-package.ps1` / `scripts/verify-unified-plugin-package.sh`
- `scripts/manage-unified-plugin.ps1` / `scripts/manage-unified-plugin.sh`
- `tools/PluginOps/PluginOps.csproj`

当前限制：

- Phase 1 打包脚本当前只支持 `Audit`
- 当前没有运行中宿主的热刷新入口，宿主已启动时仍需重启以重新装载后端程序集
- 安装目录默认使用仓库内 `artifacts/installed-plugins`

## 安装目录约定

统一插件包安装后，目录结构如下：

```text
<install-root>/
  packages/
    <ModuleId>/
      <Version>/
        plugin.json
        backend/
        frontend/
        checksums/
  state/
    <ModuleId>.json
  runtime/
    backend-load-view/
      <ModuleId>/
        plugin.json
        <entry-assembly>.dll
    frontend-plugin-manifest.json
  public/
    <ModuleId>/
      <Version>/
        manifest.json
        assets/
```

说明：

- `packages/` 保存已安装包的版本化原始产物
- `state/` 保存模块级安装和启用状态
- `runtime/` 是宿主后端装载视图和前端运行时清单
- `public/` 是按宿主静态资源路径投影后的前端资源目录

## 标准操作顺序

1. 打包统一插件包
2. 校验统一插件包
3. 安装到标准安装目录
4. 启用指定版本
5. 检查运行视图与前端资源投影
6. 若宿主已运行，重启宿主让后端程序集按新视图重装载

## 打包

PowerShell：

```powershell
.\scripts\pack-unified-plugin.ps1 -ModuleName Audit -Configuration Release -Force
```

Shell：

```bash
./scripts/pack-unified-plugin.sh --module-name Audit --configuration Release --force
```

产物默认输出到：

`artifacts/plugin-packages/Audit`

## 校验

PowerShell：

```powershell
.\scripts\verify-unified-plugin-package.ps1 -PackagePath .\artifacts\plugin-packages\Audit
```

Shell：

```bash
./scripts/verify-unified-plugin-package.sh --package-path artifacts/plugin-packages/Audit
```

成功时输出：

`=== Unified plugin package verified ===`

## 安装

PowerShell：

```powershell
.\scripts\manage-unified-plugin.ps1 `
  -Action install `
  -PackagePath .\artifacts\plugin-packages\Audit `
  -InstallRoot .\artifacts\installed-plugins
```

Shell：

```bash
./scripts/manage-unified-plugin.sh \
  --action install \
  --package-path artifacts/plugin-packages/Audit \
  --install-root artifacts/installed-plugins
```

成功时会输出：

`Audit:1.0.0:Installed`

安装不会自动切换当前启用版本。

如果当前模块已经启用了同一版本，而你重新安装了该版本的新包内容，PluginOps 会在安装成功后同步重建：

- `runtime/backend-load-view`
- `runtime/frontend-plugin-manifest.json`
- `public/<ModuleId>/<Version>/...`

这样可以避免“状态仍指向当前版本，但运行时投影和静态资源还是旧内容”的漂移。

## 启用

PowerShell：

```powershell
.\scripts\manage-unified-plugin.ps1 `
  -Action enable `
  -ModuleId Audit `
  -Version 1.0.0 `
  -InstallRoot .\artifacts\installed-plugins
```

Shell：

```bash
./scripts/manage-unified-plugin.sh \
  --action enable \
  --module-id Audit \
  --version 1.0.0 \
  --install-root artifacts/installed-plugins
```

启用成功后，PluginOps 会立即重建：

- `runtime/backend-load-view`
- `runtime/frontend-plugin-manifest.json`
- `public/<ModuleId>/<Version>/...`

成功时输出：

`Audit:1.0.0:Enabled`

## 禁用

```powershell
.\scripts\manage-unified-plugin.ps1 -Action disable -ModuleId Audit -InstallRoot .\artifacts\installed-plugins
```

```bash
./scripts/manage-unified-plugin.sh \
  --action disable \
  --module-id Audit \
  --install-root artifacts/installed-plugins
```

禁用成功后，当前启用版本会被清空，并同步重建运行视图。

## 回滚

```powershell
.\scripts\manage-unified-plugin.ps1 -Action rollback -ModuleId Audit -InstallRoot .\artifacts\installed-plugins
```

```bash
./scripts/manage-unified-plugin.sh \
  --action rollback \
  --module-id Audit \
  --install-root artifacts/installed-plugins
```

回滚成功后，当前启用版本会切回上一个已安装版本，并同步重建运行视图。

## 结果检查

安装并启用后，至少应检查以下路径：

- `artifacts/installed-plugins/state/Audit.json`
- `artifacts/installed-plugins/runtime/backend-load-view/Audit/plugin.json`
- `artifacts/installed-plugins/runtime/frontend-plugin-manifest.json`
- `artifacts/installed-plugins/public/Audit/1.0.0/manifest.json`
- `artifacts/installed-plugins/public/Audit/1.0.0/assets/entry.js`

如果要让 HostApi 使用这套安装结果启动，需要把宿主配置中的 `Platform:PluginInstallRoot` 指向同一安装目录，例如：

```powershell
$env:Platform__PluginInstallRoot = (Resolve-Path .\artifacts\installed-plugins)
dotnet run --project .\apps\host-api\HostApi.csproj
```

宿主会在启动时再次按安装状态重建运行视图，并从：

- `/plugins/frontend-plugin-manifest.json`
- `/plugin-assets/...`

对外提供前端运行时入口。

## 失败语义

- `install` 失败时，不会返回 `Installed`
- `enable` / `disable` / `rollback` 失败时，不会保留半更新的运行视图
- 若运行视图重建失败，PluginOps 会尝试恢复上一个插件状态，并返回失败

这保证当前 Phase 1 闭环里，启用态切换不会静默漂移到“状态已切换、运行视图未切换”的中间态。

## 网站运维入口（Plugin Operations Center）

除脚本与 `PluginOps` CLI 以外，当前仓库还提供了面向管理员的网页入口：

- 页面路由：`/plugin-ops`
- 后端入口：`/api/plugin-ops/*`
- 访问边界：页面要求登录，真正的管理员边界仍由后端 `AdminOnly` 策略控制

当前网页运维闭环如下：

1. 管理员登录 Shell 后进入“插件运维中心”
2. 页面调用 `GET /api/plugin-ops/modules` 加载当前模块状态
3. 管理员上传统一插件包，页面调用 `POST /api/plugin-ops/uploads/verify`
4. 校验通过后，页面可继续调用 `POST /api/plugin-ops/uploads/{uploadId}/install`
5. 对已安装模块，页面可继续执行：
   `POST /api/plugin-ops/modules/{moduleId}/enable`
   `POST /api/plugin-ops/modules/{moduleId}/disable`
   `POST /api/plugin-ops/modules/{moduleId}/rollback`
6. 每次操作后，页面都会重新拉取模块状态列表并展示最近反馈

这套网页入口复用了现有统一插件包内核能力，不替代脚本和 CLI，而是把现有运维动作产品化为站内管理入口。
