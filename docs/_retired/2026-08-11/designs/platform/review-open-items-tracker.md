# Review 不一致点跟踪清单

## 1. 目的

本清单用于承接当前几份 design review 中提到的“不一致点”，把它们按处理状态重新分组，避免后续进入实施计划时：

- 误把已经解决的问题继续当阻断项
- 漏掉那些已经识别但尚未进入计划吸收的问题
- 把纯文档整理问题误当成主线阻断

本清单不替代原 review 文档，只负责给下一步实施计划准备提供更直接的入口。

## 2. 基线来源

本清单汇总以下 review 文档中的不一致点：

1. `docs/designs/platform/mainline-design-consistency-review.md`
2. `docs/designs/modules/organization-person-center-pilot-consistency-review.md`
3. `docs/designs/modules/organization-person-center-pilot-scope-freeze-review.md`
4. `docs/designs/platform/metadata-consumer-alignment-review.md`

## 3. 已解决

以下问题已经在当前 review 阶段被明确解决，不再作为下一步阻断项。

### 3.1 组织模块试点首轮删除策略

状态：

- 已解决

结论：

- 首轮冻结为 `none`
- 首轮试点不开放删除入口

来源：

- `organization-person-center-pilot-scope-freeze-review.md`

### 3.2 组织模块试点 `summaryBlocks` 首轮最低承接范围

状态：

- 已解决

结论：

- 首轮只冻结：
  - 块位
  - 状态化承接
  - 只读降级展示
- 不冻结复杂交互

来源：

- `organization-person-center-pilot-scope-freeze-review.md`

### 3.3 组织模块试点首轮真实边界

状态：

- 已解决

结论：

- 首轮只围绕：
  - 导航入口
  - 人员列表
  - 人员详情
  - 编辑
  - 停用
  - 只读摘要

来源：

- `organization-person-center-pilot-scope-freeze-review.md`

## 4. 已冻结，待计划吸收

以下问题已经被识别，且当前不需要再继续做 design review，但必须在后续实施计划中显式吸收。

### 4.1 `.dic` 消费链仍未完全对齐 authoring 统一规则

状态：

- 已冻结，待计划吸收

后续计划必须吸收的内容：

- 不再依赖 `metadata/dictionaries/*.dic.json`
- `type = "dic"` 成为真正门禁
- `dicKey` 来自业务根对象
- 同词条按模块顺序覆盖

来源：

- `metadata-consumer-alignment-review.md`

### 4.2 `.model` 消费链仍有固定目录 / 固定文件路径依赖

状态：

- 已冻结，待计划吸收

后续计划必须吸收的内容：

- 不再要求 `metadata/models`
- 共享单业务根对象 loader
- 模块消费与工具消费使用同一套规则

来源：

- `metadata-consumer-alignment-review.md`

### 4.3 `.settings` 消费链仍主要依赖单一 `SourcePath`

状态：

- 已冻结，待计划吸收

后续计划必须吸收的内容：

- authoring/apply 优先消费 `SourceFiles`
- adapter 显式校验 `type = "settings"`
- apply 不再只假设单目录整体替换

来源：

- `metadata-consumer-alignment-review.md`

### 4.4 组织模块人员中心试点实施计划必须以前三项冻结结论为边界

状态：

- 已冻结，待计划吸收

后续计划必须吸收的内容：

- `delete = none`
- `summaryBlocks = 块位 + 状态化承接 + 只读降级`
- 只做最小闭环，不拉入绑定账号复杂交互、授权复杂交互、tree、route metadata、权限与规则中心

来源：

- `organization-person-center-pilot-scope-freeze-review.md`

## 5. 非阻断，后续整理

以下问题当前不阻断进入下一步实施计划准备，但建议在后续文档整理或计划落地时顺手收口。

### 5.1 `metadata-authoring-layout-unification-design.md` 的引用路径不统一

状态：

- 非阻断，后续整理

说明：

- 实际路径位于 `docs/designs/`
- 后续文档引用应统一到真实路径

来源：

- `mainline-design-consistency-review.md`

### 5.2 当前阶段“唯一基线文档列表”仍未单独固化

状态：

- 非阻断，后续整理

说明：

- 虽然 review 文档里已经隐含了基线范围
- 但后续若协作面继续扩大，仍建议补一份更直接的基线入口说明

来源：

- `mainline-design-consistency-review.md`

### 5.3 `nav / view` 当前只有设计结论，暂无实现级验证结论

状态：

- 非阻断，后续整理

说明：

- 当前只能确认设计自洽
- 还不能写成“运行时已验证成立”

来源：

- `mainline-design-consistency-review.md`

## 6. 当前结论

到当前为止，这些 review 文档里的不一致点已经可以分成清楚的三类：

1. 已解决  
   这些问题已经在 review 阶段形成明确冻结结论，不应再反复打开。

2. 已冻结，待计划吸收  
   这些问题不需要继续做 review，但必须进入后续实施计划。

3. 非阻断，后续整理  
   这些问题当前不应拖住主线继续前进。

因此，当前最合理的下一步是：

**进入实施计划准备时，显式吸收“待计划吸收”这一组问题，而不是继续横向补更多 review。**

## 7. AI-First 最小检查清单

### 入口在哪里

- `docs/designs/platform/mainline-design-consistency-review.md`
- `docs/designs/modules/organization-person-center-pilot-consistency-review.md`
- `docs/designs/modules/organization-person-center-pilot-scope-freeze-review.md`
- `docs/designs/platform/metadata-consumer-alignment-review.md`
- `docs/designs/platform/review-open-items-tracker.md`

### 边界在哪里

- 本文档只做 review 结果归并
- 不新增设计结论
- 不写实施计划
- 不进入代码实现

### 如何验证

- 检查所有 review 中提到的不一致点是否都被归类
- 检查哪些问题已解决，哪些仍需进入计划
- 检查当前是否还有被误标为阻断项的问题

### 失败信号是什么

- 已冻结的问题在下一步又被重新当成开放问题讨论
- 应进入实施计划的问题没有被带入计划
- 纯文档整理问题被误当成主线阻断项

### 如何回滚

- 若后续 review 结论更新，直接调整本清单归类即可
- 不影响原 review 文档
- 不影响后续实施计划重写
