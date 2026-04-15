# 元数据 Authoring 布局统一验证记录

## 1. 验证范围

- 取消模块清单
- 统一扫描 `modules/*/metadata/**`
- `ConfigCenter` 模型元数据迁移到 `modules/ConfigCenter/metadata/**`
- `Platform` 枚举与本地化元数据迁移到 `modules/Platform/metadata/**`
- `resources/**` 删除

## 2. 实际结果

- 平台不再依赖模块 manifest
- 平台改为扫描 `modules/*/metadata/**`
- `ConfigCenter` 模型元数据已迁入 `modules/ConfigCenter/metadata/models/ConfigItem.model.json`
- `Platform` 枚举元数据已迁入 `modules/Platform/metadata/enumerations/**`
- `Platform` 字典与枚举本地化已统一重组为 `modules/Platform/metadata/dictionaries/*.dic.json`
- `modules/Platform/metadata/languages.json` 成为全局唯一语言配置入口
- `resources/**` 已删除

## 3. 关键约束复核

- 平台内核执行级元数据目录收口到 `server/platform/metadata/**`
- 文件类型映射入口为 `server/platform/metadata/file-type-mappings.json`
- `mergeMode` 当前仅允许 `replace` 与 `dic-entry`
- `settings` 使用对象映射结构
- `enum` / `model` 使用整对象覆盖
- `dic` 使用词条级合并，同词条冲突时整组多语言覆盖
- 模型多语言不内嵌在 `model` 文件中，`ConfigItem` 的相关多语言词条已同步补到 `modules/Platform/metadata/dictionaries/ConfigCenter.dic.json`

## 4. 验证命令

- `dotnet build server/modules/MetadataConfigCenter/MetadataConfigCenter.csproj`
  - 结果：通过
- `dotnet build server/modules/ConfigCenter/ConfigCenter.csproj`
  - 结果：通过
- `dotnet test tests/MetadataConfigCenter.Tests/MetadataConfigCenter.Tests.csproj`
  - 结果：通过，`17/17` 用例通过
- `dotnet test tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj --filter ConfigCenterModelMetadataProviderTests`
  - 结果：通过，`2/2` 用例通过
- `pnpm --filter @icp-platform/shell test`
  - 结果：通过，`14/14` 测试文件、`41/41` 用例通过
- `pnpm --filter @icp-platform/shell build`
  - 结果：通过

补充说明：

- `dotnet test tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj` 全量执行时，存在既有失败 `EfConfigRepositoryTests.GetByKeyAsync_ReturnsTenantAndEnvironmentMatchFirst`
  - 失败信号：`EfConfigRepository.CreateAsync` 写入动态实体时主键 `EID` 为空，触发 EF 跟踪异常
  - 结论：该失败点位于 `server/modules/ConfigCenter/Persistence/Ef/EfConfigRepository.cs`，与本次 metadata authoring 布局统一改动不在同一代码路径，先作为仓库现有独立问题记录，不阻断本次目录统一验证结论
- `dotnet build ICP-Platform.sln` 当前无法作为本次验证依据
  - 失败信号：解决方案文件报错“有两个名为 `sdk` 的项目”
  - 结论：该问题属于解决方案装配层现状，未在本次任务内调整

## 5. 风险与回滚

- 已知风险
  - 仓库当前仍存在 `ConfigCenter` 的独立 EF 测试失败，后续若要把“全量后端测试通过”作为门禁，需要单独修复该持久化路径
  - `ICP-Platform.sln` 存在重名项目导致的解决方案级构建失败，当前只能以受影响项目级构建和测试作为验证依据
  - 前端测试存在 React Router future flag 与 `act(...)` 的警告，当前未阻断通过，但后续升级依赖时应避免其演化为真实失败
- 回滚思路
  - 恢复 `MetadataManifestService`、`SettingDefinitionApplyService`、`SettingDefinitionAuthoringService` 等扫描与装配入口到迁移前版本
  - 将 `modules/ConfigCenter/metadata/models/ConfigItem.model.json` 恢复回旧路径，并恢复 `resources/**` 下的平台枚举、本地化与语言配置
  - 恢复模块 manifest 与旧扫描入口后，重新执行本文件中的项目级构建、测试与前端验证，确认回到迁移前可用状态
