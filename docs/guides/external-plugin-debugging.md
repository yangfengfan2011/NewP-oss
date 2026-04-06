# 外部插件调试指南

本文档说明当前最小外部插件的本地调试路径。

## 最小调试思路

当前推荐采用“先打包、再让宿主从插件目录装载”的方式调试，而不是修改宿主源码。

## 步骤

1. 生成外部插件骨架

```powershell
.\scripts\new-module.ps1 `
  -ModuleName PartnerSample `
  -DisplayName "合作方示例插件" `
  -ExternalPlugin `
  -OutputPath external-plugins/PartnerSample
```

2. 构建插件

```powershell
dotnet build .\external-plugins\PartnerSample\PartnerSample.csproj
```

3. 打包插件

```powershell
.\scripts\pack-plugin.ps1 -ProjectPath .\external-plugins\PartnerSample\PartnerSample.csproj -Force
```

4. 校验插件包

```powershell
.\scripts\verify-plugin-package.ps1 -PackagePath .\artifacts\plugin-packages\PartnerSample
```

5. 指向宿主插件目录启动

```powershell
$env:Platform__ModulesPath = (Resolve-Path .\artifacts\plugin-packages)
dotnet run --project .\apps\host-api\HostApi.csproj
```

## 观察点

启动后重点关注：

- 插件发现根目录日志
- `loadable plugins` / `skipped plugins`
- 模块、权限、菜单聚合日志

如果插件未装上，优先检查：

- `plugin.json` 是否存在
- `entryAssembly` 是否正确
- 宿主版本兼容范围是否匹配
- 插件工程是否仍只依赖公开 SDK

## 当前已知限制

- 宿主仍有少量内建依赖，例如本地登录种子会要求 `Authorization` 插件存在
- 因此单独调试某些插件时，可能需要同时放入最小依赖插件包组合
