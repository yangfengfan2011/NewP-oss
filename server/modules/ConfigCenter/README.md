# ConfigCenter 模块

ConfigCenter 拥有运行时配置值与 Value List 能力，注册配置服务、EF 仓储、旧 Setting 导入适配，以及对应菜单和权限。

定义层 authoring 编辑归 `MetadataConfigCenter`；ConfigCenter 不扩张为元数据设计器。当前入口和依赖以 `ConfigCenterModule.cs`、Controllers、Services、Persistence 与 `tests/ConfigCenter.Tests` 为准。

本模块仍通过兼容 `ModuleBase` 接入。新增模块使用 Module Capsule 与 behavior binding，不复制这里的身份、菜单或权限声明。
