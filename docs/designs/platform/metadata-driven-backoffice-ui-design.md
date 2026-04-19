# 元数据驱动后台界面主线设计

## 1. 背景

当前仓库已经具备：

- `frontend/packages/ui` 的基础控件与结构块
- 模块级前端接入能力
- 组织模块的人员中心、详情页和绑定账号等前端骨架
- 模型元数据对字段布局、创建/编辑/查看静态语义的初步设计

但仍缺少一层“平台认可的后台界面 authoring 真源”：

- 左侧导航目前主要还是模块代码里的菜单声明
- 列表页、详情页内部结构仍主要写在页面代码中
- 字段级布局、页面级布局、导航级布局之间的边界还没有被一份平台级设计明确收口

如果不先把这层主线定下来，后续组织模块试点、列表模板、功能授权和后续 AI 辅助开发都会遇到同一类问题：

- 页面结构容易继续散落在 TSX 中各自实现
- 模块之间会重复定义列表、详情和导航编排
- 元数据边界不清，容易把行为逻辑重新塞进元数据
- 后续想补功能授权、对象授权和规则能力时，入口不稳定

本设计的目标，是先把“后台显示层元数据主线”收成最小可落地方案，作为组织模块“人员中心维护”试点的显示层基础。

## 2. 目标

- 明确后台显示层的三类元数据：`nav metadata`、`view metadata`、`model metadata`
- 明确三类元数据的边界，避免职责互相侵入
- 定义 `nav metadata` 的 V1 最小结构
- 定义 `view metadata` 的 V1 最小结构
- 定义 `list view` 的查询、列、排序、导出和标准维护动作承载方式
- 定义 `detail view` 作为单对象维护页时的最小结构
- 为未来功能授权、对象授权、route metadata 和规则中心预留稳定接入点，但不把它们拉入当前主线

## 3. 非目标

- 不在本设计中引入完整低代码页面解释器
- 不在本设计中实现功能授权、对象授权或统一规则中心
- 不在本设计中落地 `route metadata`
- 不在本设计中支持动态脚本、表达式驱动显隐、联动查询或页面生命周期脚本
- 不在本设计中纳入 `tree view`
- 不在本设计中实现列宽持久化、导出模板自定义、异步导出任务等高级能力

## 4. 范围

本设计涉及：

- `modules/<ModuleId>/metadata/navigation/*.nav.json`
- `modules/<ModuleId>/metadata/views/*.view.json`
- `modules/<ModuleId>/metadata/models/*.model.json` 的消费边界
- `frontend/modules/*/src/index.ts` 中现有菜单/路由声明与元数据的衔接方式
- Shell 左侧导航聚合和业务页面装配的元数据来源

本设计不涉及：

- 路由运行时装载机制重写
- 功能授权模块重构
- 对象授权模型落地
- 统一规则中心 DSL
- 复杂树界面、审批流页面、仪表盘页面

## 5. 设计约束

- 元数据只负责声明“显示什么、怎么排布、允许哪些静态入口”，不负责执行流程、脚本逻辑、回滚逻辑。
- Host / Shell 只负责聚合、装载和受控展示，不吸收模块私有业务规则。
- `model metadata` 继续负责字段级静态语义；`view metadata` 不得反向重写字段级布局细节。
- 当前路由仍以模块代码契约为真源；导航和页面视图元数据不得偷偷接管路由装配。
- 当前阶段必须优先保证小上下文、可验证、可回退，而不是一次性做完整后台元数据 DSL。

## 6. 总体方案

### 6.1 显示层三类元数据

后台显示层首轮统一拆成三类元数据：

- `nav metadata`
  - 负责左侧导航入口、层级、顺序、图标和路径
- `view metadata`
  - 负责页面内部静态编排
- `model metadata`
  - 负责字段级布局、`create/edit/view` 静态行为和字段展示语义

三者分工如下：

- 左侧是否有入口、入口挂在哪个父节点下：`nav metadata`
- 页面有哪些 section、哪些列表列、哪些查询项、哪些 action：`view metadata`
- section 内字段顺序、宽度、只读/可编辑/隐藏、详情是否显示：`model metadata`

### 6.2 首轮只保留两类 view

