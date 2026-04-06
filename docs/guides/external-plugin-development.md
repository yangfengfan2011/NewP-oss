# 外部插件开发指南

本文档说明如何基于公开 SDK 和外部插件模板开发最小后端插件。

## 目标

外部插件开发路径的目标是：

- 不修改宿主源码
- 不依赖内建模块实现
- 只依赖公开 SDK / Contracts
- 产出可被宿主插件目录识别的标准插件包

## 推荐模板

模板目录：

- `server/templates/external-plugin-template`

脚手架生成：

```powershell
.\scripts\new-module.ps1 `
  -ModuleName PartnerSample `
  -DisplayName "合作方示例插件" `
  -ExternalPlugin `
  -OutputPath external-plugins/PartnerSample
```

```bash
./scripts/new-module.sh \
  --module-name PartnerSample \
  --display-name "合作方示例插件" \
  --external-plugin \
  --output-path external-plugins/PartnerSample
```

## 生成结果

外部模式当前只生成后端插件骨架，包含：

- 模块入口
- 最小状态服务
- 最小状态接口
- 工程文件
- README

不会做：

- `HostApi.csproj` 接线
- 前端模块生成
- Shell 注册

## 开发约束

- 模块必须继续继承 `ModuleBase`
- 只能通过 `IPermissionProvider`、`IMenuProvider`、`IConfigProvider` 等公开契约声明扩展点
- 不应直接引用宿主实现或内建模块实现
- 不应假设自己可以修改宿主内部结构来完成接入

## 本地构建

```powershell
dotnet build .\external-plugins\PartnerSample\PartnerSample.csproj
```

## 打包

```powershell
.\scripts\pack-plugin.ps1 -ProjectPath .\external-plugins\PartnerSample\PartnerSample.csproj -Force
```

## 校验

```powershell
.\scripts\verify-plugin-package.ps1 -PackagePath .\artifacts\plugin-packages\PartnerSample
```

## 当前限制

- 当前默认仍使用仓库内 `server/sdk/Sdk.csproj` 作为开发期引用
- 对外分发的独立 SDK 包、版本治理和签名校验后续补齐
- 当前只覆盖后端插件，不包含前端资源打包
