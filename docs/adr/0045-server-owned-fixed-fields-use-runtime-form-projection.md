---
status: accepted
---

# 固定字段目录与所有权由运行时 Model 投影

Model authoring 声明业务字段、选择的 builtin 和显式固定字段覆盖。后端 modeling 以同一套固定字段定义补全 runtime Model 的 `model.fields`，使其成为 List、Detail 和 Form 共用的解析后字段目录。显式字段保持 authoring 顺序和已有属性；显式固定字段只补缺失的 contract、storage 和 `uiType`，已有 dataSource、contract、storage 和 `uiType` 优先，补充 `uiType` 时遵循已有 dataSource 与 contract 语义。缺少的固定字段按后端规范顺序追加，并带有字段契约、存储类型、可空性和 `uiType`。Entity 目录覆盖 `EID`、四个审计字段以及选择的 `ENAME`、`ECODE`；Link 目录覆盖 `LID`、`PEID`、`CEID`、四个审计字段以及选择的 `LSequence`。

追加的固定字段不进入 authoring 的 form sections，且 create/edit 均为 `hidden`。authoring 显式声明的固定字段继续沿用现有所有权判定：现有策略为 `Server` 的字段在未声明表单策略时隐藏，显式 `readonly` 时允许显示但不得提交，显式 `editable` 在 Prepare 阶段的 metadata validation、任何 migration 之前稳定拒绝并定位冲突字段。显式 `EID`、`LID` 仍依现有判定归 `Caller`；`ENAME`、`ECODE`、`PEID`、`CEID`、`LSequence` 和业务字段的 `ServerOnCreate` 不因此改变所有权。

普通 authoring Model 的 Prepare 与 Stage 必须对同一份完整 runtime Model 集合执行相同投影并保持原子发布；`persistence.models` 只是持久化计划的子集，不能代替 runtime Model 集合。standard-data Model 在 Stage 内存生成后调用同一 runtime projector，再写入 staging；其业务 sections 和 columns 保持原样，追加的固定字段只通过 `model.fields` 提供给运行时消费者。

投影只改变 runtime JSON，不反写 authoring，不改变持久化 model digest、数据库 schema 或数据。Shell 直接消费 `model.fields`，不合并自己的固定字段名单；表单继续消费字段的 form 结构，并在最终提交边界排除 `readOnly` 字段，即使资源的值构造器重新带回该字段。值构造器附加的合法非表单业务值仍须保留。

## 非目标

本决策不新增并行字段目录或固定字段分类生成物，不建立前端字段名单或生成同步系统，不引入 `Revision`，不重构 Standard CRUD Mutation Execution 生命周期，也不扩大既有字段所有权、持久化或数据库迁移语义。

## 回滚

回滚必须共同恢复后端 runtime Model 投影与 Shell 的目录、表单及提交消费，并恢复 Company、Department 中被删除的 `UpdatedAt` 字段声明及其 `hidden` form 策略，避免只恢复一侧。runtime 投影不修改数据库，因此回滚不执行数据库或数据恢复。