`view metadata` 首轮只支持：

- `list`
- `detail`

`tree view` 暂时后置，不纳入当前主线。原因不是否定树场景，而是当前试点首先要走通“人员中心维护”这条线，先用 `list/detail` 把显示层主线收口。

### 6.3 标准维护动作与 editorMode

标准维护动作只限定为：

- `create`
- `edit`
- `view`

这三类动作的默认承载方式由 `editorMode` 决定，而不是由每个按钮单独声明打开 `page / drawer / dialog`。

`editorMode` 首轮取值：

- `page`
- `drawer`
- `dialog`

专用动作不受 `editorMode` 影响，例如：

- `delete`
- `disable`
- `bindUser`
- `openAuthorization`

它们继续由代码 handler 决定执行方式。

### 6.4 首轮门禁、试点收敛与后置项

#### 必须先定

- `nav / view / model` 三层边界
- `nav metadata` 最小字段
- `list view` 最小字段
- `detail view` 最小字段
- `editorMode` 规则
- 导出、排序、默认查询条件等 list 的固定语义

#### 试点中收敛

- 具体对象的 section 划分
- 具体对象的 summary block 是否存在
- 哪些对象更适合 `page`，哪些更适合 `drawer/dialog`
- 列宽默认值与页面级展示细节

#### 后置项

- 功能授权实现
- 对象授权实现
- 统一规则中心
- route metadata
- tree view
- 条件表达式、动态联动、脚本

## 7. 关键模型

### 7.1 nav metadata

建议路径：

```text
modules/<ModuleId>/metadata/navigation/*.nav.json
```

V1 最小结构：

```json
{
  "schemaVersion": "1.0",
  "type": "nav",
  "moduleId": "Organization",
  "navigation": {
    "items": [
      {
        "key": "organization",
        "titleKey": "nav.organization",
        "icon": "building",
        "order": 100,
        "path": ""
      },
      {
        "key": "organization.personCenter",
        "titleKey": "nav.organization.personCenter",
        "icon": "users",
        "parentKey": "organization",
        "order": 110,
        "path": "/organization/persons"
      }
    ]
  }
}
```

V1 字段：

- `key`
- `titleKey`
- `icon`
- `parentKey`
- `order`
- `path`

V1 规则：

- `parentKey` 为空表示顶级节点
- `path` 为空表示容器/分组节点
- `path` 非空表示可点击节点
- `key` 全局唯一
- `parentKey` 必须指向已存在节点
- 禁止循环引用
- 首轮不支持“既有 path 又有子节点”的节点

后续若功能授权需要控制导航显示，默认直接使用 `key` 作为匹配标识。

### 7.2 list view

建议路径：

```text
modules/<ModuleId>/metadata/views/*.view.json
```

V1 最小结构：

