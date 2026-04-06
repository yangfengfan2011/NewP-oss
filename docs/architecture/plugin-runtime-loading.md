# 插件运行时装配

本文档说明宿主当前的后端插件运行时装配路径。

## 当前装配主线

宿主启动时采用如下步骤：

1. 读取 `Platform:ModulesPath`
2. 将其解析为宿主运行目录下的插件根目录
3. 扫描根目录下的每个插件子目录
4. 读取并解析每个插件目录中的 `plugin.json`
5. 校验入口程序集路径是否存在
6. 校验插件是否出现在 `Platform:DisabledPlugins` 禁用列表中
7. 校验插件声明的宿主版本兼容范围
8. 如果清单声明了 `entryAssemblySha256`，则校验入口程序集 SHA-256
9. 对通过校验的插件执行 `Assembly.LoadFrom`
10. 将装载得到的程序集交给现有 `AssemblyModuleLoader` 发现 `IModule`
11. 由 `ModuleRegistry` 完成模块、权限、菜单、配置和迁移描述符收集

## 当前失败策略

当前策略为“宿主继续启动，问题插件跳过并记录日志”。

失败场景包括：

- 插件目录不存在
- 清单不存在
- 清单 JSON 非法
- 入口程序集不存在
- 插件被显式禁用
- 宿主版本不兼容
- 入口程序集 SHA-256 不匹配
- 入口程序集加载失败

对于上述失败：

- 宿主不会因为单个插件失败而整体崩溃
- 失败插件不会加入运行时程序集集合
- 宿主日志会输出插件目录、失败类型和错误消息

## 当前开发期过渡机制

当前内建模块仍保留 `HostApi.csproj` 中的 `ProjectReference`，但其职责已经降级为：

- 在开发期构建宿主时，确保模块 DLL 被产出到宿主输出目录
- 配合复制到输出目录的 `modules/*/plugin.json` 形成最小插件目录

它们不再决定正式运行时装配协议。正式协议已经切为：

- 插件目录
- 插件清单
- 兼容检查
- 程序集运行时加载

## 当前实现位置

- 宿主入口：`apps/host-api/Program.cs`
- 插件发现：`server/kernel/PluginDiscovery/PluginDiscoveryService.cs`
- 插件扫描：`server/kernel/PluginDiscovery/PluginScanner.cs`
- 兼容检查：`server/kernel/PluginDiscovery/PluginCompatibilityValidator.cs`
- 运行时程序集装载：`server/kernel/PluginDiscovery/PluginRuntimeAssemblyLoader.cs`
- 模块发现与依赖排序：`server/kernel/ModuleLoader/AssemblyModuleLoader.cs`

## 已完成的最小闭环

当前仓库已经完成：

- 清单驱动的插件目录扫描
- 宿主禁用插件列表接入
- 宿主版本兼容校验
- 可选的入口程序集 SHA-256 校验
- 宿主基于插件目录装载 6 个内建后端模块
- 不再依赖 `ConfigCenter.dll` 这类特判入口
- 不再依赖 `*Module.dll` 文件名扫描作为宿主正式装配入口

## 未完成项

- 插件依赖冲突治理
- 多版本插件并存策略
- 插件签名体系
- 外部插件安装与升级链路
