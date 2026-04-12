# MetadataConfigCenter

`MetadataConfigCenter` 是独立的元数据配置中心后端模块。

## 当前阶段范围

- 提供独立模块入口
- 暴露菜单与权限契约
- 为后续 manifest 发现和 `SettingDefinition` 编辑闭环预留模块位置

## 设计边界

- 只负责元数据定义层治理入口
- 不承接 `ConfigCenter` 的 `ConfigItem` 值层职责
- 保存、应用、Git 同步保持分离

## 本阶段不包含

- manifest 扫描实现
- `SettingDefinition` 读写 API
- `Dictionary / Enumeration` 编辑器
