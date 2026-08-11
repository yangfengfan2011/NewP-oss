# nav metadata 统一页面入口一致性 Review

## 1. Review 范围

本 review 只回答一个问题：

**`nav metadata` 统一页面入口设计，是否与当前平台主线、仓库架构约束以及标准列表页模板设计保持一致。**

本次 review 不进入：

- 代码实现
- SDK 字段级改动
- 全模块迁移计划

## 2. 基线文档

本 review 以以下文档作为基线：

1. `docs/designs/platform/metadata-driven-backoffice-ui-design.md`
2. `docs/designs/platform/standard-list-page-template-design.md`
3. `docs/designs/platform/list-management-layout-demo-design.md`
4. `AGENTS.md`
5. `docs/designs/platform/nav-metadata-page-entry-design.md`

## 3. 总体结论

本次 review 的总体结论是：

**`nav metadata` 统一页面入口设计与当前平台主线整体一致，可以作为后续运行时设计与实施计划的输入。**

原因是：

- 它统一的是页面入口，而不是强迫所有页面走同一种模板
- 它让 `nav metadata` 保持声明性，没有直接耦合组件实现细节
- 它没有把 `custom` 页面错误拉入 `view metadata`
- 它和标准列表页模板设计形成了清晰分工

## 4. 一致项

### 4.1 与元数据边界一致

该设计仍然遵守仓库主线：

- `nav metadata` 管入口
- `view metadata` 管标准列表页结构
- 代码内核管装配、注册、校验与分流

没有把执行逻辑重新塞回元数据。

### 4.2 与“不是所有页面都要 metadata 化”的新边界一致

该设计明确区分了：

- `standard-list`
- `custom`

这意味着：

- 只有标准列表页走 `view metadata`
- `custom` 页只统一导航入口，不强行模板化

这个边界与当前达成的新共识一致。

### 4.3 与标准列表页模板设计一致

该设计并不替代 `standard-list-page-template-design.md`，而是补上其缺失的运行时入口层。

二者分工如下：

- `nav-metadata-page-entry-design.md`
  - 负责“页面怎么进来”
- `standard-list-page-template-design.md`
  - 负责“标准列表页进来以后怎么渲染”

### 4.4 与 Shell 边界一致

该设计要求：

- Shell 只负责读取入口、做受控装配和分流
- 真正的 `targetKey` 解析仍通过注册表完成

这符合仓库对 Shell 的职责限制。

## 5. 当前需要显式记录的边界

### 5.1 当前不是 `route metadata` 设计

虽然本设计已经开始接近“页面入口真源”，但它仍然不应被表述成完整 `route metadata` 设计。

当前更准确的定位是：

- `nav metadata` 统一导航可点击入口
- 运行时根据入口类型分流到模板页或自定义页

### 5.2 当前不展开 SDK 细节

本设计冻结了“需要注册表解析 `targetKey`”这一原则，但没有展开 SDK 最终字段结构，这一点是合理的。

否则会把当前设计范围扩大成“入口模型 + SDK 重构 + 实施计划”三件事混在一起。

### 5.3 当前只是纠正目标架构，不等于否定过渡实现

此前已经做出的 `StandardListPage + PersonListPage` 过渡实现仍有价值：

- 它先把列表页骨架抽到了共享层

但该 review 明确认为：

- 它不应被继续当作目标架构扩散

## 6. 阻断项与非阻断项判断

### 6.1 当前阻断项

当前未发现新的设计级阻断项。

### 6.2 当前非阻断项

以下事项当前不是阻断项，但后续计划中必须如实说明：

1. 现有模块仍主要通过模块代码声明页面入口
2. `targetKey` 对应注册表的 SDK/运行时字段仍需后续设计细化
3. 现有 `StandardListPage` 接入方式属于过渡态

## 7. 建议的下一步动作

最推荐的下一步是：

1. 回填 `standard-list-page-template-design.md`，明确其属于标准列表页内部渲染层，而不是最终页面入口模型
2. 以后续一份新计划来拆解：
   - `nav metadata` 入口升级
   - 注册表契约补齐
   - 标准列表页宿主接入

## 8. 结论

本次一致性 review 的最终结论是：

1. `nav metadata` 统一页面入口设计与当前主线一致
2. 它正确区分了标准列表页与自定义页面
3. 它补齐了标准列表页模板设计缺失的“入口层”
4. 它可以作为后续实施计划的正式输入

## 9. AI-First 最小检查清单

### 入口在哪里

- `docs/designs/platform/nav-metadata-page-entry-design.md`

### 边界在哪里

- `nav metadata` 管入口
- `view metadata` 只服务标准列表页
- `custom` 页面不进入 `view metadata`

### 如何验证

- 检查 `entryType` 和 `targetKey` 是否足够清晰
- 检查是否没有耦合组件名
- 检查是否没有把 `custom` 页面错误模板化

### 失败信号是什么

- `nav metadata` 变相写组件路径
- 标准列表页仍以业务页组件作为目标入口
- `custom` 页面被强拉到模板页体系

### 如何回滚

- 保留旧模块代码入口模式
- 暂不启用新入口分流
- 不影响当前页面实现
