# 主线设计一致性 Review

## 1. Review 范围

本 review 只针对当前已经形成主线关系的设计文档做一致性检查，不进入实施计划，不进入代码实现。

本次 review 以以下设计为范围：

1. `docs/designs/platform/metadata-driven-backoffice-ui-design.md`
2. `docs/designs/platform/standard-crud-contract-design.md`
3. `docs/designs/modules/organization-person-center-pilot-design.md`
4. `docs/designs/platform/list-management-layout-demo-design.md`
5. `docs/designs/platform/dic-localization-manifest-alignment-design.md`
6. `docs/designs/user-visible-text-localization-foundation-design.md`
7. `docs/designs/metadata-authoring-layout-unification-design.md`

补充说明：

- 第 7 份文档的实际路径位于 `docs/designs/` 根下，而不是 `docs/designs/platform/`。
- 该路径差异不构成设计冲突，但后续引用时应统一写实际路径，避免新 agent 或后续文档引用错误。

## 2. 当前阶段基线设计清单

本轮 review 后，建议把以下内容视为当前阶段的主线基线结论。

### 2.1 后台显示层主线

- 显示层当前只验证三层元数据：
  - `nav metadata`
  - `view metadata`
  - `model metadata`
- `view metadata` 当前只保留：
  - `list`
  - `detail`
- `tree view` 暂时后置

### 2.2 list view 结构

- `query.fields` 只定义轻筛选条字段
- `advancedFilter.sections.fields` 定义高级筛选抽屉字段
- 不再使用 `quickFields`
- 查询字段和高级筛选字段都显式声明：
  - `key`
  - `labelKey`
  - `source`
  - `field` 或 `preset`
  - `operator`
- `operator` 只允许一个，且必须显式声明
- 当前允许的 `operator`：
  - `=`
  - `like`
  - `>`
  - `>=`
  - `<`
  - `<=`
  - `in`
- 平台当前不做“字段类型 -> operator”的默认限制映射
- `list.columns` 也采用显式列定义
- 导出是 `list` 内建能力，不进入 `pageActions`

### 2.3 detail view 结构

- `detail view` 是“单对象维护页”，不是所有对象都必须有
- `editorMode` 只作用于标准维护动作：
  - `create`
  - `edit`
  - `view`
- `editorMode` 取值：
  - `page`
  - `drawer`
  - `dialog`
- `detail view` 最小结构：
  - `sections`
  - `summaryBlocks`
  - `pageActions`

### 2.4 动作元数据

- `action metadata` 只声明入口，不承载执行逻辑
- 当前最小字段：
  - `key`
  - `labelKey`
  - `selectionRequirement`
  - `confirmKey`
- 权限实现、对象授权、统一规则中心全部后置

### 2.5 多语言消费规则

- 一个 `view` 对应一个 `dic` 文件/命名空间
- `view metadata` 中显式声明：
  - `dicKey`
  - `titleKey`
  - `descriptionKey`
  - `labelKey`
  - `confirmKey`
- 局部 key 若需要层次感，使用 `_`，不用 `.`
- 运行时统一拼成：
  - `${dicKey}.${localKey}`
- 前端继续消费现有：
  - `catalogs = 语言 -> flat key -> 文案`
  - `t(key)`

### 2.6 metadata authoring 布局

- 权威 authoring 根目录统一为：
  - `modules/<ModuleId>/metadata/**`
- 平台共享 authoring 根目录统一为：
  - `modules/Platform/metadata/**`
- 平台内核执行级元数据保留在：
  - `server/platform/metadata/**`
- 文件类型判定以文件内容中的 `type` 为准
- 文件名与业务根对象标识一致性必须显式校验

## 3. 一致性结论

### 3.1 总体结论

本次 review 的总体结论是：

**当前主线设计整体一致，可以进入下一步更聚焦的设计 review 或实施计划准备；未发现必须推翻主线重来的设计级冲突。**

也就是说，以下三条主线当前是咬合的：

1. 后台显示层主线  
   `nav / view / model`

2. 标准 CRUD 契约主线  
   `list / detail / create / update / enable-disable / delete / options`

3. 多语言与 metadata authoring 主线  
   `dicKey + localKey -> flat key`  
   `modules/<ModuleId>/metadata/** + type 自描述`

### 3.2 显示层与 CRUD 的一致性

`metadata-driven-backoffice-ui-design.md` 与 `standard-crud-contract-design.md` 当前是对齐的：

- `list view` 需要的 query / columns / defaultSort / export
- `detail view` 需要的 detail read model / summaries
- `editorMode = page` 对应 `detailViewKey`
- projection 字段与 model 字段的区分

两份文档之间没有明显职责冲突。

### 3.3 显示层与组织模块试点的一致性

`organization-person-center-pilot-design.md` 当前是对显示层主线与 CRUD 主线的受控映射，不是另外发明一套例外规则。

当前试点文档遵守了以下边界：

- 仍以 `list/detail` 为主线
- 仍以 `editorMode = page` 作为人员中心默认承载方式
- 仍把绑定账号与授权摘要放在辅助验证线，而不是主阻断链路
- 没有把权限、规则中心、route metadata 拉入当前范围

因此试点设计与平台主线设计是一致的。

