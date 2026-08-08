# ICP.Platform.Plugin.Contracts

`standard-data.v1` 的稳定公共契约包，只依赖 BCL。它提供 Blueprint、Platform API release/attestation、package 与稳定 diagnostic 契约，不公开 Host、Kernel、EF、ASP.NET 或内建模块实现。

此包与 `ICP.Platform.Plugin.Sdk`、`@icp-platform/plugin-sdk`、`ICP.Platform.Plugin.Tool` 使用相同精确 SemVer 和 release digest；外部项目必须以 `[1.0.0]` 精确引用并提交锁文件。
