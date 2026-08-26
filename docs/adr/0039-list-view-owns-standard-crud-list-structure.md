---
status: accepted
---

# List View 是 Standard CRUD 列表结构的唯一事实源

Standard CRUD 列表曾允许资源注册通过 `renderFilters`、`renderHeaderActions` 和 `renderRowActions` 向共享宿主注入任意 React 内容。这些回调与 List View 已拥有的查询、页级操作和行级操作形成平行入口，并通过 `StandardCrudListExtensionContext` 把筛选状态、刷新、加载结果和行数据暴露给模块 UI。当前 Organization、Identity、Example 与 standard-data runtime projector 均没有生产采用这些回调；其中 `renderFilters` 已没有运行时消费者。平台决定删除这些浅 Interface，由 List View 唯一拥有标准列表结构。

## 决策

List View 拥有标准列表的静态展示结构，包括 metadata 列、查询字段、高级筛选分区、page action、row action、分页、排序和默认值。Shell 解析并校验完整 List View，形成列表运行时投影后统一呈现；资源模块不得通过注册回调重新定义筛选区、页头操作区或行操作区。

ADR-0038 定义的 runtime column 是唯一列结构委托。runtime 模式下，List View 只选择列来源，模块 Runtime Column Provider 返回完整、可序列化的列集；Provider 不返回 React 节点、renderer 或其他列表 UI。metadata 列与 runtime 列最终都由 Shell 消费同一种解析后列模型。

资源注册继续绑定运行时行为：Standard CRUD 数据 Adapter、Runtime Column Provider、lookup provider、action handler、action state、action input renderer、字段或摘要 renderer，以及操作结果 presenter。模块拥有数据转换、业务动作和专用原子内容，但不拥有标准列表区域的结构或宿主刷新策略。需要完全自定义筛选布局、页头或行内容的页面使用自定义页面 capability，不扩展 Standard CRUD 列表 Interface。

SDK 一次性删除 `StandardCrudListExtensionContext`、`renderFilters`、`renderHeaderActions` 和 `renderRowActions` 及其根出口，不保留 deprecated 字段、Host fallback、兼容 wrapper 或静默忽略路径。外部调用方如使用这些入口，升级前必须迁移为 List View metadata 与稳定 behavior binding，或改用自定义页面 capability。

操作列只由解析后的 row action 决定。`StandardCrudDataAdapter.getRowId` 只提供稳定行身份，不表示页面存在行操作；没有 row action 时不得生成 `actions` 列、空表头、空单元格或对应列偏好项。

## 取舍

未采用通用 slot、任意 renderer registry 或新的 React 回调。它们会把布局所有权重新交给模块，使标准列表需要同时理解 metadata 与模块 UI，并把筛选状态和刷新生命周期重新暴露到公共 Interface。

未保留弃用桥。仓库内没有生产采用者，兼容桥只会延长两套事实源并允许新模块继续依赖旁路。该切换作为显式公共契约收缩交付，相关 SDK、Host、测试与文档必须保持同一版本边界。

## 非目标

- 不改变 List View、Model、Detail View 或 runtime column metadata 协议。
- 不改变 Frontend Query Runtime、数据 Adapter、Runtime Column Provider、Standard CRUD Action Execution 或 Mutation Execution Interface。
- 不增加批量选择、批量操作、保存视图、跨页选择或新的导出能力。
- 不重构完整详情、Drawer、Lookup 或 Standard CRUD Host 的其他职责。

## 验收与失败信号

SDK type-test 必须证明被删除的字段和上下文不再属于公共 Interface。Shell 公开行为测试必须证明：必选 `getRowId` 存在但 List View 没有 row action 时不产生操作列；metadata page action 与 row action 仍通过既有 handler 和执行生命周期工作；Example runtime-columns 的 list-only 页面没有空操作列；Person 与 Identity 的标准筛选和操作行为不回归。

出现以下任一情况即表示决策失败：删除回调后又向 metadata 写入 React 节点或任意 renderer；Host 增加模块或资源特判；列表结构需要合并两个事实源；为旧字段增加运行时探测或兼容 fallback；操作列仍由行身份或数据 Adapter 能力隐式决定；或者必须改变 Query Runtime、数据 Adapter、runtime columns 或动作执行的公共 seam。

## 回滚

回滚必须把 SDK Interface 与出口、Shell 消费路径、Host 行为测试、Standard CRUD Context 和本 ADR 作为一个版本控制边界整体恢复，不能只恢复类型或单个 fallback 而留下半切换状态。本决策不改变后端、数据库、metadata authoring 或业务数据，无数据回滚。实施与验证由 GitHub Issue #116 跟踪。