```json
{
  "schemaVersion": "1.0",
  "type": "view",
  "moduleId": "Organization",
  "view": {
    "key": "organization.person.list",
    "dicKey": "PersonListView",
    "titleKey": "Title",
    "descriptionKey": "Description",
    "mode": "list",
    "model": "Person",
    "editorMode": "page",
    "detailViewKey": "organization.person.detail",
    "query": {
      "fields": [
        { "key": "keyword", "labelKey": "Query_Keyword", "source": "preset", "preset": "keyword", "operator": "like" },
        { "key": "companyId", "labelKey": "Query_Company", "source": "model", "field": "companyId", "operator": "=" },
        { "key": "departmentId", "labelKey": "Query_Department", "source": "model", "field": "departmentId", "operator": "=" },
        { "key": "employmentStatus", "labelKey": "Query_EmploymentStatus", "source": "model", "field": "employmentStatus", "operator": "=" }
      ],
      "defaults": [
        {
          "field": "employmentStatus",
          "operator": "=",
          "value": "enabled"
        }
      ]
    },
    "advancedFilter": {
      "enabled": true,
      "titleKey": "AdvancedFilter_Title",
      "sections": [
        {
          "key": "time",
          "titleKey": "AdvancedFilter_Time",
          "fields": [
            { "key": "createdTimeFrom", "labelKey": "Query_CreatedTimeFrom", "source": "model", "field": "createdTime", "operator": ">=" },
            { "key": "createdTimeTo", "labelKey": "Query_CreatedTimeTo", "source": "model", "field": "createdTime", "operator": "<=" }
          ]
        }
      ]
    },
    "list": {
      "columns": [
        { "key": "personNo", "labelKey": "Column_PersonNo", "source": "model", "field": "personNo", "width": 160, "sortable": true, "resizable": true },
        { "key": "name", "labelKey": "Column_Name", "source": "model", "field": "name", "sortable": true, "resizable": true },
        { "key": "companyName", "labelKey": "Column_Company", "source": "projection", "field": "companyName", "sortable": true, "resizable": true },
        { "key": "departmentName", "labelKey": "Column_Department", "source": "projection", "field": "departmentName", "sortable": true, "resizable": true },
        { "key": "employmentStatus", "labelKey": "Column_EmploymentStatus", "source": "model", "field": "employmentStatus", "width": 120, "sortable": true, "resizable": true },
        { "key": "isEnabled", "labelKey": "Column_IsEnabled", "source": "model", "field": "isEnabled", "width": 100, "sortable": true, "resizable": true }
      ],
      "defaultSort": [
        { "key": "personNo", "direction": "asc" }
      ],
      "export": {
        "enabled": true,
        "modes": ["current", "all"],
        "columnSource": "visibleColumns",
        "includeCurrentQuery": true,
        "includeCurrentSort": true,
        "format": "xlsx"
      }
    },
    "pageActions": [
      { "key": "create", "labelKey": "Action_Create", "selectionRequirement": "none" },
      { "key": "refresh", "labelKey": "Action_Refresh", "selectionRequirement": "none" }
    ],
    "rowActions": [
      { "key": "view", "labelKey": "Action_View" },
      { "key": "edit", "labelKey": "Action_Edit" },
      { "key": "disable", "labelKey": "Action_Disable", "confirmKey": "Confirm_Disable" },
      { "key": "delete", "labelKey": "Action_Delete", "confirmKey": "Confirm_Delete" }
    ]
  }
}
```

#### view 文案字典

`list view` 与 `detail view` 首轮都建议显式声明：

- `dicKey`

`dicKey` 用于表达“当前 view 消费哪一个 dic 命名空间”。

`dic` 资源的识别、`dicKey` 的来源、authoring 扫描范围、校验与装载规则，遵循已有的字典/元数据基础设计，不在本文重复展开。本文只定义 `view metadata` 如何消费 dic。

规则：

- 一个 `view` 对应一个 `dic` 文件
- `titleKey / descriptionKey / labelKey / confirmKey / section.titleKey` 默认都视为当前 `view.dicKey` 下的局部 key
- 最终唯一定位由 `dicKey + 局部 key` 共同决定
- 局部 key 可以很短；若需要层级感，使用 `_`，不要使用 `.`
- 文本解析只按当前语言进行，不允许在同一界面同时展示多语言版本

最小解析规则：

- `view metadata` 中保留两段式声明：
  - `dicKey`
  - 局部 `titleKey / descriptionKey / labelKey / confirmKey / section.titleKey`
- 前端运行时继续复用现有 `t(key, fallback?)` 扁平 key 解析入口
- `dic` 文件装载进前端 `catalogs` 时，统一扁平化为：

```text
dicKey.localKey -> 文案
```

- 传给 `t()` 的最终 key 统一拼成：

```text
${dicKey}.${localKey}
```

例如：

- `dicKey = PersonListView`
- `localKey = Query_Keyword`
- 运行时最终解析为：`t("PersonListView.Query_Keyword")`
- 若当前语言为 `zhs`，则对应 `catalogs["zhs"]["PersonListView.Query_Keyword"] = "关键字"`

装载约束：

- `dic` 文件在进入前端运行时前，先按 `dicKey.localKey` 扁平化
- 前端 `LocalizationProvider` 不负责理解 `dicKey` 与局部 key 的嵌套结构
- `t()` 只消费扁平后的最终 key，不额外新增第二套页面文案查询入口

约束：

- `dicKey` 自身不再使用 `.`
- 局部 key 自身不再使用 `.`
- `.` 仅保留给运行时最终 flat key 拼接
- `_` 仅用于局部 key 内部表达层级感

