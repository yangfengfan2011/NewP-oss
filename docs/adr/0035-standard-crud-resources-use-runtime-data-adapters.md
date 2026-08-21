---
status: accepted
---

# Standard CRUD 资源通过运行时数据 Adapter 接入共享宿主

Standard CRUD 共享宿主统一消费一个小的 `StandardCrudDataAdapter` Interface，而不再分别解释资源的 HTTP 路径、列表映射、行标识、详情装载和普通写入 fallback。平台提供标准 REST Adapter；拥有不同传输和写入语义的模块提供自己的 Adapter。两类 Adapter 都输出规范列表、详情和普通操作结果，因此 Shell 只拥有页面生命周期，不了解模块 DTO 或 endpoint 差异。

Model、List View 与 Detail View 继续分别拥有字段、查询、表单和详情展示结构；Data Adapter 不解释 metadata，也不产生 Candidate、artifact 或新的运行时事实源。模块专用命令、renderer、lookup 和敏感结果仍由模块 binding 拥有，不进入标准 REST Adapter。

这一决定替代 `StandardCrudResourceRegistration` 中由 Host 直接消费 `apiRoute`、`mapListItem`、`getRowId`、`loadDetail` 和 `operationHandlers` 的浅 Interface，并删除已由 metadata runtime 取代的 `createEmptyFilters` 与 `buildListQuery`。切换同时覆盖内建资源和现有 `standard-data.v1` runtime projector，不保留旧字段 fallback。

未采用全局 Resource compiler 或 Accepted artifact：当前变化发生在进程内运行时 seam，两个真实业务 Adapter 与一个现有 projector 已足以证明差异，增加编译、staging 或持久化产物不会提高调用方获得的能力，反而会扩大公共 Interface 和失败面。
