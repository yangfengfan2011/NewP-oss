# 组织模块人员中心试点设计一致性 Review

## 1. Review 范围

本 review 只针对“组织模块人员中心试点”与当前平台主线设计之间的一致性做检查，不进入实施计划，不进入代码实现。

本次 review 以以下文档为范围：

1. `docs/designs/modules/organization-person-center-pilot-design.md`
2. `docs/designs/platform/metadata-driven-backoffice-ui-design.md`
3. `docs/designs/platform/standard-crud-contract-design.md`
4. `docs/designs/platform/list-management-layout-demo-design.md`
5. `docs/designs/platform/dic-localization-manifest-alignment-design.md`
6. `docs/designs/user-visible-text-localization-foundation-design.md`
7. `docs/designs/metadata-authoring-layout-unification-design.md`
8. `docs/designs/platform/mainline-design-consistency-review.md`

## 2. 当前试点基线

本轮 review 后，建议把组织模块人员中心试点的当前基线收成如下。

### 2.1 试点目标

组织模块“人员中心维护”当前仍是首条真实试点主线，用于同时验证：

- 后台显示层三层元数据边界：
  - `nav metadata`
  - `view metadata`
  - `model metadata`
- 标准 CRUD 契约能否承接真实业务模块
- 多语言文案消费规则能否落到真实列表页和详情页

### 2.2 试点范围

当前试点范围固定为：

1. 从左侧导航进入“人员中心”
2. 人员列表查询
3. 人员详情读取
4. 人员基础信息编辑
5. 人员停用
6. 删除能力首轮按“受限删除或显式暂不开放”处理

辅助验证项仍然是：

- 账号绑定状态
- 授权摘要

它们当前不应反向拖垮试点主流程。

### 2.3 试点页面结构

人员中心当前默认采用：

- `list view`
- `editorMode = page`
- `detail view`

列表页当前基线为：

- `query.fields`
- 可选 `advancedFilter`
- `list.columns`
- `pageActions`
- `rowActions`
- `export current / all`

详情页当前基线为：

- `sections`
- `summaryBlocks`
- `pageActions`

### 2.4 试点多语言规则

人员中心试点当前仍应遵守统一规则：

- 一个 view 对应一个 dic 文件 / 命名空间
- `view metadata` 显式声明：
  - `dicKey`
  - `titleKey`
  - `descriptionKey`
  - `labelKey`
  - `confirmKey`
- 前端运行时仍按：
  - `${dicKey}.${localKey}`
  - `catalogs + t(key)`

## 3. 一致性结论

### 3.1 总体结论

本次 review 的总体结论是：

**组织模块人员中心试点设计与当前平台主线设计整体一致，可以作为下一阶段实施计划准备的直接输入。**

也就是说，当前没有发现“为了让人员中心试点成立，必须反向推翻平台主线设计”的冲突。

### 3.2 与显示层主线的一致性

`organization-person-center-pilot-design.md` 与 `metadata-driven-backoffice-ui-design.md` 当前是对齐的。

具体表现在：

1. 试点没有绕开三层边界  
   仍然是：
   - `nav` 管左侧入口
   - `view` 管页面级结构
   - `model` 管字段静态语义

2. 试点没有引入额外 view 类型  
   仍然只使用：
   - `list`
   - `detail`

3. 试点没有把行为逻辑塞回元数据  
   当前动作元数据仍然只声明入口，不承载执行逻辑。

因此，人员中心试点不是平台主线的例外，而是平台主线的受控映射。

### 3.3 与标准 CRUD 主线的一致性

`organization-person-center-pilot-design.md` 与 `standard-crud-contract-design.md` 当前也是对齐的。

当前映射关系清晰：

- `list`
  - `GET /api/organization/persons`
- `detail`
  - `GET /api/organization/persons/{id}`
- `create`
  - `POST /api/organization/persons`
- `update`
  - `PUT /api/organization/persons/{id}`
- `enable-disable`
  - `POST /api/organization/persons/{id}/enable`
  - `POST /api/organization/persons/{id}/disable`
- `delete`
  - `DELETE /api/organization/persons/{id}` 或首轮显式策略后置
- `options`
  - 公司 / 部门 / 岗位选项查询

同时试点也正确区分了：

- list projection
- detail read model
- summaries

没有把它们重新混成一个 DTO。

### 3.4 与多语言和 authoring 布局主线的一致性

人员中心试点当前也没有偏离多语言与 authoring 布局规则。

试点文档当前默认遵守：

- `view` 文案来自各自 dicKey
- 前端消费仍按 flat key
- authoring 真源仍回到：
  - `modules/<ModuleId>/metadata/**`

这意味着：

- 试点没有自发引入第二套页面文案解析机制
- 也没有重新把文案写回页面代码或模块私有常量

## 4. 当前不一致点

本节只记录当前仍值得在设计层显式指出的点，不展开实施方案。

