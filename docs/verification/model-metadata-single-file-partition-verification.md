# 单文件分区模型元数据验证记录

## 验证范围

- 平台级 `storage-types.json` 类型映射文件加载
- 模型元数据文档的最小加载与校验
- `storage` 到 `ModelDefinition` 的最小投影
- `Entity / Link` 固定字段与最小内建索引补入
- `ConfigCenter` 通过真实 `ConfigItem.model.json` 进入 provider 加载链路
- 后端最小 schema 输出契约

## 已执行命令

```powershell
dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj -nr:false -v minimal
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "ConfigCenterModelDefinitionTests|ConfigCenterModelMetadataProviderTests" -nr:false -v minimal
```

## 当前结果

- `Modeling.Tests` 当前共 11 个测试，通过
- `ConfigCenter` 相关模型元数据试点测试当前共 3 个测试，通过
- `ConfigItem` 已可通过真实元数据文件完成加载、校验和最小投影

## 当前边界

- 当前 projector 仍是最小实现，只覆盖首轮测试涉及的 `storage.type`
- `layout / form / view / dataSource` 当前已进入文档模型与最小 schema 输出，但尚未形成完整 schema API 契约
- `ValueListItem` 仍保留原有手写定义，尚未进入元数据试点链路
- `ConfigItem` 当前试点文件仍是最小模型，不代表最终完整业务字段集合