#### query

V1 支持：

- `fields`
- `defaults`

默认查询条件规则：

- 首轮支持以下操作符：
  - `=`
  - `like`
  - `>`
  - `>=`
  - `<`
  - `<=`
  - `in`
- 只允许静态值
- 仅作用于首次进入页面和点击重置
- `defaults.field` 必须已经声明在 `query.fields` 或 `advancedFilter.sections.fields` 中

`query.fields` 首轮不再直接使用裸字符串，而是使用轻量对象，最小字段为：

- `key`
- `labelKey`
- `source`
- `operator`

其中：

- `source = model`
  - 表示该查询项直接对应模型字段，字段控件类型、静态数据源等优先从 `model metadata` 推导
- `source = preset`
  - 表示该查询项不是模型普通字段，而是平台或模块预定义查询项
- `field`
  - 当 `source = model` 时，用于显式声明当前查询项绑定的模型字段
- `preset`
  - 当 `source = preset` 时，用于声明当前查询项绑定的预置查询项

当前首轮只允许一个预置查询项：

- `preset = keyword`

它用于承接“关键字”这类跨多个字段的通用查询输入。

随着列表筛选结构收口，首轮额外允许以下预置查询项：

- `preset = bindingStatus`
- `preset = summaryStatus`
- `preset = dateRange`

它们分别用于承接：

- 账号绑定状态
- 授权摘要状态
- 时间区间类查询

规则：

- `query.fields` 只用于声明轻筛选条中真正展示的查询项顺序
- 轻筛选条通常只放 2 到 4 个高频查询项
- 每个字段必须显式声明且只能声明一个 `operator`
- `operator` 只允许使用当前首轮支持的操作符集合
- 每个字段都必须显式声明 `labelKey`
- 如果 `query.fields` 已足够覆盖页面筛选需求，则不应再强行声明高级筛选入口

条件 label 的多语言规则：

- 查询条件 label 只按当前语言解析和显示，不允许在同一界面同时展示多语言版本
- 条件 label 由 `view metadata` 中的 `labelKey` 显式定义
- `source = model` 时，`model metadata` 继续提供字段静态语义，但不再承担最终显示 label 来源
- `source = preset` 时，预置查询项定义继续提供查询能力语义，但不再承担最终显示 label 来源

当前不允许：

- 任意 query 字段自己在 `view metadata` 中重复定义完整控件 schema
- 任意表达式型 query 字段
- 未声明来源的 query 字段

#### advancedFilter

`list view` 首轮允许声明可选的高级筛选能力块：

- `advancedFilter.enabled`
- `advancedFilter.titleKey`
- `advancedFilter.sections`

规则：

- 只有当 `advancedFilter.enabled = true` 时，页面才显示“高级筛选”入口
- 若未声明 `advancedFilter`，或显式声明 `enabled = false`，则默认不显示高级筛选按钮，也不装配右侧筛选抽屉
- `advancedFilter.titleKey` 用于抽屉标题或等价的当前语言文案
- `advancedFilter.sections` 用于声明抽屉中的筛选分区与字段归属
- 每个 `section` 首轮最小字段为：
  - `key`
  - `titleKey`
  - `fields`
- `section.fields` 直接定义高级筛选抽屉自身显示的查询项，不再复用 `query.fields` 引用
- `section.fields` 中每个字段的最小结构与 `query.fields` 保持一致：
  - `key`
  - `labelKey`
  - `source`
  - `operator`
- 若使用 `preset`，则仍需声明 `preset`
- 每个高级筛选字段必须显式声明且只能声明一个 `operator`
- 当前阶段高级筛选仍然不引入独立 DSL，但抽屉字段由 `advancedFilter.sections.fields` 自身声明
- 若当前查询项已经足够覆盖页面需求，不应为了保持界面对称而强行显示高级筛选入口

高级筛选条件 label 的多语言规则与轻筛选一致：

- 字段 label 只按当前语言显示
- 字段 label 由 `section.fields[].labelKey` 显式定义
- `source = model` 的字段语义仍来自 `model metadata`
- `source = preset` 的字段语义仍来自预置查询项定义
- `section.titleKey` 负责 section 标题的多语言，不承担字段级 label 定义

