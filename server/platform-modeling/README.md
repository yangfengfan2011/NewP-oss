# Platform Modeling

Platform Modeling 把模块模型 metadata 校验并投影为统一 `ModelDefinition`、运行时模型、DTO 描述和持久化输入。它不拥有模块业务模型，也不把 UI authoring 字段混入数据库语义。

## Authoring 边界

- 模型真源位于 `modules/<ModuleId>/metadata/models/*.model.json`。
- 所有修改进入版本库并通过 metadata validator、DTO 同步和相关模块测试。
- 人工或自动 authoring 工具都必须写入同一契约，不能建立第二套模型真源。
- 业务前端不直接解析模型文件，通过后端 schema/descriptor 契约消费投影结果。

## 投影规则

- `storage` 是进入 `ModelDefinition` 与 migration 的字段分区。
- `layout`、`form`、`view`、`uiType` 和 `dataSource` 只服务 authoring，不参与数据库投影。
- Entity/Link 固定字段与内建索引由内核补入；模型文件不重复声明。
- `builtinFields` 只能启用当前 kind 允许的可选固定字段。
- `linkedTypes` 只表达 Link 允许的父子类型组合，不表达删除策略或运行时流程。
- `storage.type` 必须来自 `server/platform-modeling/metadata/storage-types.json`。

生成 DTO 使用 `server/tools/model-dto-sync`；持久化执行边界见 `docs/contexts/platform-persistence/CONTEXT.md`。
