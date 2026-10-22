# ICP.Platform.Plugin.Sdk

`standard-data.v1` 的 build-only Authoring SDK。它精确依赖同版 Contracts，提供 Blueprint 编译、投影、包构建/验证、Platform API release 校验、MSBuild 锁步门禁与禁止 backend runtime 的 analyzer。

SDK 不进入插件 runtime。外部项目必须使用精确版本 `[1.0.0]`，不得使用 ProjectReference、平台实现依赖或源码路径；完整隔离消费示例见 `tests/integration/plugin-api/baseline/1.0.0`。
