# 标准 CRUD 契约设计

## 1. 背景

当前仓库已经开始讨论并落地“元数据驱动后台界面主线”，其中已经明确：

- `nav metadata` 负责左侧导航 authoring
- `view metadata` 负责列表页、详情页等页面级静态编排
- `model metadata` 负责字段级布局与 `create/edit/view` 静态语义

但如果没有一套稳定的 CRUD 契约，这条显示层主线仍然无法真正落地：

- 列表页无法知道查询参数、分页、排序和 projection 字段的稳定口径
- 详情页无法知道详情读取模型与只读摘要块如何承载
- `editorMode = page` 虽然能指向 `detailViewKey`，但缺少与详情读取模型的统一关系
- 列表中的 `projection` 字段、预置查询项、导出列和默认排序都会退回到页面代码里各自处理
- 删除策略、启停策略和错误语义若不统一，前端元数据与后端接口很快就会重新分叉

因此需要一份平台级 CRUD 契约设计，把标准后台业务模块在列表、详情、创建、更新、启停、删除和选项查询上的最小口径收紧下来，作为后续试点模块和页面元数据的共同基础。

## 2. 目标

- 定义标准后台业务模块的 7 类基础契约：
  - `list`
  - `detail`
  - `create`
  - `update`
  - `enable-disable`
  - `delete`
  - `options`
- 定义列表查询参数、分页、排序和 projection 读取模型的统一口径
- 定义详情读取模型与只读摘要块的最小承载方式
- 定义创建、更新、启停和删除的统一语义边界
- 定义删除策略分档，避免模块对删除能力各自随意表述
- 定义选项查询的最小返回结构，支撑 `view metadata` 和 `model metadata` 的消费
- 定义错误语义与最小返回模型，降低前后端各自猜测的空间
- 为导出和后续权限/规则扩展提供稳定承接点，但不在本设计中一次性做满

## 3. 非目标

- 不在本设计中实现功能授权或对象授权
- 不在本设计中实现统一规则中心 DSL
- 不在本设计中强推现有所有模块立刻重写接口
- 不在本设计中纳入批量导入、复杂导出任务、审批流、版本发布流
- 不在本设计中把所有业务模块抽象成一个完全相同的超大通用 API
- 不在本设计中设计 route metadata 或页面脚本能力

## 4. 范围

本设计涉及：

- 标准后台业务模块的读写接口口径
- 与 `view metadata` 对应的列表 projection、详情读取、查询项和导出承接
- 与 `model metadata` 对应的字段级输入与详情显示的承接边界
- 组织模块“人员中心维护”试点需要的最小接口类别

本设计不涉及：

- 权限体系实现
- 统一规则中心实现
- 复杂树结构接口标准
- 批量导入导出任务中心
- 复杂搜索 DSL

## 5. 设计约束

- CRUD 契约必须首先服务于页面模式与对象维护主线，不做脱离页面消费场景的空转抽象。
- `list`、`detail`、`options` 都属于读取模型；它们可以彼此不同，不要求共用同一 DTO。
- `projection` 字段允许脱离模型原始字段存在，但必须有稳定命名和稳定来源。
- 创建、更新、启停、删除属于不同行为，不允许全部塞进一个“大更新接口”。
- 删除能力必须显式声明策略，不允许模块在实现期临时决定“到底能不能删”。
- 当前阶段允许现有模块暂不完全符合本标准，但后续新增或重做页面的模块应尽量向本标准靠拢。

## 6. 总体方案

### 6.1 标准接口分类

首轮统一把标准后台业务模块的基础契约划分为 7 类：

- `list`
  - 承接列表页、分页、排序、projection、导出列来源
- `detail`
  - 承接详情维护页、drawer/dialog 编辑态、只读摘要块
- `create`
  - 创建新对象
- `update`
  - 更新对象基础信息
- `enable-disable`
  - 显式状态切换
- `delete`
  - 删除对象或声明不支持删除
- `options`
  - 提供下拉选项、引用选项和轻量候选项

### 6.2 读模型与写模型分离

读模型按场景拆分：

- `ListProjection`
  - 用于列表页
- `DetailReadModel`
  - 用于详情页或编辑态加载
- `OptionItem`
  - 用于下拉和候选项

写模型按行为拆分：

- `CreateRequest`
- `UpdateRequest`
- `EnableRequest / DisableRequest` 或显式 enable/disable 空体请求
- `Delete`
  - 默认不承载请求体

首轮不要求 `CreateRequest`、`UpdateRequest` 与任何读取模型完全同构。

### 6.3 必须先定、试点中收敛与后置项

