# 组织模块人员中心试点设计

## 1. 背景

当前仓库已经具备：

- 平台级 Host / Shell / Kernel / 模块接入链路
- 组织模块首版后端模型、服务和部分 API
- 组织模块首版前端页面骨架与账号绑定联通
- 两份新的平台级设计：
  - `docs/designs/platform/metadata-driven-backoffice-ui-design.md`
  - `docs/designs/platform/standard-crud-contract-design.md`

但当前状态仍停留在“模块已经能接进来”，还没有真正验证：

- 显示层三类元数据 `nav / view / model` 是否足够承接一个标准后台业务页
- 标准 CRUD 契约是否足够支撑列表页、详情页、创建/编辑和导出
- 组织模块当前 API 与页面骨架距离平台标准还差哪些关键补项

因此需要一份试点设计，把“人员中心维护”作为首条真实验收线，用来验证平台标准是否可落地，并明确当前阶段哪些内容必须先补、哪些内容先后置。

## 2. 目标

- 将“人员中心维护”定义为首条标准后台业务模块试点主线。
- 用该主线验证 `nav metadata + view metadata + model metadata` 的消费链路。
- 用该主线验证 `list / detail / create / update / enable-disable / options` 的 CRUD 主干是否足够支撑真实模块，并确认 `delete` 首轮只保留为标准分类位置。
- 明确组织模块当前实现与平台标准之间的差距，并形成首轮补项清单。
- 将首轮范围收紧在“列表 -> 详情 -> 编辑 -> 停用 -> 只读摘要”闭环，不把删除、权限、规则中心、route metadata 一次性拉入。
- 将人员中心列表页作为“标准列表页模板能力”的首个真实试点，优先验证后续同类功能是否可以通过共享模板复用统一页面骨架。

## 3. 非目标

- 不在本设计中直接进入组织模块代码实现。
- 不在本设计中实现功能授权、对象授权或统一规则中心。
- 不在本设计中接管 `Identity Lite` 的账号创建、禁用、密码和登录策略。
- 不在本设计中接管 `Authorization` 的角色分配或权限编辑。
- 不在本设计中纳入 `tree view`、`route metadata`、批量导入导出、复杂人事流程。
- 不把组织模块扩成完整 HR 系统。

## 4. 范围

本设计涉及：

- `frontend/modules/organization/src/pages/PersonListPage.tsx`
- `frontend/modules/organization/src/services/organizationService.ts`
- `server/modules/Organization/Controllers/PersonsController.cs`
- `server/modules/Organization/Controllers/CompaniesController.cs`
- `server/modules/Organization/Controllers/DepartmentsController.cs`
- `server/modules/Organization/Controllers/PositionsController.cs`
- 组织模块后续拟新增的 `nav / view / model` 元数据与试点消费关系

本设计不涉及：

- 组织树页重做
- 岗位管理页重做
- 账号中心或授权中心页面改造
- 组织模块之外的 Shell 内部结构调整

## 5. 设计约束

- 试点首要目标是验证“标准后台业务模块主线”，不是尽快把组织模块做大。
- 组织模块当前已存在首版实现，本设计应优先识别“哪些地方要向平台标准收口”，而不是推翻已有实现重来。
- 当前仍以模块代码中的 route 声明为真源，不额外扩出 `route metadata`。
- 当前不实现权限和规则中心，但必须给后续接入预留稳定扩展位。
- 任何新增设计都要优先回答：AI 是否能在小上下文内理解入口、补齐缺口、验证结果和执行回滚。

### 5.1 必须先定

- 试点主验收线固定为“人员中心维护”。
- 首轮主流程固定为：
  - 列表
  - 详情
  - 编辑
  - 停用
  - 只读摘要
- `绑定账号` 和 `授权摘要` 只作为辅助验证线，不作为首轮主阻断链路。
- 人员中心默认采用：
  - `list view`
  - `editorMode = page`
  - `detail view`
- 当前不把 `tree view`、权限实现、规则中心和 route metadata 拉进主线。

### 5.2 试点中收敛

- 人员列表最终列集与查询项取舍
- 详情页 section 划分
- `summaryBlocks` 的具体展示样式
- 导出入口、停用动作与只读摘要承接的具体页面细节
- 人员列表页迁移到平台标准列表页模板后的最小插槽边界

### 5.3 后置项

- 功能授权和对象授权实现
- 统一规则中心
- 复杂导出任务
- 列宽持久化
- tree 模式与组织树统一改造

## 6. 当前现状

### 6.1 前端现状

`PersonListPage` 当前已经具备：

- 页面标题和说明
- 查询区骨架
- 列表容器位置
- 公司筛选加载
- 空态承载位

但当前仍是明显的骨架状态：

- `rows` 固定为空
- 没有真实人员列表接口接入
- 没有分页
- 没有排序
- 没有导出
- 没有真实 `rowActions`

