---
status: accepted
---

# 标准 CRUD 页面统一采用 Ant Design 官方生态

平台标准 CRUD 页面统一由 Shell 共享宿主使用 `antd`、`@ant-design/icons` 与 `@ant-design/pro-components` 组合呈现。平台可以用薄封装组合这些官方组件，并使用无交互语义的普通 HTML 容器，但表单、按钮、菜单、表格、分页、标签、提示、抽屉、弹窗和空状态等交互或数据展示控件不得由业务模块改用原生控件、自绘控件或自行重写键盘、焦点、选择与弹层机制；视觉样式统一消费 Ant Design theme token。该选择保留 ProTable 等成熟能力，同时以组件来源契约、可访问性和共享测试约束平台一致性，避免严格限制为 `antd` 单包所导致的重复建设。

View metadata 拥有查询项、列、默认排序、响应式优先级、动作、权限与导出模式，Model metadata 拥有字段类型、枚举、Lookup、默认值、必填、只读与校验语义；Shell 共享宿主拥有布局、官方组件映射、URL 状态、分页、请求竞态以及加载和错误反馈，模块前端注册只提供业务 Lookup provider、handler、mapper 与必要 renderer。人员中心是首个完整验收页面，但不得形成专用宿主或复制标准列表布局；既有非 CRUD `StandardListPage` 不因本决策自动迁移。
