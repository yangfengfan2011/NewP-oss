# MetadataConfigCenter

`MetadataConfigCenter` 是独立的元数据配置中心后端模块。

## 当前阶段范围

- 提供独立模块入口
- 暴露菜单与权限契约
- 提供基于 manifest 的模块发现
- 提供 `SettingDefinition` 的读取、保存与显式 `apply` 闭环

## 设计边界

- 只负责元数据定义层治理入口
- 不承接 `ConfigCenter` 的 `ConfigItem` 值层职责
- 保存、应用、Git 同步保持分离
- 保存只回写 source metadata，`apply` 负责把 source 投影到 runtime

## 本阶段不包含

- `Dictionary / Enumeration` 编辑器
- 通用化的插件内核 apply / rollback 编排
- 真实 Git 同步执行链路
