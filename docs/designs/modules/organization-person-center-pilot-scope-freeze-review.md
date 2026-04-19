# 组织模块人员中心试点范围冻结 Review

## 1. Review 范围

本 review 的目标不是再次判断“组织模块人员中心试点是否与平台主线一致”，而是进一步回答：

**当前试点是否已经具备进入实施计划准备的冻结程度。**

本次 review 只聚焦以下三件事：

1. 首轮删除策略是否应冻结
2. 首轮 `summaryBlocks` 的最低承接范围是否应冻结
3. 首轮必须实施的真实边界是否应冻结

本次 review 以以下文档为范围：

1. `docs/designs/modules/organization-person-center-pilot-design.md`
2. `docs/designs/modules/organization-person-center-pilot-consistency-review.md`
3. `docs/designs/platform/mainline-design-consistency-review.md`
4. `docs/designs/platform/metadata-driven-backoffice-ui-design.md`
5. `docs/designs/platform/standard-crud-contract-design.md`

本 review 不进入：

- 实施计划
- 代码实现
- 其他模块扩展

## 2. 当前冻结目标

本次 review 结束后，期望把组织模块人员中心试点冻结到以下程度：

- 可以明确首轮做什么
- 可以明确首轮不做什么
- 可以明确下一步实施计划应只围绕哪些真实文件、接口和页面展开

这意味着本次冻结 review 不追求把所有细节都定满，而是只冻结会直接影响实施范围和验收口径的部分。

## 3. 冻结结论

### 3.1 首轮删除策略

建议冻结结论为：

**首轮删除策略按 `none` 冻结。**

也就是：

- 人员中心首轮主流程只验证：
  - 列表
  - 详情
  - 编辑
  - 停用
- 删除能力当前只保留设计位置与文档口径
- 首轮不把 `delete` 作为必须打通的真实操作链路

原因：

1. 当前试点主目标是验证：
   - 显示层三层边界
   - 标准 CRUD 主干
   - 真实列表/详情/编辑闭环

2. 删除天然更容易把试点拉向：
   - 复杂规则判断
   - 引用校验
   - 跨模块状态约束

3. 当前阶段即便标准 CRUD 设计中保留了 `delete` 分类，也不等于首轮试点必须开放删除入口

因此，当前最稳妥的冻结方式不是“再留一点选择空间”，而是明确写死：

- **首轮人员中心试点不开放删除入口**

### 3.2 首轮 `summaryBlocks` 最低承接范围

建议冻结结论为：

**首轮 `summaryBlocks` 只冻结块位和状态化承接，不冻结复杂交互和完整业务内容。**

也就是：

- 详情页首轮保留两个摘要块：
  - `userLink`
  - `authorizationSummary`
- 首轮最低要求是：
  - 两个块都能稳定装配
  - 两个块都能返回状态化结构
  - 页面能按状态做只读降级展示

首轮最低状态建议冻结为：

- `ready`
- `notConfigured`
- `dependencyUnavailable`

首轮不冻结：

- 复杂摘要交互
- 详细权限摘要展开
- 账号侧或授权侧的编辑操作链路

原因：

1. 试点当前需要验证的是：
   - `detail view.summaryBlocks` 这条结构是否成立
   - detail read model 是否能承接主字段与摘要块并存

2. 当前不需要把 `userLink` 或 `authorizationSummary` 做成完整二级业务页

3. 只要状态化结构和只读降级成立，就足以验证这条设计主线

因此，建议把 `summaryBlocks` 的首轮冻结口径写成：

- **必须有块位**
- **必须有状态**
- **允许内容最小化**

### 3.3 首轮必须实施的真实边界

建议冻结结论为：

**首轮试点只围绕“导航入口 + 人员列表 + 人员详情 + 编辑 + 停用 + 只读摘要”这条最小闭环实施。**

对应到真实边界，建议冻结为以下几类。

#### 3.3.1 必须新增或收口的 metadata

1. 组织模块导航 metadata
2. 人员列表 view metadata
3. 人员详情 view metadata
4. 人员模型 metadata 的真实消费映射
5. 人员列表 / 详情对应的 dic 文案

#### 3.3.2 必须补齐或收口的后端接口

1. persons list
2. persons detail
3. persons create
4. persons update
5. persons disable
6. 公司 / 部门 / 岗位 options

首轮不要求：

- persons delete
- 完整 enable/disable 双向闭环中的 enable 入口一定在 UI 暴露