### 4.1 试点文档目前更像“缺口清单”，尚未完全收成“实施前冻结范围”

当前试点文档已经明确了：

- 主流程
- 当前缺口
- 后置项

但从实施前准备角度看，它还缺一层更硬的冻结表达：

- 哪些接口首轮必须补齐
- 哪些 metadata 文件首轮必须新增
- 哪些动作首轮允许降级

这不是设计冲突，但会影响后续计划编写时的稳定性。

### 4.2 删除策略仍然保留空间，尚未在试点层完全冻结

当前文档对删除的表述是合理的：

- 可以走受限删除
- 也可以首轮只声明策略，不开放入口

但这意味着在试点设计层，还需要一次更明确的冻结：

- 首轮到底是：
  - `delete = none`
  - 还是 `delete = conditional`

当前这件事尚未被明确收成“唯一结论”。

### 4.3 `summaryBlocks` 的承接语义已定方向，但对象级冻结还不够硬

当前文档已明确：

- `userLink`
- `authorizationSummary`

作为详情页摘要块。

但当前还没有进一步冻结：

- 首轮哪些状态必须真实返回
- 哪些状态允许只做只读降级

这不构成平台设计冲突，但会影响试点实施计划的边界清晰度。

### 4.4 当前 review 只能确认“试点设计自洽”，还不能确认“试点可直接实施”

这是本次 review 必须明确写清的边界：

- 当前能够确认：
  - 试点设计与平台主线一致
- 当前还不能确认：
  - 试点已经具备直接进入实施的冻结程度

也就是说，这次 review 的结论是“方向正确、边界一致”，不是“下一步已经可以跳过计划直接编码”。

## 5. 阻断项与非阻断项

### 5.1 当前阻断项

如果下一步目标是“进入组织模块试点实施计划准备”，当前阻断项只有一类：

**试点范围还需要一次更明确的冻结。**

当前至少还需在设计层再明确三件事：

1. 删除策略首轮最终取值
2. `summaryBlocks` 的首轮最低承接范围
3. 人员中心试点首轮必须新增的 metadata / API / 页面清单

这三个点不需要补新平台设计，但需要在试点层进一步收口。

### 5.2 当前非阻断项

以下内容当前都不应阻断试点继续推进：

- `tree view`
- `route metadata`
- 功能授权实现
- 对象授权实现
- 统一规则中心
- 批量导入导出
- 岗位管理页 / 组织树页同步改造

## 6. 建议的下一步设计动作

基于本次 review，当前最推荐的下一步不是直接写实施计划，而是先补一个更聚焦的试点冻结动作：

### 6.1 第一优先级

**对组织模块人员中心试点再做一次“范围冻结 review”。**

这次冻结 review 只需要聚焦三件事：

1. 首轮删除策略
2. 首轮 summaryBlocks 最低承接范围
3. 首轮必须实施的真实文件 / 接口 / 页面边界

### 6.2 第二优先级

当上述三点冻结后，再进入：

- 组织模块人员中心试点实施计划准备

而不是现在就直接跳到实施计划。

## 7. 结论

本次组织模块人员中心试点设计 review 的最终结论是：

1. 试点设计与平台主线整体一致
2. 当前没有发现必须推翻平台主线才能成立的冲突
3. 当前真正缺的不是新平台设计，而是试点范围的进一步冻结
4. 下一步最合理的是继续做一次试点范围冻结 review，再进入实施计划准备

## 8. AI-First 最小检查清单

### 入口在哪里

- `docs/designs/modules/organization-person-center-pilot-design.md`
- `docs/designs/platform/metadata-driven-backoffice-ui-design.md`
- `docs/designs/platform/standard-crud-contract-design.md`
- `docs/designs/platform/list-management-layout-demo-design.md`
- `docs/designs/platform/dic-localization-manifest-alignment-design.md`
- `docs/designs/user-visible-text-localization-foundation-design.md`
- `docs/designs/metadata-authoring-layout-unification-design.md`
- `docs/designs/platform/mainline-design-consistency-review.md`

### 边界在哪里

- 本文档只做组织模块人员中心试点的设计一致性 review
- 不进入实施计划
- 不进入代码实现
- 不重新讨论已经定下来的平台主线结论

### 如何验证

- 检查试点设计是否仍然落在 `nav / view / model` 三层边界内
- 检查试点是否与标准 CRUD 契约一致
- 检查试点是否仍然遵守统一多语言与 authoring 布局规则
- 检查当前是否还有必须先冻结的试点边界

### 失败信号是什么

- 试点为了成立，必须绕开 `nav / view / model` 三层边界
- 试点为了成立，必须引入第二套 CRUD 契约
- 试点为了成立，必须改用独立的页面文案解析规则
- 试点文档把权限、规则中心、tree view 拉回当前主线

### 如何回滚

- 若后续 review 发现本文结论有偏差，直接更新或回退本文即可
- 不影响现有平台主设计文档
- 不影响后续试点计划是否另行调整
