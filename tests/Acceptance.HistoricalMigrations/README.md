# Example 历史验收资产

三个隔离程序集通过 Compile Include 复用正式、不可改写的 migration 源码，仅编译指定前缀；正式 Host 不引用这些测试项目。Assembly checksum 属于本夹具，原迁移 step 事实保持不变，执行仍使用默认精确类型校验。

冻结 authoring 来源：Organization foundation 四模型取自 `7ef442cf`；Example stage 2 取自 `e4929b0a`。Example stage 3 取自 `413e692a` 的完整 authoring，并按该提交中的 `20260828082341_Example_000003_RecordSourceNullable` 固定 RecordSource storage nullable=true，代表同一交付中必填收紧前的真实阶段。所有输入通过正式 loader/storage map/projector 投影并与既有迁移 digest 比较，不从当前模型增删字段构造历史。
