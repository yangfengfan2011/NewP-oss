---
status: accepted
---

# 详情结构与响应数据独立，数据不可用时局部降级

Detail View 继续唯一拥有标准 CRUD 详情的字段选择、顺序、分区和 renderer；详情响应独立提供本次请求的数据，不再作为候选注册阶段的字段白名单。普通响应字段缺失不能拒绝 metadata、页面或整个模块候选，而应由 Drawer 与完整详情页共享的详情呈现 module 在响应到达后统一判定可用性并产生稳定诊断。

## 决策

- 使用 own-property 语义区分字段存在与字段缺失。存在且值为 `null` 的字段显示 `—`，不记作契约缺失；字段不存在或为非 JSON 的 `undefined` 时，仅该字段显示“数据不可用”并记录 `STANDARD_CRUD_DETAIL_FIELD_UNAVAILABLE`。
- 只有一个 section 的全部声明字段都不可用时，才以整段“数据不可用”替代字段列表并记录 `STANDARD_CRUD_DETAIL_SECTION_UNAVAILABLE`；部分字段可用时必须继续显示这些字段。
- 标准 CRUD 实体的 `EID` 是关键标识。它缺失、null、空或与当前路由 id 不一致时，整个详情以稳定错误失败，不呈现摘要或动作；不一致可能把另一实体的数据与当前路由动作组合，属于数据完整性与安全风险。
- page action 可以在 Detail View 内声明封闭的 `dataDependency`，只包含该动作需要的字段和不可用原因字典 key。依赖字段缺失或 null 时，动作保持可见但禁用并显示原因，同时记录 `STANDARD_CRUD_DETAIL_ACTION_DEPENDENCY_UNAVAILABLE`。该声明不是详情响应字段全集，不改变后端查询投影，也不得复制到 SDK behavior binding。
- metadata 解析只校验 Detail View 的结构、字段命名空间、renderer 和 action dependency 引用；真实数据可用性只按本次响应判断，不从 Model 推断。

## Consequences

`Resolved Detail Schema` 与详情数据可以独立加载；候选注册不再接收 `detailResponseFields`。共享详情呈现 module 是 Drawer、完整页、普通字段、section、关键标识、动作可用性和诊断的唯一运行时解释入口，不允许人员或其他资源特判。浏览器验收可以通过明确标记的响应拦截证明前端降级，但该证据不代表正式后端 API 产生了相同异常。

本决定只替代 ADR-0031 中“详情响应未提供的普通字段必须在候选编译或前端原子注册阶段拒绝整份候选”的条款；Detail View 结构真源、无 Model/form fallback、显式 View、renderer 校验以及 create/edit 不回归仍保持有效。回滚必须同时恢复候选响应字段校验协议、两个详情 surface、动作依赖语义、诊断和验收，不能只让一个页面重新抛错。