#### 页面头部文案

`list view` 与 `detail view` 首轮都允许声明页面头部文本键：

- `dicKey`
- `titleKey`
- `descriptionKey`（可选）

规则：

- `dicKey` 用于指定当前 view 对应的 dic 文件
- `titleKey` 用于页面标题
- `descriptionKey` 用于标题下的简短页面说明，可为空
- 页面说明不是字段 schema，也不是业务脚本说明，应保持简短且稳定
- 文本解析只按当前语言进行，不允许在同一界面同时显示多语言版本
- 当前阶段默认按系统或平台默认语言解析；后续可接用户账号默认语言或登录时所选语言
- 装载到前端语言缓存后，标题与说明都按 `${dicKey}.${localKey}` 对应的扁平 key 解析

#### list.columns

每列首轮支持：

- `key`
- `labelKey`
- `source`
- `field`
- `width`
- `sortable`
- `resizable`

列宽规则：

- 显式 `width` 优先
- 未指定宽度的列平均分配剩余空间
- 空间不足时横向滚动
- 超长文本默认省略显示
- 系统保留列默认不可拖动
- 若未写 `resizable`，默认 `true`
- 首轮只支持运行时拖动调整，不纳入列宽持久化

排序规则：

- `sortable` 控制该列是否可点击排序
- `defaultSort` 控制默认排序
- 首轮只支持一个最终生效排序字段；若 `defaultSort` 使用数组，仅首项参与初始排序

`columns[].key` 首轮必须显式声明来源，避免模型字段与投影字段混淆：

- `source = model`
  - 列 key 对应模型字段
- `source = projection`
  - 列 key 对应列表 DTO 或投影字段，例如 `companyName`、`departmentName`

规则：

- 每列都必须显式声明 `labelKey`
- `source = model` 时，可消费 `model metadata` 中已存在的字段显示语义
- `source = projection` 时，字段由列表接口直接提供，不要求必须回溯到模型字段
- `field` 用于显式声明列绑定的模型字段或 projection 字段
- 排序与导出映射以列声明为准，不允许页面代码自行猜测列 key 是否来自投影

列标题的多语言规则：

- 列标题只按当前语言显示
- 列标题由 `columns[].labelKey` 显式定义
- `model metadata` 与 projection 契约继续提供字段语义，但不承担列头最终显示文案来源

#### list.export

导出作为列表内建能力，不进入 `pageActions`。

V1 固定支持两种模式：

- `current`
- `all`

语义固定：

- `current`：若当前列表实现启用了行选择且已有选中行，则导出选中行；否则导出当前页
- `all`：导出当前查询条件下的全部结果
- 导出列默认等于当前列表显示列
- 导出顺序等于列表列顺序
- 导出按钮位置固定为表格左下角
- 分页位置固定为表格右下角
- 默认导出格式固定为 `xlsx`

### 7.3 detail view

`detail view` 的定位是“单对象维护页”，不是所有对象都必须有的页面类型。

它只在对象采用 `editorMode = page` 时成为标准维护动作的主承载方式。

当 `editorMode = page` 时，来源 view 必须显式声明：

- `detailViewKey`

它用于把标准维护动作从当前 view 解析到目标 detail view，而不是依赖运行时猜测。

V1 额外规则：

- `editorMode = page` 时，`detailViewKey` 必填
- `editorMode = drawer/dialog` 时，不要求声明 `detailViewKey`
- `detailViewKey` 必须指向同模块内已存在的 `detail view`
- 当前阶段仍由模块代码路由负责真正的 path 装配；`detailViewKey` 只负责 view 间寻址，不负责取代 route metadata

V1 最小结构：

```json
{
  "schemaVersion": "1.0",
  "type": "view",
  "moduleId": "Organization",
  "view": {
    "key": "organization.person.detail",
    "dicKey": "PersonDetailView",
    "titleKey": "Title",
    "descriptionKey": "Description",
    "mode": "detail",
    "model": "Person",
    "sections": ["basic", "organization"],
    "summaryBlocks": ["userLink", "authorizationSummary"],
    "pageActions": [
      { "key": "edit", "labelKey": "Action_Edit" },
      { "key": "disable", "labelKey": "Action_Disable", "confirmKey": "Confirm_Disable" },
      { "key": "delete", "labelKey": "Action_Delete", "confirmKey": "Confirm_Delete" }
    ]
  }
}
```

