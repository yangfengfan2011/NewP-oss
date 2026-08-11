# 标准列表页模板宿主一致性 Review

## 1. Review 范围

本 review 只回答一个问题：

**`StandardListPageHost` 这一层的设计，是否与当前 `nav metadata` 页面入口设计、标准列表页模板设计以及仓库架构约束保持一致。**

本次 review 不进入：

- 代码实现
- SDK 细节字段设计
- 全模块迁移计划

## 2. 基线文档

本 review 以以下文档为基线：

1. `docs/designs/platform/nav-metadata-page-entry-design.md`
2. `docs/designs/platform/standard-list-page-template-design.md`
3. `docs/designs/platform/metadata-driven-backoffice-ui-design.md`
4. `AGENTS.md`
5. `docs/designs/platform/standard-list-page-host-design.md`

## 3. 总体结论

本次 review 的总体结论是：

**标准列表页模板宿主设计与当前平台主线整体一致，可以作为后续实施计划的直接输入。**

原因是：

- 它补齐了 `standard-list` 入口和模板渲染内核之间缺失的装配层
- 它没有把模板反向变成入口解析器
- 它没有让 `custom` 页面误入标准列表页宿主
- 它没有破坏 Shell / UI / 模块 provider 的职责边界

## 4. 一致项

### 4.1 与 `nav metadata` 页面入口设计一致

宿主页设计正确承接了：

- `entryType = standard-list`
- `targetKey = viewKey`

也就是说：

- 页面入口由 `nav metadata` 负责
- 宿主页负责消费该入口

这与入口设计完全对齐。

### 4.2 与标准列表页模板设计一致

宿主页设计明确了：

- `StandardListPage` 是渲染内核
- `StandardListPageHost` 是装配层

这样就把之前“模板既像入口又像渲染组件”的潜在混乱拆开了。

### 4.3 与仓库边界一致

宿主页建议放在 Shell 侧运行时层，而不是 `ui` 包。

这符合仓库边界：

- `ui` 放共享展示组件
- Shell 负责装配与受控入口

### 4.4 与 AI-First 原则一致

宿主页只接收 `viewKey`，边界清楚，失败信号显式，可验证性强。

这比把 metadata 装配逻辑分散到模板或业务页里更利于小上下文理解和回滚。

## 5. 当前需要显式记录的边界

### 5.1 宿主页不是新的页面解释器

它只负责：

- 加载 metadata
- 查注册表
- 组装页面输入

它不负责：

- 通用 DSL 解释
- 业务规则执行
- 统一所有页面类型

### 5.2 宿主页当前只服务标准列表页

当前设计不应被误读为：

- 后续所有页面都要进入 `StandardListPageHost`

它只服务 `standard-list` 入口。

### 5.3 当前不展开 provider 类型细节

当前设计只冻结职责，不冻结最终 SDK 类型，这是合理的。

否则会把宿主页设计与 SDK 变更绑死，扩大范围。

## 6. 阻断项与非阻断项判断

### 6.1 当前阻断项

当前未发现新的设计级阻断项。

### 6.2 当前非阻断项

以下事项当前不是阻断项，但需要在后续计划中显式说明：

1. 现有模块仍未提供正式的 `list view registry`
2. 当前 `StandardListPage` 接入方式仍是过渡态
3. Shell 侧还没有真正的模板入口路由分流实现

## 7. 建议的下一步动作

当前最推荐的下一步是：

1. 基于这三份设计：
   - `nav-metadata-page-entry-design.md`
   - `standard-list-page-host-design.md`
   - `standard-list-page-template-design.md`
2. 重写新的实施计划，按：
   - 入口真源
   - 宿主页
   - 注册表
   - 人员中心试点迁移
   的顺序推进

## 8. 结论

本次一致性 review 的最终结论是：

1. `StandardListPageHost` 设计与当前主线一致
2. 它正确补齐了入口层与模板层之间的装配层
3. 它没有破坏 UI / Shell / provider 的职责分层
4. 它可以作为下一份正式实施计划的输入

## 9. AI-First 最小检查清单

### 入口在哪里

- `docs/designs/platform/standard-list-page-host-design.md`

### 边界在哪里

- 宿主页负责装配
- 模板负责渲染
- provider 负责业务适配

### 如何验证

- 检查 `viewKey -> host -> registry -> template` 链路是否闭合
- 检查宿主页是否不承担业务逻辑

### 失败信号是什么

- 宿主页承担过多业务逻辑
- 模板承担入口解析
- `custom` 页面被错误导入宿主页链路

### 如何回滚

- 保留当前过渡实现
- 暂不启用宿主页
- 不影响前两份设计的边界定义
