# 组织模块人员中心试点实施计划一致性 Review

## 1. Review 范围

本 review 的目标不是重写实施计划，而是回答：

**`docs/superpowers/plans/2026-04-20-organization-person-center-pilot.md` 当前是否已经与既有设计、冻结结论、仓库约束和真实代码现状保持一致，并具备进入正式实施的条件。**

本次 review 只检查以下几类一致性：

1. 计划是否仍然遵守主线设计与试点范围冻结结论
2. 计划中的文件范围、命令和验证路径是否与仓库现状一致
3. 计划是否还保留会在实施首步直接卡住的隐含前提

本次 review 不进入：

- 代码实现
- 新的平台设计扩展
- `.dic / .model / .settings` 消费链专项计划重写

## 2. 基线文档

本次 review 以以下文档作为基线：

1. `docs/designs/platform/mainline-design-consistency-review.md`
2. `docs/designs/modules/organization-person-center-pilot-consistency-review.md`
3. `docs/designs/modules/organization-person-center-pilot-scope-freeze-review.md`
4. `docs/designs/platform/review-open-items-tracker.md`
5. `docs/designs/modules/organization-person-center-pilot-design.md`
6. `docs/superpowers/plans/2026-04-20-organization-person-center-pilot.md`

同时参考当前代码现状：

1. `frontend/modules/organization/package.json`
2. `tests/Organization.Tests/Organization.Tests.csproj`
3. `tests/Organization.Tests/OrganizationModuleTests.cs`

## 3. 总体结论

当前这份试点实施计划在**范围边界、设计口径和主干任务拆分**上，整体与前面几份 review 保持一致。

也就是说，以下冻结结论都已经被正确吸收到计划中：

1. 首轮删除策略为 `none`
2. 首轮 `summaryBlocks` 只做“块位 + 状态化承接 + 只读降级展示”
3. 首轮真实边界只围绕导航入口、人员列表、人员详情、编辑、停用、只读摘要
4. 首轮不拉入 `tree view`、`route metadata`、权限与规则中心、复杂摘要交互

同时，上一轮 review 中指出的三个计划问题，当前已经被吸收到计划正文中：

1. 已显式写入 `metadata consumer alignment` 前提
2. Task 3 已收口为“基于现有前端构建暴露缺口”，不再假定模块已有 `vitest`
3. Task 4 已降级为“存在稳定 smoke 入口时才执行，否则显式记录跳过”

因此，当前计划**已经具备进入正式实施的条件**。

## 4. 当前一致项

## 4.1 范围一致

当前计划没有把删除、绑定账号复杂交互、授权复杂交互、组织树页、岗位页重新拉回首轮范围，这一点与试点范围冻结 review 一致。

## 4.2 元数据边界一致

当前计划只把组织模块试点所需的 `nav / view / model / dic` authoring 真源纳入范围，没有重新引入 `route metadata`，这一点与显示层主线结论一致。

## 4.3 前后端职责划分一致

当前计划把：

1. 查询、详情、停用、摘要状态化结构
2. 页面列表、详情、只读摘要呈现
3. metadata authoring 真源

拆成后端、前端、metadata 三组任务，整体符合“元数据只声明、执行逻辑收口在代码内核”的架构约束。

## 5. 当前不一致点

### 5.1 当前未发现新的阻断性计划不一致点

本轮复核时，上一轮 plan review 里指出的计划级问题都已经修正完成。  
按当前文本状态，未再发现会在实施首步直接卡住的计划级阻断项。

### 5.2 仍需明确记录的边界说明

当前仍有两类内容需要在实施执行时保持边界清晰，但它们已经不是计划文本不一致，而是实施时的显式前提：

1. `.dic / .model / .settings` 消费链应视为已就绪前提，不得被重新并回当前试点计划
2. Task 4 的 metadata smoke 只有在当时确有稳定入口时才执行，不得为了补 smoke 再顺手扩范围

## 6. 阻断项与非阻断项判断

### 6.1 当前阻断项

当前**没有新的计划级阻断项**。

### 6.2 当前非阻断项

以下内容当前仍属于非阻断项，但需要在实施结果汇报中如实说明：

1. Task 4 的 metadata smoke 可能会因为缺少稳定入口而被跳过
2. 前端验证当前仍以构建与联调为主，不额外扩充模块级测试基础设施

## 7. 建议的下一步动作

当前最推荐的下一步已经不是继续修计划，而是：

**以 `2026-04-20-organization-person-center-pilot.md` 作为正式实施基线进入执行。**

实施时只需要继续遵守两条边界：

1. 不重新打开 `delete = none`
2. 不为补验证顺手扩出前端测试基础设施或新的 metadata consumer 专项工作

## 8. 结论

本次实施计划一致性 review 的最终结论是：

1. 当前试点实施计划与主线设计、试点冻结结论整体一致
2. 上一轮 plan review 提出的计划问题已经被吸收到正文
3. 当前不存在新的计划级阻断项
4. 当前计划已经可以作为正式实施基线使用

## 9. AI-First 最小检查清单

### 入口在哪里

- `docs/superpowers/plans/2026-04-20-organization-person-center-pilot.md`
- `docs/designs/modules/organization-person-center-pilot-scope-freeze-review.md`
- `docs/designs/platform/review-open-items-tracker.md`

### 边界在哪里

- 本文档只做实施计划一致性 review
- 不重写试点设计
- 不进入代码实现
- 不把 metadata consumer 专项重新并入本计划

### 如何验证

- 检查计划是否仍遵守 `delete = none`
- 检查 `summaryBlocks` 是否仍保持最小状态化承接
- 检查计划里的验证命令是否仍与仓库现状一致
- 检查前轮指出的计划问题是否已被正文吸收

### 失败信号是什么

- 计划重新要求不存在的前端测试基础设施
- 计划重新把可选 smoke 写回强验收项
- 计划重新把 metadata consumer 对齐专项并回试点主计划

### 如何回滚

- 若后续发现本 review 判断过严或过宽，直接回退本文或调整计划即可
- 不影响前面的主线 design review
- 不影响后续重新生成试点实施计划