V1 规则：

- `sections` 必有
- `summaryBlocks` 可选
- `pageActions` 可为空但通常存在
- `titleKey` 用于详情页标题
- `descriptionKey` 可用于详情页说明；若无实际必要，可不声明

detail view 不是“三块强制全有”，而是“有统一骨架，具体块按对象配置”。

### 7.4 action metadata

当前 action 只保留最小字段：

- `key`
- `labelKey`
- `selectionRequirement`
  - 仅 `pageActions` 使用
- `confirmKey`

当前不做：

- `featureKey`
- `when`
- `visible/enabled` 元数据表达式
- handler 名
- 脚本
- API URL

标准维护动作和专用动作都只声明入口，不在元数据中定义执行逻辑。

action 文案规则：

- `labelKey` 与 `confirmKey` 默认都使用当前 `view.dicKey` 下的局部 key
- action 文案只按当前语言显示
- 当前阶段不要求 action 文案回退到模型字段或预置定义

### 7.5 model / view / nav 的边界

- `nav metadata`
  - 控左侧入口、层级、顺序、图标、路径
- `view metadata`
  - 控页面内部结构、列表列、查询区、页面动作、标准维护动作承载方式
- `model metadata`
  - 控字段在 `create/edit/view` 中的静态行为，以及字段在 section 中的位置和宽度

具体来说：

- section 内字段顺序、宽度：`model metadata`
- 页面有哪些 section、哪些 summary block：`view metadata`
- 查询项若 `source = model`，其字段级静态语义由 `model metadata` 提供；若 `source = preset`，则由平台预置查询项定义提供
- 列若 `source = model`，可消费 `model metadata`；若 `source = projection`，则以列表 DTO / projection 契约为准
- 左侧是否显示入口：`nav metadata`

## 8. 核心流程

### 8.1 左侧导航装配

1. Shell 装载模块运行时插件。
2. 模块提供现有前端代码契约与导航元数据。
3. 导航装配层读取 `nav metadata`，按 `parentKey + order` 构建左侧导航树。
4. 叶子节点使用 `path` 跳转到当前模块已注册的代码路由。

### 8.2 列表页装配

1. 页面装配层读取 `list view metadata`。
2. 根据 `query.fields` 渲染查询区。
3. 首次进入页面时应用 `query.defaults`。
4. 根据 `list.columns` 渲染表格列。
5. 根据 `defaultSort` 应用默认排序。
6. 表格底部左侧装配导出入口，右侧装配分页。

### 8.3 标准维护动作承载

1. 页面读取 `editorMode`。
2. 当点击 `create/edit/view` 时：
   - `editorMode = page`：根据 `detailViewKey` 进入目标 detail view
   - `editorMode = drawer`：打开抽屉
   - `editorMode = dialog`：打开弹窗
3. 当点击专用动作时，由代码 handler 决定执行或打开方式。

### 8.4 detail view 装配

1. 页面读取 `detail view metadata`。
2. 按 `sections` 决定详情维护页显示哪些 section。
3. section 内字段布局继续消费 `model metadata`。
4. 若存在 `summaryBlocks`，则在详情页固定摘要区域展示只读块。

## 9. 接口与集成点

### 9.1 与当前前端模块契约的关系

当前仍沿用模块代码中的路由声明和菜单/权限声明结构。

首轮关系为：

- `nav metadata` 作为导航 authoring 真源
- `routes` 仍由模块代码契约声明
- 页面路由挂载后，由页面代码选择并消费对应 `view metadata`

### 9.2 与 Shell 的关系

- Shell 继续只负责聚合、装载和展示
- Shell 不直接实现模块私有业务规则
- Shell 不吸收页面级字段布局细节

### 9.3 与 model metadata 的关系

