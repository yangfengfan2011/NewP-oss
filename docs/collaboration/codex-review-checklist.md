# 变更审查清单

## 事实与范围

- 目标是否来自当前 GitHub issue/spec、ADR 或用户确认？
- 结论是否由当前源码、配置和测试支撑，而不是退役阶段材料？
- diff 是否只包含本任务拥有的文件或 hunk，并保留已有工作区内容？

## 架构与契约

- Host、Shell、Kernel、Contracts、SDK 和模块所有权是否符合[组件化护栏](../architecture/componentization-guardrails.md)？
- 模块身份、metadata 与 behavior binding 是否只有一个 owner？
- 跨模块调用是否经过稳定 Contracts；持久化、安全或公共 API 变化是否有已确认设计？
- 兼容路径是否明确标记、隔离并有退出条件？

## 实现与失败语义

- Controller 是否轻薄，业务规则是否位于应用或领域服务？
- 冲突、部分失败、重试、回滚和 restart 后状态是否有明确语义？
- 日志和错误是否提供稳定诊断信息，同时避免连接串、凭据和 token 泄漏？

## 验证与交付

- 是否先运行定向测试，再按影响范围运行 `scripts/test.ps1`、`scripts/verify.ps1` 或 Acceptance Gate？
- 文档、链接、命令和本地路径是否通过当前文档检查？
- 修改文件是否无混合行尾，`git diff --check` 是否通过？
- 最终报告是否区分实际验证、未执行项、风险和回滚方式？