### 3.4 多语言规则与 authoring 统一布局的一致性

`dic-localization-manifest-alignment-design.md`、`user-visible-text-localization-foundation-design.md`、`metadata-authoring-layout-unification-design.md` 三份文档当前也是一致的：

- `dic` 的运行时消费维持：
  - `catalogs + t(key)`
- `dic` 的 authoring 真源回到：
  - `modules/<ModuleId>/metadata/**`
- `dic` 的识别规则回到：
  - `type = "dic"`
- `dicKey` 应来自文件内容中的业务根对象命名空间，而不是长期依赖文件名推导

这三份文档之间没有方向冲突，只有“实现现状尚未完全对齐”的问题。

## 4. 当前不一致点

本节只记录**设计层面仍需要明确指出的点**，不展开实施方案。

### 4.1 路径引用仍有不统一

当前最明确的不一致点不是设计内容本身，而是文档引用路径：

- `metadata-authoring-layout-unification-design.md` 实际位于 `docs/designs/`
- 但前文曾按 `docs/designs/platform/` 引用

这不影响设计内容，但会影响后续 review、计划和 agent 入口判断。

### 4.2 当前主线仍缺少一份“阶段基线说明”

虽然几份设计本身已经可以互相咬合，但当前仍缺一份短文档明确回答：

- 当前阶段哪些文档是唯一基线
- 哪些旧专项文档已经不再作为主入口
- 下一步应先做设计 review，还是实施计划，还是试点计划

这不构成设计冲突，但会增加后续协作时拿错入口的风险。

### 4.3 `nav / view` 尚未进入实现，当前不存在实现级结论

这不是设计冲突，但需要明确记录：

- 当前 `nav / view` 的一致性结论停留在设计层
- 还没有进入正式消费实现
- 因此当前 review 只能确认“设计自洽”，不能确认“运行时已验证”

### 4.4 metadata 消费端对新 authoring 规则的实现一致性，不属于本 review 结论本身

这次 review 只看设计，不把当前消费实现问题混入主线结论。

也就是说：

- 即便当前 `.dic / .model / .settings` 的部分消费实现还存在旧假设
- 也不改变本次设计 review 的结论
- 那部分问题应归入单独的“消费链一致性收口”工作，而不是回头推翻主线设计

## 5. 阻断项与非阻断项

### 5.1 当前阻断项

从“继续做设计 review 或进入下一步计划准备”的角度看，当前**没有必须先补新平台设计才能继续前进的阻断项**。

换句话说：

- 当前不是“设计不完整，无法继续”
- 当前更像是“设计已足够，需要先做正式一致性结论收口，再进入下一步”

### 5.2 当前非阻断项

以下内容当前都属于非阻断项：

- `tree view`
- `route metadata`
- 功能授权与对象授权实现
- 统一规则中心
- `enum` 体系扩展
- 批量导入导出任务
- 列宽持久化

这些点都已经在现有设计中被明确后置，不应反向拖住当前主线。

## 6. 建议的下一步设计动作

基于本次 review，当前最推荐的下一步不是继续补平台主设计，而是做下面两件事中的第一件：

### 6.1 第一优先级

**基于本次 review 结论，继续做“组织模块人员中心试点”的设计一致性 review。**

重点不是再扩平台设计，而是确认：

- 组织模块试点是否已经具备进入实施计划准备的设计基础
- 当前试点范围是否需要进一步冻结
- 当前试点验收口径是否已经足够清晰

### 6.2 第二优先级

当组织模块试点设计 review 通过后，再进入：

- 试点实施计划准备

而不是现在就直接进入实施。

## 7. 结论

本次主线设计 review 的最终结论是：

1. 当前主线设计整体一致
2. 当前没有必须再补一个新平台设计才能继续前进的阻断项
3. 当前最合理的下一步是继续做更聚焦的试点设计 review，而不是立即补新平台设计或直接进入实施计划

## 8. AI-First 最小检查清单

### 入口在哪里

- `docs/designs/platform/metadata-driven-backoffice-ui-design.md`
- `docs/designs/platform/standard-crud-contract-design.md`
- `docs/designs/modules/organization-person-center-pilot-design.md`
- `docs/designs/platform/list-management-layout-demo-design.md`
- `docs/designs/platform/dic-localization-manifest-alignment-design.md`
- `docs/designs/user-visible-text-localization-foundation-design.md`
- `docs/designs/metadata-authoring-layout-unification-design.md`

### 边界在哪里

- 本文档只做设计一致性 review
- 不进入实施计划
- 不进入代码实现
- 不重新讨论已经收口的显示层主线结论

### 如何验证

- 检查显示层、CRUD、试点、多语言、authoring 布局五条设计线是否互相冲突
- 检查是否还有必须先补设计才能继续前进的阻断项
- 检查当前阶段基线文档清单是否明确

### 失败信号是什么

- 文档之间对 `nav / view / model` 边界给出互相冲突的定义
- 文档之间对 `dicKey`、flat key、authoring 真源位置给出冲突结论
- 试点设计为了成立，不得不反向推翻平台主线设计

### 如何回滚

- 若后续 review 发现本文结论有偏差，直接更新或回退本文即可
- 不影响现有主设计文档
- 不影响后续试点计划是否单独重写