- `view metadata` 只决定页面级结构
- 字段级布局和 `create/edit/view` 静态语义继续由 `model metadata` 承担
- `view metadata` 不得覆盖字段级 width/section/mode 等细节
- `query.fields` 若使用模型字段，应复用 `model metadata` 的字段级静态语义，而不是在 `view metadata` 中重新描述一遍
- `columns[].source = projection` 时，显示与导出语义以列表接口 projection 契约为准

### 9.4 route metadata 的定位

当前还没有 `route metadata`，也不纳入首轮主线。

当前规则：

- 路由继续由模块代码契约声明
- `nav metadata` 先直接使用 `path`
- 后续如有必要，再单独设计 `route metadata`

## 10. 数据与状态

- `nav metadata`、`view metadata`、`model metadata` 都属于显示层或 authoring 层静态输入
- 导航层不缓存业务规则结果
- 页面状态仍由代码控制，不进入元数据
- 当前不通过列表数据预计算按钮能力状态
- 当前不通过元数据表达式定义动态显隐和联动

## 11. 错误处理与风险

- 若 `nav metadata` 的 `parentKey` 指向不存在节点，应在装配时直接报错
- 若 `path` 指向未注册 route，应在装配或验证阶段直接失败，不静默忽略
- 若 `view metadata` 的 `query.defaults.field` 既不在 `query.fields` 中，也不在 `advancedFilter.sections.fields` 中，应直接校验失败
- 若 `editorMode = page` 但未声明 `detailViewKey`，应在校验阶段直接失败
- 若 `detailViewKey` 指向不存在的 detail view，应在校验或装配阶段直接失败
- 若 `list.export` 和 `pageActions` 重复表达同一能力，应视为元数据设计错误
- 若 `view metadata` 开始承载脚本、表达式、API URL 或行为逻辑，说明边界失守
- 若后续不得不在 Shell 中写大量页面特判，说明当前元数据边界设计不足

## 12. 验证方案

- 元数据文件结构通过 JSON schema 或等价静态校验
- `nav metadata` 能正确生成组织模块左侧导航入口
- `list view metadata` 能驱动人员中心列表页生成查询区、列、排序和导出入口
- `detail view metadata` 能驱动人员详情页按 section 和 summary block 装配
- `model metadata` 与 `view metadata` 的边界在实现中保持清晰，不出现重复表达
- 当前实现不要求功能授权、对象授权和规则中心参与验证

## 13. 未决问题

- `tree view` 的最小结构应何时纳入主线
- `route metadata` 应以何种形式演进为 authoring 真源
- 列宽拖动后的持久化是否需要进入后续阶段
- 导出是否需要异步任务、导出模板和格式扩展
- 标准维护动作与复杂对象多步编辑流程的关系如何收口

## 14. 实施拆分建议

1. 先固化本设计中的 `nav/view/model` 边界和 V1 结构
2. 再补 `standard-crud-contract-design.md`，确保列表和详情接口能支撑本设计
3. 再补 `organization-person-center-pilot-design.md`，把组织模块试点映射到本设计
4. 做一次一致性 review，确认显示层主线、CRUD 主线和试点主线没有互相打架
5. review 通过后，再进入后续实现计划

## 15. AI-First 最小检查清单

- 入口在哪里：
  - `modules/<ModuleId>/metadata/navigation/*.nav.json`
  - `modules/<ModuleId>/metadata/views/*.view.json`
  - `modules/<ModuleId>/metadata/models/*.model.json`
- 边界在哪里：
  - `nav` 只管左侧入口
  - `view` 只管页面内部静态编排和标准维护动作承载方式
  - `model` 只管字段级静态语义
  - 路由、权限、规则、脚本不进入本设计
- 如何验证：
  - 元数据结构校验
  - 人员中心列表页和详情页装配验证
  - 导航入口与页面结构 smoke 验证
- 失败信号是什么：
  - 元数据开始承载脚本、表达式或行为逻辑
  - Shell 被迫吸收模块私有页面特判
  - `nav/view/model` 三层出现明显重复表达
- 如何回滚：
  - 移除或停用新增的 `nav/view` 元数据消费入口
  - 保留当前模块代码中的菜单、路由和页面实现
  - 逐模块回退到纯代码声明，不影响既有路由和页面骨架