这意味着它目前验证的是“页面位置先固定下来”，还没有验证“标准 list view 能否承接真实业务页”。

同时，当前列表页虽然已经大致遵循统一布局方向，但页面级骨架仍主要写在业务页 TSX 中，尚未沉淀为平台共享模板。因此，本试点后续实现路径应调整为：

- 先补平台标准列表页模板能力
- 再让 `PersonListPage` 迁移到该模板上
- 最后用人员中心验证模板能力能否承接真实业务页

### 6.2 前端服务现状

`organizationService.ts` 当前已经提供：

- 公司查询
- 部门查询
- 岗位查询
- 人员详情查询
- 账号搜索
- 绑定账号

当前缺失：

- 人员列表查询
- 人员创建
- 人员更新
- 人员启用 / 停用
- 统一的 `options` 口径

同时当前 DTO 仍偏首版实现口径：

- `getPersonDetail` 返回 `PersonDetailDto`
- `create/update` 的统一 detail 刷新语义尚未形成
- 授权摘要仍是 `string[]`，没有 `summary status` 结构

### 6.3 后端现状

当前组织模块 controller 现状：

- `companies`
  - 已有 `GET list / GET detail / POST / PUT`
- `departments`
  - 已有 `GET list / GET detail / POST / PUT`
- `positions`
  - 已有 `GET list / GET detail / POST / PUT`
- `persons`
  - 已有 `GET detail / POST / PUT`
  - 已有 `PUT user-link`
  - 已有 `GET user-options`
  - 缺少 `GET list`
  - 缺少 `enable / disable`

这说明组织模块已经有“详情维护 + 绑定账号”的一部分能力，但还没有补齐人员中心真正需要的标准 CRUD 主干。

## 7. 试点主流程

首轮主流程固定为：

1. 从左侧导航进入“人员中心”
2. 按关键字、公司、部门、在职状态筛选人员列表
3. 查看列表结果
4. 进入人员详情维护页
5. 编辑人员基础信息并保存
6. 执行停用
7. 在详情页查看账号绑定状态和授权摘要的只读状态化结果

其中：

- 第 1 到第 6 步是首轮主验收链路
- 第 7 步是辅助验证链路

原因：

- 它足够贴近真实后台业务模块
- 它能同时验证列表、详情、编辑和只读摘要
- 它又不会把首轮范围扩张到完整树治理和复杂规则中心

## 8. 与平台标准的映射

### 8.1 nav metadata 映射

人员中心应作为组织模块左侧主入口之一：

- `organization`
  - 容器节点
- `organization.personCenter`
  - `path = /organization/persons`

它用于验证：

- 左侧导航入口能否从代码声明逐步收口到 `nav metadata`
- 未来功能授权是否能直接用 `key` 接住导航显隐

### 8.2 view metadata 映射

人员中心列表页：

- `mode = list`
- `model = Person`
- `editorMode = page`
- `detailViewKey = organization.person.detail`

列表应至少具备：

- query：
  - `keyword`（preset）
  - `companyId`（model）
  - `departmentId`（model 或后续 option source）
  - `employmentStatus`（model）
- columns：
  - `personNo`
  - `name`
  - `companyName`
  - `departmentName`
  - `employmentStatus`
  - `isEnabled`
- pageActions：
  - `create`
  - `refresh`
- rowActions：
  - `view`
  - `edit`
  - `disable`
- export：
  - `current`
  - `all`

人员详情页：

- `mode = detail`
- `model = Person`
- `sections`
  - `basic`
  - `organization`
- `summaryBlocks`
  - `userLink`
  - `authorizationSummary`
- `pageActions`
  - `edit`
  - `disable`

### 8.3 model metadata 映射

`model metadata` 继续负责：

- 人员字段在 `create/edit/view` 中的静态行为
- section 内字段顺序和宽度
- 字段显示语义

这意味着：

- 详情页有哪些 section，由 `view metadata` 决定
- section 内字段如何排布、是否只读、是否编辑，由 `model metadata` 决定

### 8.4 CRUD 契约映射

人员中心首轮至少需要补齐以下契约：

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
  - 首轮冻结为 `none`
  - 仅保留标准分类位置与后续设计位置，不作为当前必须补齐契约
- `options`
  - 公司、部门、岗位的选项查询
  - 账号搜索作为跨模块选项能力单独保留

## 9. 当前缺口清单

### 9.1 P0：进入实现前必须明确

1. 人员列表接口缺失  
当前没有 `GET /api/organization/persons`，人员中心无法验证标准 list view。

2. 人员详情读取模型与 summary block 口径未完全收口  
当前 `PersonDetailDto.authorizationSummary` 仍是 `string[]`，还不能表达：
- `ready`
- `notConfigured`
- `dependencyUnavailable`

