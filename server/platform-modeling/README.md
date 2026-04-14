# Platform Modeling

## Metadata Authoring Workflow

- 当前阶段不提供人工设计器。
- 模型语义定义默认由 AI 生成或修改。
- 所有生成结果必须进入版本库。
- 所有生成结果必须通过 `ModelDefinitionValidator` 和相关模块测试。
- 后续若建设设计器，设计器输出仍必须对齐当前模型语义契约。

## Model Metadata Rules

- `storage` 是唯一允许进入 `ModelDefinition` 与 migration 的字段分区。
- `layout / form / view / uiType / dataSource` 只服务于 schema authoring，不参与数据库投影。
- `Entity / Link` 的固定字段与内建索引由内核自动补入，不要求每个模型文件重复声明。
- `builtinFields` 只能启用当前 `kind` 允许的可选固定字段。
- `Link` 的 `linkedTypes` 只表达允许的父子类型组合，不表达删除策略或运行时流程。
- `storage.type` 必须来自 `metadata/storage-types.json` 中定义的平台级类型映射。
- 业务前端不直接读取 `*.model.json`，统一通过后端 schema 输出契约消费解析结果。
