---
status: accepted
---

# Standard CRUD 运行时列由模块 Provider 解析并先于行数据加载

Standard CRUD List View 只通过 `columnSource` 选择 `metadata` 或 `runtime` 列来源；缺省的 metadata 来源继续由 List View 拥有具体静态列，runtime 来源不声明具体列，而由资源模块注册的 Runtime Column Provider 异步返回完整、可序列化的运行时列集。Provider 调用模块自己的 API 并转换模块 DTO；metadata 不保存 endpoint，Host、Shell 和标准 REST Adapter 不包含日期计算、模块字段或资源特判。这个决定扩展 ADR-0035 对 List View 列结构所有权的结论。

Shell 统一拥有列优先加载代际：先解析并校验列，再校验排序、恢复列偏好、构造字段投影，最后通过既有 `StandardCrudDataAdapter.list` 读取普通或计算型行数据。列加载或契约校验失败时不得读取或展示未知结构的行；筛选提交、刷新和相关上下文变化重新开始完整代际，翻页和排序可以复用当前有效列集。每个完整代际隔离旧 Provider 与旧行响应，避免不同上下文的列和行组合。

运行时列集可以返回不透明 Column Context Token；Shell 只把它作为独立读取上下文交给后续 `dataAdapter.list`，不解释、不持久化，也不放入 URL、筛选或列偏好。相同筛选条件下翻页和排序可以复用 token；筛选、刷新、相关上下文变化或错误重试必须重新解析列。计算型行数据仍只通过 `dataAdapter.list` 返回，不增加第二套列表读取入口。

未采用同步 `buildColumns`、metadata endpoint、Host/Shell 业务计算、React 列描述或静态列与运行时列的合并协议。同步 UI 回调不能表达远程加载、失败、重试、竞态或业务快照；首个纵向验证由独立 Example 模块提供当前周与下周日期列和计算型行数据，证明 seam 后才能删除 `buildColumns`，其他列表 UI 回调留待后续独立切片。runtime 列导出不属于首个切片，声明导出时必须配置失败。
