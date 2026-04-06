# 插件打包指南

本文档说明当前后端插件包的最小打包与校验路径。

## 当前目标

当前阶段的目标不是完整插件市场或外部分发，而是先建立一条可复用的最小交付链路：

- 从一个后端模块工程产出可交付插件目录
- 自动生成 `plugin.json`
- 让包内至少包含程序集、清单和最小说明文件
- 在宿主接线之外独立校验包结构

## 产物结构

打包脚本默认输出到：

`artifacts/plugin-packages/<ModuleId>/`

当前最小产物包括：

- `plugin.json`
- `<AssemblyName>.dll`
- `README.md`

如果存在，也会额外复制：

- `<AssemblyName>.pdb`
- `<AssemblyName>.deps.json`

## PowerShell 打包

示例：打包 `Audit`

```powershell
.\scripts\pack-plugin.ps1 `
  -ModuleName Audit `
  -Force
```

示例：打包 `ModuleTemplateSample`

```powershell
.\scripts\pack-plugin.ps1 `
  -ModuleName ModuleTemplateSample `
  -Force
```

可选参数：

- `-ModuleName`
  适用于仓库内 `server/modules/<ModuleName>` 模块。
- `-ProjectPath`
  适用于显式指定模块工程路径。
- `-Configuration`
  默认为 `Release`
- `-OutputRoot`
  默认为 `artifacts/plugin-packages`
- `-HostVersionMin`
  默认取 `apps/host-api/appsettings.json` 中的 `Platform:HostVersion`
- `-HostVersionMax`
  默认按宿主主版本向上推一个大版本
- `-Force`
  覆盖已有包目录

## Shell 包装器

```bash
./scripts/pack-plugin.sh --module-name Audit --force
```

```bash
./scripts/pack-plugin.sh --project-path server/modules/Audit/AuditModule.csproj --force
```

## 包校验

PowerShell：

```powershell
.\scripts\verify-plugin-package.ps1 -PackagePath .\artifacts\plugin-packages\Audit
```

Shell：

```bash
./scripts/verify-plugin-package.sh --package-path artifacts/plugin-packages/Audit
```

当前校验项包括：

- `plugin.json` 存在
- `README.md` 存在
- 清单必填字段完整
- `entryAssembly` 指向的程序集文件存在

## 宿主安装约定

宿主当前按“插件根目录下每个子目录代表一个插件”进行发现。

因此安装时应把打出的插件目录整体复制到宿主 `ModulesPath` 根目录下，例如：

```text
<host-modules-root>/
  Audit/
    plugin.json
    AuditModule.dll
    README.md
```

开发期仓库内的 `apps/host-api/modules/*` 仍是内建模块清单目录，用于保持当前研发闭环；标准插件包会逐步替代这类开发期过渡目录。

## 当前限制

- 依赖冲突和依赖装配顺序还未纳入脚本治理
- 哈希/签名校验尚未加入
- 当前元数据主要从模块源码入口提取，因此默认要求模块入口继续保持 `ModuleBase` 约定
- 当前只覆盖后端插件包，不包含前端资源打包
