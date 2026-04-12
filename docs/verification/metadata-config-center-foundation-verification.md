# Metadata Config Center Foundation 验证记录

## 1. 验证目标

验证本轮 foundation 最小闭环已经形成：

- 元数据配置中心作为独立模块存在
- `ConfigCenter` 模块的 `SettingDefinition` 可被读取
- 保存后会按 source/runtime 实际差异返回状态
- `apply` 会把当前 source metadata 显式投影到 runtime
- `ConfigCenter` 仍只承担 `ConfigItem` 值层职责
- 当运行时 frontend manifest 存在但未安装对应插件包时，Shell 仍会注册 `metadata-config-center` 入口

## 2. 执行命令

后端测试：

- `dotnet test .\tests\MetadataConfigCenter.Tests\MetadataConfigCenter.Tests.csproj -nr:false -v minimal`
- `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "ConfigServiceTests|LegacySettingImportServiceTests|ConfigCenterModuleTests" -nr:false -v minimal`

前端验证：

- `pnpm --filter @icp-platform/module-metadata-config-center build`
- `pnpm --filter @icp-platform/module-config-center build`
- `pnpm --filter @icp-platform/shell build`
- `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/registerModules-plugin-ops.test.ts`
- `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/metadata-config-service.test.ts`

最小 API smoke：

1. 以 `Persistence__AutoMigrate=false`、`Platform__SkipLocalAuthSeeding=true` 启动 `HostApi`
2. 调用 `GET /metadata-config/modules`
3. 调用 `GET /metadata-config/modules/ConfigCenter/settings`
4. 修改首条设置定义的 `DisplayName`
5. 调用 `PUT /metadata-config/modules/ConfigCenter/settings`
6. 调用 `POST /metadata-config/modules/ConfigCenter/settings/apply`
7. 恢复样本文件内容，确保工作树不被 smoke 污染

## 3. 结果

测试与构建结果：

- `MetadataConfigCenter.Tests`：12/12 通过
- `ConfigCenter` 定向边界测试：13/13 通过
- `@icp-platform/module-metadata-config-center`：构建通过
- `@icp-platform/module-config-center`：构建通过
- `@icp-platform/shell`：构建通过
- `registerModules-plugin-ops.test.ts`：4/4 通过
- `metadata-config-service.test.ts`：1/1 通过

API smoke 结果：

- `GET /metadata-config/modules` 返回 1 个模块，包含 `ConfigCenter`
- `GET /metadata-config/modules/ConfigCenter/settings` 返回 1 条设置定义
- `PUT /metadata-config/modules/ConfigCenter/settings` 成功
- 保存返回会按 source/runtime 差异计算状态；当 source 已改而 runtime 落后时，返回 `HasUnappliedChanges = true`
- `POST /metadata-config/modules/ConfigCenter/settings/apply` 成功后，返回 `Status = 已应用`，且 `HasUnappliedChanges = false`

## 4. 结论

- 独立元数据配置中心模块已经形成最小 foundation 闭环
- `SettingDefinition` 的定义层已收口为“runtime 读取、source 保存、apply 投影 runtime”的显式链路
- `ConfigCenter` 的值层边界仍保持稳定，没有回退成定义层编辑中心
- Shell 侧已补齐 host-managed 注册，运行时 manifest 模式下也不会丢失 `metadata-config-center` 入口

## 5. 当前边界

- 本轮只验证 `SettingDefinition`，未扩展到 `Dictionary / Enumeration` 的复杂 authoring
- 手工 smoke 通过 `Persistence__AutoMigrate=false` 绕开了启动期数据库迁移，验证的是定义层 authoring 闭环，不替代值层数据库 smoke
- `Git 同步` 当前仍保持独立入口且未在本轮实现；`apply` 已实现，但目前仍是模块级动作，尚未上推为统一插件内核动作