如果后端保留 enable 接口作为标准分类完整性的一部分，可以接受；  
但首轮试点 UI 主链路只要求停用。

#### 3.3.3 必须收口的前端页面

1. `PersonListPage`
2. `PersonDetailPage`
3. `organizationService.ts` 中与 persons 相关的真实服务方法

首轮应做到：

- 列表真实查询
- 列表真实列渲染
- 默认排序
- 导出入口位置与语义稳定
- 详情读取
- 编辑保存
- 停用
- 摘要块只读降级展示

#### 3.3.4 当前明确不纳入首轮的内容

1. 删除主链路
2. 绑定账号编辑链路
3. 授权摘要复杂交互
4. tree view
5. route metadata
6. 功能授权 / 对象授权实现
7. 统一规则中心
8. 组织树页 / 岗位页同步标准化

## 4. 冻结后的试点范围表述

本次冻结 review 后，建议把组织模块人员中心试点当前范围统一表述为：

### 4.1 首轮主流程

1. 从左侧导航进入人员中心
2. 通过列表页完成人员查询、查看、编辑入口与停用入口
3. 进入详情页完成基础信息查看与编辑
4. 在详情页查看账号绑定状态与授权摘要的只读状态化结果

### 4.2 首轮不做

1. 删除
2. 复杂摘要交互
3. 绑定账号与授权编辑主链路
4. 权限、规则中心、tree、route metadata

### 4.3 首轮验收重点

1. 显示层三层边界不失守
2. 标准 CRUD 主干在真实模块中成立
3. 多语言 key 消费链路成立
4. `summaryBlocks` 的块位与状态化承接成立

## 5. 当前阻断项与非阻断项

### 5.1 当前阻断项

本次冻结 review 后，当前**不再有阻断进入实施计划准备的试点范围问题**。

也就是说，只要接受本 review 的冻结结论：

- `delete = none`
- `summaryBlocks = 块位 + 状态化承接`
- 首轮边界按最小闭环收口

则组织模块人员中心试点已经具备进入实施计划准备的条件。

### 5.2 当前非阻断项

以下内容当前仍然是明确的非阻断项：

- enable 的完整前端回开入口
- 删除策略后续重启
- 绑定账号复杂交互
- 授权摘要复杂交互
- 组织树页
- 岗位页
- 权限与规则中心

## 6. 建议的下一步动作

基于本次冻结 review，当前最推荐的下一步就是：

**进入组织模块人员中心试点实施计划准备。**

而且实施计划应严格以本次冻结结论为边界，不再重新打开以下问题：

1. 删除首轮要不要做
2. `summaryBlocks` 首轮是否要做复杂交互
3. 首轮要不要把绑定账号和授权编辑拉进主链路

## 7. 结论

本次组织模块人员中心试点范围冻结 review 的最终结论是：

1. 首轮删除策略冻结为 `none`
2. 首轮 `summaryBlocks` 冻结为“块位 + 状态化承接 + 只读降级展示”
3. 首轮必须实施的真实边界已足够明确
4. 当前可以进入组织模块人员中心试点实施计划准备

## 8. AI-First 最小检查清单

### 入口在哪里

- `docs/designs/modules/organization-person-center-pilot-design.md`
- `docs/designs/modules/organization-person-center-pilot-consistency-review.md`
- `docs/designs/modules/organization-person-center-pilot-scope-freeze-review.md`
- `docs/designs/platform/mainline-design-consistency-review.md`
- `docs/designs/platform/metadata-driven-backoffice-ui-design.md`
- `docs/designs/platform/standard-crud-contract-design.md`

### 边界在哪里

- 本文档只做试点范围冻结 review
- 不进入实施计划
- 不进入代码实现
- 不重新讨论平台主线结论

### 如何验证

- 检查删除策略是否已冻结
- 检查 `summaryBlocks` 首轮承接范围是否已冻结
- 检查首轮必须实施的真实边界是否已足够明确
- 检查是否已达到进入实施计划准备的条件

### 失败信号是什么

- 首轮是否做删除仍然没有明确结论
- `summaryBlocks` 首轮仍然既想最小承接，又想同时承担复杂交互
- 首轮边界仍然把绑定账号、授权编辑、tree、权限、规则中心混入主流程

### 如何回滚

- 若后续发现冻结范围过宽或过窄，直接更新或回退本文即可
- 不影响平台主线设计文档
- 不影响后续实施计划重新调整范围
