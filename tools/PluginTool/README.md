# ICP.Platform.Plugin.Tool

`newp` 本地工具提供 `plugin validate`、`plugin explain`、`plugin pack`、`plugin verify`，并以 `platform-api release` 从四个真实发布包生成锁步 release manifest。

`plugin finalize-migration --root <外仓> --release-manifest <同版manifest> --migration <尾节点ID> --source <仓内canonical.cs路径> --compatibility <分类>` 静态派生受支持 profile 的迁移清单。首次先提交空 manifest，后续以外仓 HEAD 核对不可变前缀；实际 DLL migration 类型集合必须精确匹配，重复同一尾节点保持相同字节。工具不连接数据库或执行 Up。

公开 Authoring SDK 继续只依赖同版 Contracts。Tool 随自身发布携带 finalize 所需的共享静态建模/manifest库和 storage type map；外部项目不引用这些内部库。工具不发布远端包，也不把 NewP 源码路径写入插件或证据。完整可复制基线与部署步骤见 `docs/developer/external-plugin-development.md`。