#### 必须先定

- `list/detail/create/update/enable-disable/delete/options` 七类契约
- 列表分页与排序骨架
- `projection` 与模型字段的来源区分
- `detail` 与 summary blocks 的承载方式
- 删除策略分档
- 错误语义最小分类

#### 试点中收敛

- 不同资源的 projection 命名细节
- 写操作成功后返回 detail 还是最小对象的具体细节
- `preset keyword` 背后的字段覆盖范围
- options 查询的附加筛选参数细节

#### 后置项

- 批量 CRUD 契约
- 导入导出任务中心
- 复杂搜索 DSL
- 乐观锁和版本号协商
- 对象授权与规则中心统一接入

## 7. 关键模型

### 7.1 list 查询契约

标准 list 契约建议统一为：

- `GET /api/<resource>`

查询参数首轮统一最小集合：

- `page`
- `pageSize`
- `sortBy`
- `sortDirection`
- `view metadata.query.fields[].key`

其中：

- `page` 和 `pageSize` 承接分页
- `sortBy` 和 `sortDirection` 承接排序
- query 参数名直接对应 `view metadata.query.fields[].key`
- `source = model` 的 query 字段，由模型字段语义提供标签、控件类型和静态数据源
- `source = preset` 的 query 字段，由平台预置查询项定义提供语义
- 当前首轮只支持一个最终生效排序字段；若 `view metadata.list.defaultSort` 使用数组，只取首项作为初始排序来源

#### 预置查询项

当前首轮只定义一个预置查询项：

- `keyword`

语义：

- 它不是模型普通字段
- 它表示一类模块自定义的关键字查询输入
- 具体匹配哪些字段由资源自己的查询实现决定

约束：

- `view metadata.query.fields` 若声明 `source = preset` 且 `preset = keyword`
- 列表接口必须接受 `keyword` 参数
- 页面不再额外发明第二种关键字参数名

#### list 返回结构

标准返回骨架建议统一为：

```json
{
  "items": [],
  "total": 0,
  "page": 1,
  "pageSize": 20,
  "sortBy": "personNo",
  "sortDirection": "asc"
}
```

规则：

- `items` 为当前页 projection 列表
- `total` 为当前查询条件下的总数
- `page` / `pageSize` 回显当前分页结果
- `sortBy` / `sortDirection` 回显最终生效排序
- 若请求未显式传入排序参数，则服务端可按资源默认排序或 `view metadata.list.defaultSort` 首项生效后的结果回显

### 7.2 list projection 契约

`list.items` 中的每一项都是 `ListProjection`，用于承接列表页显示、排序与导出列来源。

首轮规则：

- projection 字段允许不是模型原始字段
- 只要 `view metadata.list.columns[].source = projection`
  - 该字段就由 list 接口直接提供
- 只要 `view metadata.list.columns[].source = model`
  - 该字段名应与模型字段保持稳定对应

例如：

- `personNo`、`name` 可直接对应模型字段
- `companyName`、`departmentName` 可作为 projection 字段

约束：

- 若某列被标记为 `sortable = true`
  - 列表接口必须支持使用该列 key 或与其稳定对应的 `sortBy` 值进行排序
- 导出若使用 `visibleColumns`
  - 则导出列来源必须与当前 projection 字段保持一致

### 7.3 detail 读取契约

标准 detail 契约建议统一为：

- `GET /api/<resource>/{id}`

`DetailReadModel` 用于承接：

- `editorMode = page` 的详情维护页
- `editorMode = drawer/dialog` 时的单对象加载
- 只读摘要块

建议结构口径：

- 基础对象字段
- 必要的显示值字段
- 可选的 `summaries`

示意：

```json
{
  "id": "00000000-0000-0000-0000-000000000001",
  "personNo": "P0001",
  "name": "张三",
  "companyId": "00000000-0000-0000-0000-000000000010",
  "companyName": "总部",
  "departmentId": "00000000-0000-0000-0000-000000000020",
  "departmentName": "人事部",
  "isEnabled": true,
  "summaries": {
    "userLink": {
      "status": "ready",
      "data": {}
    },
    "authorizationSummary": {
      "status": "dependencyUnavailable",
      "data": null
    }
  }
}
```

规则：

- `summaries` 的 key 应与 `detail view.summaryBlocks` 稳定对应
- 详情主字段与摘要块读取可以同接口返回，但摘要语义应与主字段语义区分
- 摘要块首轮建议统一采用 `{ status, data }` 结构
- `status` 首轮至少区分：
  - `ready`
  - `notConfigured`
  - `dependencyUnavailable`
