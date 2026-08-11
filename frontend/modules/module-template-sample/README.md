# module-template-sample 兼容样本

该模块保留早期 `FrontendModule` 声明与示例 API 页面，只用于回归 legacy adapter，不是新增前端模块的复制来源。

新增模块使用 `scripts/new-module.ps1` 生成根 Capsule、metadata 和两侧 behavior binding；前端入口应导出 `FrontendBehaviorBindings`。当前规范见 `frontend/docs/module-integration-guide.md` 和 `docs/developer/componentized-development.md`。
