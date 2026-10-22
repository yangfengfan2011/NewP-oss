# @icp-platform/plugin-sdk

`standard-data.v1` 的 build-only TypeScript SDK，包含 canonical schema、类型、纯校验器和标准前端投影函数。它没有 React、Shell、Registry、Core、UI、全局状态或 runtime 依赖。

外部仓库必须精确锁定 `1.0.0` 并提交 `pnpm-lock.yaml`；插件包不携带本包或任何外部可执行前端 JavaScript。