- 若某摘要块不存在或未接入，不得强迫页面猜测为“无数据”还是“依赖暂不可用”
- 当 `status = ready` 时，`data` 才表示可消费摘要内容；其余状态下 `data` 可为 `null`

### 7.4 create / update 契约

标准写操作建议统一为：

- `POST /api/<resource>`
- `PUT /api/<resource>/{id}`

规则：

- `CreateRequest` / `UpdateRequest` 只提交当前资源允许写入的字段
- 不提交 `projection` 字段
- 不提交只读 summary block
- 不把启停、删除、绑定等专用行为混入通用更新

成功返回建议：

- 首轮推荐统一返回最新的 `DetailReadModel`

原因：

- 支撑 `editorMode = page`
- 支撑 `drawer/dialog` 保存后直接刷新当前对象状态
- 避免前端保存后再额外拼装显示值和摘要块

### 7.5 enable-disable 契约

启用与停用不应隐式塞进通用 `update`。

标准建议统一为：

- `POST /api/<resource>/{id}/enable`
- `POST /api/<resource>/{id}/disable`

规则：

- 这是显式状态切换行为
- 是否允许切换由资源自身规则决定
- 成功后首轮推荐返回最新 `DetailReadModel`

### 7.6 delete 契约

删除必须作为标准能力类别保留，但模块必须声明自己的删除策略。

首轮删除策略统一分为三档：

- `none`
  - 不支持删除
- `conditional`
  - 满足前置条件时允许删除
- `auditable`
  - 允许删除，但必须保留审计痕迹

建议规则：

- 若策略为 `none`
  - 不暴露删除 action
  - 不要求暴露删除接口
- 若策略为 `conditional` 或 `auditable`
  - 使用 `DELETE /api/<resource>/{id}`

删除成功响应首轮建议：

- `204 No Content`

删除失败时，必须能区分至少这三类情况：

- 目标不存在
- 当前状态不允许删除
- 存在引用或关联，不可删除

### 7.7 options 契约

标准 options 契约用于支撑：

- 下拉框
- 关联对象选择
- 轻量候选项

建议统一为：

- `GET /api/<resource>/options`

首轮返回结构建议为：

```json
[
  {
    "value": "00000000-0000-0000-0000-000000000001",
    "label": "总部"
  }
]
```

V1 最小字段：

- `value`
- `label`

允许按需追加轻量筛选参数，例如：

- `companyId`
- `departmentId`
- `keyword`

但不在当前设计中展开更复杂的 options DSL。

### 7.8 导出与 list 契约的关系

导出虽然不属于 CRUD 主干，但当前显示层主线已经把导出纳入 `list.export`。

因此本设计补充一条约束：

- 若资源实现 `export current / export all`
- 其列来源、查询条件和排序语义必须直接继承 list 契约

也就是说：

- `current`
  - 若当前列表支持行选择且已有选中行，则使用选中行对应的 projection 列
  - 若当前列表未启用行选择或当前没有选中行，则回退为当前页结果
- `all`
  - 使用当前查询条件和当前排序下的完整结果

当前阶段只要求：

- 默认导出格式为 `xlsx`

不要求现在统一导出任务接口。

### 7.9 错误语义

标准错误语义首轮至少分为：

- 字段校验失败
- 目标不存在
- 业务规则冲突
- 依赖暂不可用
- 权限不足（预留）
- 系统异常

建议错误返回最小结构：

```json
{
  "code": "Organization.Person.DeleteBlockedByUserLink",
  "message": "当前人员已绑定账号，不能删除。",
  "reasonKey": "organization.person.deleteBlockedByUserLink",
  "fieldErrors": []
}
```

建议语义：

- `code`
  - 稳定错误码
- `message`
  - 当前返回的人类可读消息
- `reasonKey`
  - 供前端统一提示、多语言或 action 禁用提示复用
- `fieldErrors`
  - 字段级校验错误数组

建议最小映射：

- 字段校验失败：`400`
- 目标不存在：`404`
- 业务规则冲突：`409`
- 依赖暂不可用：`503`
- 权限不足：`403`
- 系统异常：`500`

## 8. 核心流程

### 8.1 列表页读取

1. 页面读取 `view metadata.query` 与 `view metadata.list`。
2. 生成 `page / pageSize / sortBy / sortDirection` 与 query 字段。
3. 调用 list 接口。
4. 使用返回的 `items / total / page / pageSize / sortBy / sortDirection` 装配列表。

### 8.2 标准维护动作读取

