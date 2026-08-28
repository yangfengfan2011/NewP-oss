---
status: accepted
---

# 标准 CRUD 页面保留 ProTable 并采用 Ant Design 官方控件

平台标准 CRUD 页面统一由 Shell 共享宿主使用 `antd`、`@ant-design/icons` 以及基于这些官方控件的平台薄封装呈现，标准列表继续使用 ProTable 保留成熟的查询、表格、分页、排序与响应式能力。ProField、ProForm 或其他 Pro 组件不是标准 CRUD 的公共契约，可以按实际交互和构建预算选用；不得为了复用未使用能力而让详情、创建、查看、编辑或抽屉场景静态进入列表首次加载图。表单、按钮、菜单、表格、分页、标签、提示、抽屉、弹窗和空状态等交互或数据展示控件不得由业务模块改用原生控件、自绘控件或自行重写键盘、焦点、选择与弹层机制；视觉样式统一消费 Ant Design theme token。

共享宿主应以实际交互为装载边界：列表首次进入下载 ProTable 及查询、表格和分页所需能力，详情、创建、查看、编辑和抽屉能力在对应路由或动作命中后再加载。全局默认 chunk 门槛保持 500,000 B；只有 source map 能以唯一来源、目标场景和目标列表 capability 静态闭包共同归因于 ProTable 或其静态运行时依赖的 chunk，才可以使用 550,000 B 的专属门槛，Personnel 首次进入相对首页的 gzip 增量门槛为 620,000 B。不能唯一归因、位于目标闭包外或混入无关业务实现的 chunk 仍执行全局门槛，Vite 默认 warning 继续保留为证据。

真实浏览器 Gate 同样承认 ProTable 的固定装载成本：Personnel 首次进入的交互时间预算为 p50 不超过 6,200 ms、p95 不超过 6,500 ms，相对正式 baseline 的变化继续记录但不作为硬门槛；首页 p50 不超过 3,700 ms，且相对正式 baseline 的改善至少为 30%，首页 p95 不超过 3,900 ms，其相对变化只记录、不作为硬门槛。认证后 Shell 的全局导航属于持续可见的实际交互；当官方 Ant Design 导航与延迟装载的 CRUD 控件因组件库共享依赖和 Vite/Rollup 分块共同导致 `rc-picker` 进入 `authenticatedShell` 静态闭包时，该结构规则保留来源与路径报告但不作为失败项。此例外只适用于 `authenticatedShell`；`anonymous` 和 `moduleBindingEager` 仍禁止 `rc-picker`，所有 chunk、场景 gzip 与真实浏览器预算继续执行。组件选择还必须通过组件来源契约、可访问性、共享行为测试和真实浏览器 Gate；不得用 `manualChunks`、提高全局限制、复用缓存、拼接样本或改变统计口径替代物理依赖收敛。

View metadata 拥有查询项、列、默认排序、响应式优先级、动作、权限与导出模式，Model metadata 拥有字段类型、枚举、Lookup、存储可空性、创建或编辑输入约束、创建默认值、只读与校验语义；这些 Required 维度按 [ADR-0043](0043-required-fields-use-explicit-write-and-migration-lifecycles.md) 独立解释，不能互相推断。Shell 共享宿主拥有布局、官方组件映射、URL 状态、分页、请求竞态以及加载和错误反馈，模块前端注册只提供业务 Lookup provider、handler、mapper 与必要 renderer。人员中心是首个完整验收页面，但不得形成专用宿主或复制标准列表布局；既有非 CRUD `StandardListPage` 不因本决策自动迁移。