3. 标准维护动作与页面承载方式还未显式映射  
当前页面骨架虽已存在，但还没有正式转成：
- `editorMode = page`
- `detailViewKey`

4. `enable-disable` 语义尚未落地  
当前 persons 只有 `detail / create / update / user-link`，还不足以支撑标准维护动作。

### 9.2 P1：应随试点一起补齐

1. 人员列表 projection 口径  
需要稳定承接：
- `companyName`
- `departmentName`
- 未来可能的 `positionName`

2. 导出与 list 契约对齐  
需要确认：
- `current`
- `all`
- `xlsx`
- 列来源 = 当前显示列

3. `options` 查询口径  
公司、部门、岗位需要逐步从“模块私有查询函数”收口到平台标准 options 语义。

### 9.3 P2：首轮后置

1. 功能授权显隐
2. 对象授权
3. 统一规则中心
4. route metadata
5. tree view

## 10. 失败信号

出现以下现象时，应视为本次试点没有按预期验证平台主线：

- 为了做人员中心，不得不继续把列表结构、详情结构和动作语义全写死在页面代码里
- 为了支持列表列和查询区，不得不在页面代码里反复手写字段 schema，而不是消费 `view/model metadata`
- 为了支撑详情页，只能再发明第二套与 `detail view` 无关的页面骨架
- 为了接入人员中心，必须改 Shell 内部结构而不是走受控模块入口
- 为了做停用，不得不提前引入统一规则中心
- 绑定账号或授权摘要失败会反向拖垮人员主流程

## 11. 验证方案

### 11.1 设计验证

- `metadata-driven-backoffice-ui-design.md`
- `standard-crud-contract-design.md`
- 本文档

三份文档必须能互相咬合：

- `nav/view/model` 边界清楚
- CRUD 契约能支撑 `list/detail`
- 组织模块试点映射不越界

### 11.2 试点通过的最小标准

首轮至少要验证：

1. 人员中心可以通过 `nav metadata` 定义左侧入口
2. 人员列表和详情页可以通过 `view metadata` 描述页面结构
3. 人员字段可以通过 `model metadata` 描述 create/edit/view 静态行为
4. 人员 CRUD 主干可以按标准契约收口
5. 账号绑定和授权摘要可以作为只读或次级能力接入，而不拖垮主流程

### 11.3 当前阶段不作为阻断项

- 功能授权是否真正控制按钮显隐
- 对象授权是否落地
- 统一规则中心是否落地
- 组织树是否一起完成标准化

## 12. 回滚思路

- 若 `nav/view/model` 主线不稳定：
  - 回退到当前模块代码中的菜单、路由和页面骨架
  - 不影响组织模块既有前后端实现
- 若 CRUD 标准过度抽象：
  - 回退到模块级接口文档与局部适配
  - 不回退第一份显示层元数据主线
- 若人员中心范围被拉大：
  - 回退到“列表 + 详情 + 编辑 + 停用”闭环
  - 绑定账号和授权摘要仅保留只读降级展示
- 删除继续保持 `none`
- 首轮只保留停用

## 13. 实施拆分建议

1. 先稳定两份平台级设计
2. 再按本设计补组织模块试点缺口清单
3. 先补人员列表契约与详情读取模型
4. 再补 `view metadata` 与现有页面骨架的映射
5. 最后回填试点设计与冻结结论，不重新打开删除策略

## 14. AI-First 最小检查清单

### 入口在哪里

- 左侧入口：组织模块导航项
- 页面入口：`PersonListPage` / `PersonDetailPage`
- 后端入口：`PersonsController`
- 元数据入口：
  - `modules/Organization/metadata/navigation/*.nav.json`
  - `modules/Organization/metadata/views/*.view.json`
  - `modules/Organization/metadata/models/*.model.json`

### 边界在哪里

- 组织模块只拥有人员与组织主数据，不拥有账号和授权角色主数据
- `nav/view/model` 只承载静态 authoring，不承载脚本和规则逻辑
- CRUD 契约只负责读写模型和错误语义，不承载权限实现与规则中心 DSL

### 如何验证

- 用人员中心列表与详情页验证显示层元数据主线
- 用 persons list/detail/create/update/enable-disable/options 验证 CRUD 主线
- 用账号绑定状态和授权摘要验证跨模块只读集成

### 失败信号是什么

- 页面结构继续散落在 TSX 中，没有形成可复用元数据入口
- persons 相关接口继续停留在模块私有随意口径，无法支撑 list/detail 标准模式
- 绑定账号和授权摘要反向拖垮人员主流程
- 为了试点不得不引入权限、规则中心或 route metadata 才能前进

### 如何回滚

- 回退到当前模块代码路由、菜单和页面骨架
- 停用新增元数据消费层
- 保留组织模块现有后端接口与前端详情骨架，不影响既有组织模块最小闭环
