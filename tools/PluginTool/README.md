# ICP.Platform.Plugin.Tool

`newp` 本地工具提供 `plugin validate`、`plugin explain`、`plugin pack`、`plugin verify`，并以 `platform-api release` 从四个真实发布包生成锁步 release manifest。

工具只消费同版公开 Contracts/SDK；它不发布远端包，也不把 NewP 源码路径写入插件或证据。
