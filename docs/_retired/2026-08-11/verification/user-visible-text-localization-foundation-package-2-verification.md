# 用户可见文本多语言基础第二包验证

## 范围

- 共享 `messageKey + messageArgs + level` 用户消息契约基座
- `ConfigCenter` 值列表创建、编辑、删除失败链路的后端消息契约试点
- 值列表页对后端 `UserMessageDto` 的翻译消费闭环
- `ConfigCenter` 字典资源进入统一插件包并投影到 runtime `localization-manifest`

## 执行命令

1. `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter FullyQualifiedName~ValueListServiceTests -nr:false -v minimal`
2. `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~UserMessageContractTests -nr:false -v minimal`
3. `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter "FullyQualifiedName~ValueListControllerMessageContractTests|FullyQualifiedName~RuntimeViewMaterializerTests|FullyQualifiedName~UnifiedPluginPackagingScriptsTests" -nr:false -v minimal`
4. `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/user-message.test.ts src/__tests__/value-list-management-message-contract.test.tsx`
5. `dotnet build .\ICP-Platform.sln -nr:false -v minimal`
6. `pnpm --dir .\frontend build`

## 结果

- 第 1 条命令通过：`ConfigCenter.Tests` 中 `ValueListServiceTests` 共 `8` 个测试通过。
- 第 2 条命令通过：`Kernel.Tests` 中 `UserMessageContractTests` 共 `6` 个测试通过。
- 第 3 条命令通过：`Kernel.Tests` 中 `ValueListControllerMessageContractTests`、`RuntimeViewMaterializerTests`、`UnifiedPluginPackagingScriptsTests` 共 `21` 个测试通过。
- 第 4 条命令通过：前端定向 Vitest 共 `10` 个测试通过。
- 第 5 条命令通过：`.NET` 解决方案构建成功，`0` warning、`0` error。
- 第 6 条命令通过：前端 workspace 构建成功。

## 特殊说明

- 按计划原文把 `UserMessageContractTests` 与另外三组 `Kernel.Tests` 过滤条件合并为一条命令时，本地两次运行均超时，没有得到可信结果。
- 为避免把超时误写成通过，本次验证改为拆成第 2 条与第 3 条两条命令执行，覆盖范围保持一致，并以拆分后的 fresh 结果作为正式验证证据。
- 在最终前端全量构建阶段，额外暴露出 `frontend/packages/core` 缺少对 `@icp-platform/sdk` 的 workspace 依赖声明；本轮已补齐 `frontend/packages/core/package.json`，之后 `pnpm --dir .\frontend build` 恢复通过。

## 已验证结论

- 后端共享用户消息契约已可稳定序列化并供前后端共同消费。
- `ConfigCenter` 值列表失败链路已切到 `UserMessageException -> UserMessageDto`。
- `modules/ConfigCenter/metadata/dictionaries/ValueLists.dic.json` 已进入统一插件包，并被 runtime `localization-manifest` 真正投影。
- 值列表页已经优先消费后端 `UserMessageDto` 并按当前语言翻译展示，旧 `{ message }` 仅保留兼容回退。

## 已知限制

- `ValueListController` 中的 `UserMessageException -> DTO` 桥接目前仍写在模块控制器内，这是本包计划明确允许的临时收口方式，不在本轮继续上提到宿主级统一异常映射。
- 真实数据库唯一约束/并发冲突到 `UserMessageDto` 的统一映射尚未纳入本包范围；当前验证覆盖的是值列表试点链路与受控测试仓储路径。
- 值列表页内的 `tryReadMessage` 兼容逻辑当前仍保留在页面文件内，后续若其他页面继续接入同类契约，建议单独拆包收口成共享前端适配入口。