1. 当 `editorMode = page` 时，由来源 view 解析出 `detailViewKey`。
2. 页面或 handler 再按当前模块代码路由进入目标 detail 页面。
3. detail 页面调用 `GET /api/<resource>/{id}`。
4. 用 `DetailReadModel` 装配基础 section 与 summary block。

### 8.3 写操作

1. 页面提交 `CreateRequest` 或 `UpdateRequest`。
2. 服务端执行校验与业务规则检查。
3. 成功后返回最新 `DetailReadModel`。
4. 页面用返回结果刷新当前对象状态。

### 8.4 删除

1. 页面触发删除 action。
2. 若资源声明不支持删除，则不应暴露删除入口。
3. 若支持删除，则调用 `DELETE /api/<resource>/{id}`。
4. 服务端按资源删除策略与业务规则校验是否允许。
5. 成功返回 `204`，失败返回标准错误语义。

## 9. 接口与集成点

### 9.1 与 view metadata 的关系

- `view metadata.query.fields` 决定 list 契约要接收哪些查询参数
- `view metadata.list.columns` 决定 projection 字段如何被消费
- `view metadata.editorMode = page` 依赖 detail 契约
- `view metadata.detail.summaryBlocks` 依赖 detail 返回中的 `summaries`

### 9.2 与 model metadata 的关系

- `source = model` 的 query/column 字段可直接消费 `model metadata` 的字段静态语义
- `CreateRequest / UpdateRequest` 的写入字段边界优先由模型与应用服务共同决定
- `model metadata` 不决定 list projection 的存在与命名

### 9.3 与后续权限/规则能力的关系

- 当前不实现功能授权和对象授权
- 当前不实现统一规则中心
- 但错误语义和删除策略需要为后续这些能力保留稳定接入点

## 10. 数据与状态

- `ListProjection` 是列表页的唯一显示数据来源
- `DetailReadModel` 是单对象维护页、drawer、dialog 的推荐读取来源
- `OptionItem` 是选项控件的唯一最小来源
- 当前不在 list 返回中预计算按钮能力状态
- 当前不要求 list 和 detail 完全共用同一 DTO

## 11. 错误处理与风险

- 若模块同时把启停和删除塞进通用 `update`，说明契约边界失守
- 若 projection 字段命名不稳定，列表排序、导出和列渲染会重新退回页面特判
- 若 detail 返回无法区分主字段和 summary block，页面将无法正确做局部降级
- 若删除策略不在模块设计中显式声明，前端动作和后端实现会快速分叉
- 若 query preset 任意扩张，会重新变成复杂搜索 DSL

## 12. 验证方案

- 组织模块“人员中心维护”能够补出真实 list 接口
- list 接口能稳定返回 projection 字段，支撑 `companyName / departmentName` 这类列
- detail 接口能承接详情页和只读摘要块
- create / update / enable-disable 接口能按统一语义返回最新 detail 结果
- 删除策略能够在模块设计中被明确声明，而不是实现时临时决定
- 当前试点无需同时完成权限体系和统一规则中心

## 13. 未决问题

- projection 字段与排序字段是否始终一一同名，还是需要后续补 `sortKey`
- detail 返回中的 summary block 是否统一采用对象字典结构
- options 是否需要在后续支持树结构或更多附加字段
- 导出是否需要独立统一协议或异步任务模型
- 写操作返回最新 detail 模型是否需要对大对象做裁剪

## 14. 实施拆分建议

1. 先用本设计把 list / detail / delete / options 的统一口径定下来
2. 再把组织模块当前缺失的人员列表接口映射到本设计
3. 再在 `organization-person-center-pilot-design.md` 中明确：
   - 哪些接口首轮必须补齐
   - 哪些接口或策略显式后置
4. 一致性 review 通过后，再进入具体实现计划

## 15. AI-First 最小检查清单

- 入口在哪里：
  - `view metadata` 消费 `list/detail/options` 契约
  - 组织模块试点通过 `persons`、`companies`、`departments`、`positions` 等接口验证
- 边界在哪里：
  - CRUD 契约只负责读写模型、语义和错误口径
  - 权限、规则中心、route metadata 不进入本设计
- 如何验证：
  - 人员中心列表、详情、更新、启停、删除策略声明和选项查询可以按统一口径落地
- 失败信号是什么：
  - 页面继续靠页面代码自己猜 projection 字段、排序字段或错误语义
  - 删除策略没有被显式声明
  - list/detail/options 三类读取模型重新混成一个 DTO
- 如何回滚：
  - 回退到模块级接口文档和页面私有适配
  - 不影响第一份显示层元数据设计的主线
