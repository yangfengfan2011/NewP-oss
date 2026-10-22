# MetadataConfigCenter 模块

MetadataConfigCenter 提供元数据定义层的治理入口。它通过模块 metadata manifest 发现可管理定义，并保持 source 保存与 runtime apply 两个动作分离。

当前边界：

- 读取和保存受支持的 source metadata；
- 显式 apply，把已保存 source 投影到 runtime；
- 暴露模块、菜单和权限入口；
- 不拥有 ConfigCenter 的 `ConfigItem` 运行时值；
- 不把 Git 同步、插件激活或通用回滚编排吸收到模块内部。

支持的 metadata 类型和 authoring 能力以当前 manifest、Controllers、Services 和测试为事实源，不在 README 维护易过期清单。
