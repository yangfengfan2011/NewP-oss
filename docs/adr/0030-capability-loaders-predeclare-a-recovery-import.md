---
status: accepted
---

# Capability loader 预声明独立 recovery import

浏览器会在 module map 中缓存失败的 ESM URL；只清理 Shell 的 single-flight Promise，无法让局部 Retry 再次请求同一个 URL。前端 SDK 因此提供通用 `createRecoverableCapabilityLoader(primary, recovery)` Interface：它仍返回既有的零参数 `load()`，正常调用只执行 primary；primary 拒绝后，下一次调用切换到静态可分析且物理 URL 不同的 recovery import。`StandardCrudCapabilityBinding` 与 `RouteCapabilityRuntime.load(ref)` 的 Interface 保持不变，模块身份、resource key 和路由不参与选择。

Module Capsule 和 frontend module template 必须同时声明 primary 与 recovery 两个构建入口；注册和活动候选校验不得执行任一 loader，正常路径不得下载 recovery asset。生产验收必须精确让 primary asset 失败一次，观察局部 `ROUTE_CAPABILITY_LOAD_FAILED`，再证明 Retry 请求 recovery asset、恢复目标 capability 且活动 registry 未改变。禁止用整页刷新、运行时拼接不受构建器分析的 URL、模块特判或吞掉失败代替该恢复路径。
