# 标准 CRUD 元数据回收设计

## 1. 背景

人员中心后台 CRUD Pro 样板已经完成固化，当前页面可以通过 Shell 标准 CRUD Host 承接列表、详情抽屉、创建/编辑抽屉、删除、停用、lookup 和 summary 展示。但本轮复盘确认：人员中心目前仍是“标准 Host + 模块前端注册代码驱动”的中间态。

典型偏差集中在 `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`：

- 查询块由 `renderFilters` 和模块私有 React 组件声明。
- 列表列由 `buildColumns` 声明。
- 表单字段、分组、只读、必填、默认值、lookup 配置由 `fields` 和 `formSections` 声明。
- 页面动作、行动作、详情动作主要由 `listActions` 和 `detailActions` 声明。

这与早期“元数据声明标准功能，代码内核负责执行和校验”的原则不完全一致。若继续复制当前形态，下一批标准 CRUD 资源仍需要复制大量 TSX 注册代码，不利于 Codex 在小上下文内理解、修改、验证和回滚。

当前仓库已经存在 `nav / view / model / dic / enum / settings` 等元数据类型。人员中心也已有：

- `modules/Organization/metadata/navigation/Organization.nav.json`
- `modules/Organization/metadata/views/PersonList.view.json`
- `modules/Organization/metadata/views/PersonDetail.view.json`
- `modules/Organization/metadata/models/Person.model.json`
- `modules/Organization/metadata/dictionaries/*.dic.json`

因此本设计不新增一套低代码页面体系，而是在现有 `nav / view / model` 边界内回收标准 CRUD 的可声明部分。

## 2. 目标

- 明确人员中心标准 CRUD 哪些能力应迁回 metadata，哪些能力必须继续留在代码内核。
- 固化 `nav / view / model / dic / enum` 在标准 CRUD V1 中的职责边界。
- 定义字段控件类型、字段数据源、表单模式、查询默认值、列表列和标准动作的 V1 metadata 结构，并尽量复用当前 `uiType / form / view / dataSource` 模型字段协议。
- 明确多语言文案继续由 `dic metadata + runtime localization manifest` 承接，标准 CRUD metadata 只引用文案 key，不内联显示文本。
- 明确 `EID / ENAME / ECODE` 平台协议字段继续沿用当前内核固定映射，不新增业务 metadata 字段映射机制。
- 让人员中心成为“标准 CRUD metadata 样板”的第一批回收对象，而不是继续扩散 TSX 注册代码样板。
- 为后续实施计划提供入口、边界、验证方式、失败信号和回滚思路。

## 3. 非目标

- 不在本设计中直接修改代码或 metadata 文件。
- 不新增独立的 `button/action metadata` 类型。
- 不新增独立的 `control metadata` 类型。
- 不实现完整低代码页面解释器、表达式引擎、脚本化显隐或页面生命周期 DSL。
- 不把 lookup 加载、动作执行、保存流程、抽屉状态机、回滚流程下沉到 metadata。
- 不重做后端标准 CRUD 内核、持久化投影或 EID 值协议。
- 不新增标准 CRUD 专属 i18n、翻译表或前端文案覆盖层。
- 不要求本轮同时改造 ConfigCenter、Authorization、Audit 等其他模块。
- 不处理对象级权限、按行动态按钮状态、复杂规则中心或审批流页面。

## 4. 范围

本设计允许后续实施涉及：

- `modules/Organization/metadata/models/Person.model.json`
  - 补齐字段控件、表单模式、数据源、默认值、必填、只读/隐藏等字段级静态语义；优先复用当前 `uiType` 字段，不新增平行 UI 对象分区。
- `modules/Organization/metadata/views/PersonList.view.json`
  - 补齐查询条件、默认查询条件、默认排序、列表列、页面动作、行动作和导出声明。
- `modules/Organization/metadata/views/PersonDetail.view.json`
  - 补齐详情使用的 section、summary block、详情页动作。
- `modules/Organization/metadata/dictionaries/*.dic.json`
  - 补齐字段、查询、列、按钮、确认提示等 labelKey。
- `frontend/packages/shell/src/metadata/`
  - 扩展标准 CRUD view/model schema resolver。
- `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
  - 改为优先消费 metadata schema 渲染查询块、列表列和动作。
- `frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx`
  - 改为优先消费 metadata schema 渲染详情表单和动作。
- `frontend/packages/shell/src/standard-crud/`
  - 承接字段控件 registry、lookup runtime、动作 runtime、表单 schema 合成。
- `frontend/packages/sdk/src/standardCrud.ts`
  - 如有必要，收敛公开契约，避免模块继续传入可声明字段结构。
- `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`
  - 收缩为资源接入、provider、特殊 renderer、特殊 handler、数据映射等代码内核扩展。

明确不在范围内：

- 部署脚本、CI、密钥、基础设施编排。
- 后端数据库迁移主线。
- Shell 整体视觉重做。
- 非人员中心页面的大规模跟随改造。

## 5. 设计约束

- metadata 只表达“它是什么、归谁、允许什么”，不表达“怎么执行、怎么切换、怎么回滚”。
- `model metadata` 负责字段级静态语义；`view metadata` 不反向重写字段类型、数据源或表单模式。
- `view metadata` 负责页面内部结构：查询、默认值、列表列、动作、summary block、使用哪些 section。
- `nav metadata` 只负责菜单入口、路径、层级、图标、入口权限和 entry target。
- `dic metadata` 负责显示文案，不允许 TSX 注册代码继续硬编码标准字段、列、查询和按钮中文。
- `languages.json` 是支持语言集合真源；标准 CRUD 不单独声明语言列表。
- `enum metadata` 负责枚举候选项；字段只通过 `dataSource.type = enum` 引用。
- `EID / ENAME / ECODE` 是平台协议字段，业务 metadata 不允许自行声明 alias/mapping。
- lookup provider、特殊 renderer、特殊 action handler 仍由模块代码注册，Shell 只通过受控 key 查找。
- 人员中心 V1 不按多公司能力设计；列表页不展示公司筛选和公司列。若当前组织模型仍保留 `CompanyId`，它只作为既有表单归属/组织根字段参与过渡，不作为下一批标准 CRUD 样板默认复制的筛选或列表列。
- 部门本身是树形结构，但标准 CRUD 元数据回收 V1 不引入 `tree view`、`treeSelect`、`cascader` 或层级 option schema。人员表单里的部门选择先由 provider 返回扁平候选项，必要时用完整路径 label 降低歧义；组织树维护继续留在 Organization 模块专属页面。
- V1 不支持 metadata 表达式显隐、动态联动脚本、按行动态按钮能力预计算。
- 任何 schema 扩展都必须有校验失败信号，不能静默忽略未知 `uiType`、未知 dataSource、未知 action 或不存在字段。

## 6. 总体方案

采用“现有 metadata 类型补强，而不是新增元数据类型”的方案。

### 6.1 三层分工

- `nav metadata`
  - 控制人员中心是否出现在菜单、挂在哪个父节点、路径是什么、入口类型是什么、入口权限是什么。
- `view metadata`
  - 控制人员中心页面内显示哪些查询项、默认查询条件、列表列、页面动作、行动作、详情动作、summary block 和导出能力。
- `model metadata`
  - 控制 `Person` 字段是什么、字段使用什么控件、字段属于哪个 section、字段在 create/edit/view 中是 editable、readonly 还是 hidden，以及字段的数据源。

### 6.2 为什么不新增 control metadata

控件类型是字段静态语义的一部分，不是独立生命周期对象。若新增 `controls/*.json`，人员中心会为了一个字段同时跳转 model、view、control、provider 多处文件，反而破坏 AI-first 小上下文目标。

V1 只在 model field 上声明 `uiType`，Shell 通过固定控件 registry 渲染。控件 registry 是代码内核，不是 metadata 类型。

### 6.3 为什么不新增 action metadata

按钮动作从属于具体页面视图。`create / refresh / view / edit / delete / disable` 在人员列表页、详情页的展示位置、顺序和权限不同，因此应继续放在 `view metadata` 的 `pageActions / rowActions` 中。

动作执行流程、确认弹窗、loading、错误展示由 Shell runtime 负责；模块专属逻辑通过 `handlerKey` 绑定代码注册。

### 6.4 为什么不新增字段映射机制

`EID / ENAME / ECODE` 已经由后端平台内核固定投影，并由 DTO 生成器固定转换为 `Id / DisplayName / Code`。这属于平台协议，不属于业务 metadata。

V1 不引入通用 `mapsTo / alias / apiField / uiField` 映射。metadata 使用协议字段名和模型字段名，友好命名转换由平台代码固定处理。

### 6.5 多语言边界

多语言沿用当前平台机制：`modules/Platform/metadata/languages.json` 声明默认语言和支持语言；各模块 `metadata/dictionaries/*.dic.json` 声明文案；运行时 localization manifest 将字典投影为扁平 catalog key，例如 `PersonListView.Column_Name`。

标准 CRUD V1 不新增 `label`、`labelZhs`、`translations` 或页面内联文案。`view / model` 中的 `labelKey / titleKey / descriptionKey / confirmMessageKey / placeholderKey` 都只是字典 entry key。Shell 解析时用当前 view 的 `dicKey` 组合完整 key：`${dicKey}.${labelKey}`。

字典 entry 必须覆盖 `languages.json` 中所有支持语言，继续由 `scripts/validate-dic-localization.ps1` 校验。运行时 `t()` 可以按“当前语言 -> 默认语言 -> fallback -> key”回退，但该回退只保证页面可用，不作为缺翻译的长期接受标准。

枚举文案仍归 enum/dic 现有链路，不在 view 的查询项或列声明中重复翻译。provider/model lookup 返回的业务数据标签，例如部门路径、岗位名称、人员姓名，是业务数据展示，不要求进入 dic；只有静态 UI 文案和 static option label 使用 `labelKey`。

## 7. 关键模型

### 7.1 model field

`Person.model.json` 中每个字段允许补齐以下字段级声明。`uiType`、`form`、`view`、`dataSource` 已经是当前模型元数据的受控字段分区，本设计优先扩展它们的含义，而不是新增平行结构：

```json
{
  "name": "EmploymentStatus",
  "storage": {
    "type": "string",
    "nullable": true
  },
  "contract": {
    "valueType": "enum",
    "clrType": "EmploymentStatus"
  },
  "uiType": "select",
  "form": {
    "create": {
      "mode": "editable",
      "required": false
    },
    "edit": {
      "mode": "editable"
    }
  },
  "view": {
    "visible": true
  },
  "dataSource": {
    "type": "enum",
    "key": "EmploymentStatus"
  }
}
```

字段模式 V1 只允许：

- `editable`
- `readonly`
- `hidden`

字段控件 V1 对应 `uiType`，只允许：

- `text`
- `textarea`
- `number`
- `switch`
- `checkbox`
- `select`
- `lookup`
- `date`
- `dateRange`
- `readonly`

V1 明确不提供 `treeSelect`、`cascader`、`organizationTreeSelect` 等树形控件类型。树形选择只有在形成跨模块稳定复用能力后，才单独设计为受控 `uiType` 子类型。

Shell 可以根据字段语义推导默认控件：

- `contract.valueType = enum` 默认 `select`
- `dataSource.type = model` 默认 `lookup`
- `storage.type = bool` 默认 `switch`
- `storage.type = date/datetime` 默认 `date`
- 其他字符串默认 `text`

显式 `uiType` 只用于覆盖默认推导，不能引入未知控件。若字段未声明 `uiType`，Shell 可以按上述规则推导控件；若推导失败，默认使用 `readonly` 或直接校验失败，具体策略在实施计划中固定。

内核固定字段可以补充 UI/schema 信息但不重复 storage。例如 `ENAME` 已通过 `builtinFields` 启用时，允许在 `fields` 中增加同名条目声明 `uiType / form / view / dataSource`，但不能重新声明 `storage`。

### 7.2 dataSource

字段数据源 V1 支持四类。

`enum`：

```json
{
  "type": "enum",
  "key": "EmploymentStatus"
}
```

候选项来自现有 enum metadata。字段不得在 model metadata 中重复内联枚举项。

`model`：

```json
{
  "type": "model",
  "key": "Department",
  "model": "Department"
}
```

只声明“这是对另一个模型的受控引用”。V1 默认使用平台协议字段 `EID` 作为候选值、`ENAME` 作为候选标签；若目标模型不符合该协议，首轮应改用 `provider`，不在 metadata 中新增字段映射。实际查询 API、权限、分页、搜索和缓存由代码 provider 承接，后端标准 option endpoint 暂不作为 V1 默认路径。

`provider`：

```json
{
  "type": "provider",
  "key": "organization.positions",
  "dependsOn": ["DepartmentId"]
}
```

用于部门、岗位这类需要模块代码参与的 lookup。metadata 只声明 provider key 和依赖字段，加载逻辑由模块注册。人员中心列表不因 provider 存在而恢复公司筛选或公司列。部门虽然具有树形父子关系，但 V1 provider 仍返回扁平 option 列表；如果同名部门可能造成歧义，provider 应把路径拼入 label，例如 `集团 / 研发中心 / 平台组`。

`static`：

```json
{
  "type": "static",
  "options": [
    {
      "value": "enabled",
      "labelKey": "Status_Enabled"
    }
  ]
}
```

仅用于少量不会演化为 enum 的局部固定选项。若选项具有业务枚举语义，应优先使用 enum metadata。

### 7.3 list view query

`PersonList.view.json` 的 `query` 控制轻筛选条和默认值：

```json
{
  "query": {
    "fields": [
      {
        "key": "keyword",
        "labelKey": "Query_Keyword",
        "source": "preset",
        "preset": "keyword",
        "operator": "like"
      },
      {
        "key": "employmentStatus",
        "labelKey": "Query_EmploymentStatus",
        "source": "model",
        "field": "EmploymentStatus",
        "operator": "="
      },
      {
        "key": "isEnabled",
        "labelKey": "Query_IsEnabled",
        "source": "model",
        "field": "IsEnabled",
        "operator": "=",
        "hidden": true
      }
    ],
    "defaults": [
      {
        "field": "isEnabled",
        "value": true
      }
    ],
    "defaultSort": {
      "field": "PersonNo",
      "direction": "asc"
    },
    "defaultPageSize": 20
  }
}
```

规则：

- `labelKey` 指向当前 view `dicKey` 下的字典 entry，例如 `PersonListView.Query_Keyword`。
- `source = model` 时，`field` 必须引用 `Person.model.json` 中存在的字段或内核固定字段。
- `source = preset` 时，`preset` 首轮只允许 `keyword`。
- `defaults.field` 引用查询项的 `key`，不是模型字段名；该 key 必须存在于 `query.fields` 或 `advancedFilter.sections.fields` 中。若默认值不显示在筛选区，需要用 `hidden: true` 显式声明隐藏查询项，而不是隐式塞入。
- 默认值只作用于首次进入页面和点击重置。
- 查询参数名使用 `key`，绑定模型字段使用 `field`。这不是通用字段映射，只是查询项来源声明。

### 7.4 list columns

`PersonList.view.json` 的 `columns` 控制列表显示：

```json
{
  "columns": [
    {
      "key": "personNo",
      "labelKey": "Column_PersonNo",
      "source": "model",
      "field": "PersonNo",
      "width": 140,
      "sortable": true
    },
    {
      "key": "departmentName",
      "labelKey": "Column_DepartmentName",
      "source": "projection",
      "field": "departmentName"
    }
  ]
}
```

规则：

- `labelKey` 指向当前 view `dicKey` 下的字典 entry，例如 `PersonListView.Column_DepartmentName`。
- `source = model` 表示列绑定模型字段。
- `source = projection` 表示列来自列表 DTO/projection，例如 `companyName`、`departmentName`。
- `source = projection` 必须显式声明 `field`，该字段名对应前端标准化后的列表行属性；这里不引入 `mapsTo / alias / apiField` 等通用字段映射。
- 列标题始终由 `labelKey` 提供，不从模型字段 label 自动继承。
- 特殊单元格渲染，如状态标签颜色，允许通过代码 renderer registry 按列 key 或字段语义覆盖。
- `departmentName` 这类 projection 字段需要显式声明来源，但不进入 model metadata。
- projection 字段运行时值为 `null / undefined` 时，列表单元格按当前容错策略显示为空，不把单行数据缺值升级为整页错误。
- 已声明 projection 字段的稳定返回由后端/前端契约测试保障；人员中心这类样板资源必须测试列表 DTO 包含 `departmentName / positionName` 等已声明 projection 字段。
- 若某个显示列需要“显示名缺失时退回 ID”等体验兜底，应由模块 mapper 或受控 renderer 实现，不新增通用 fallback metadata，避免字段映射机制扩散。

### 7.5 actions

动作仍放在 view metadata 中。

列表页面动作：

```json
{
  "pageActions": [
    {
      "key": "create",
      "labelKey": "Action_Create",
      "standardAction": "create",
      "variant": "primary",
      "permissions": ["Organization.Person.Create"]
    },
    {
      "key": "refresh",
      "labelKey": "Action_Refresh",
      "standardAction": "refresh"
    }
  ]
}
```

列表行动作：

```json
{
  "rowActions": [
    {
      "key": "view",
      "labelKey": "Action_View",
      "standardAction": "view"
    },
    {
      "key": "disable",
      "labelKey": "Action_Disable",
      "standardAction": "disable",
      "variant": "danger",
      "confirmMessageKey": "Action_Disable_Confirm",
      "permissions": ["Organization.Person.Disable"],
      "handlerKey": "organization.person.disable"
    }
  ]
}
```

规则：

- `standardAction` 首轮允许 `create / refresh / view / edit / delete / disable / submit`。
- `variant` 首轮允许 `default / primary / danger`。
- `labelKey` 和 `confirmMessageKey` 指向当前 view `dicKey` 下的字典 entry，不允许直接硬编码确认文案。
- `permissions` 只声明按钮显示所需权限，权限判断由 Shell/Auth 运行时执行；字段命名与 nav metadata 保持一致，统一使用数组。
- `handlerKey` 只绑定模块注册的受控 handler，不描述执行步骤。
- 导出能力继续使用 `export` 区块，不进入 `pageActions`。

### 7.6 detail view

`PersonDetail.view.json` 负责选择详情页使用哪些 section、summary block 和详情动作：

```json
{
  "sections": [
    "basic",
    "organization"
  ],
  "summaryBlocks": [
    "userLink",
    "authorizationSummary"
  ],
  "pageActions": [
    {
      "key": "edit",
      "labelKey": "Action_Edit",
      "standardAction": "edit"
    },
    {
      "key": "disable",
      "labelKey": "Action_Disable",
      "standardAction": "disable",
      "variant": "danger",
      "confirmMessageKey": "Action_Disable_Confirm",
      "handlerKey": "organization.person.disable"
    }
  ]
}
```

section 内字段顺序、字段模式和控件类型继续来自 `Person.model.json`。

### 7.7 协议字段

`EID / ENAME / ECODE` 继续按当前实现处理：

- `model metadata` 使用协议字段名。
- `StandardCrudDescriptor` 和 `StandardCrudFieldSet` 使用协议字段名。
- 后端标准 CRUD 字段字典优先返回协议字段名。
- DTO 友好命名由平台代码固定处理：`EID -> Id`、`ENAME -> DisplayName`、`ECODE -> Code`。
- 前端 runtime 可以规范化出 `id / displayName / code`，但这是 SDK/Shell 内核行为，不是业务 metadata 映射。
- `PersonNo` 是人员业务字段，不自动映射为 `ECODE`。

## 8. 核心流程

### 8.1 列表页装配

1. Shell 通过 nav entry 找到 `targetKey = organization.person.list`。
2. Runtime metadata provider 加载 `PersonList.view.json`。
3. Standard CRUD registry 按 `listViewKey` 找到人员资源注册。
4. Schema resolver 读取 view query、columns、actions、export。
5. Schema resolver 读取 `Person.model.json`，为 `source = model` 的字段补齐 `uiType`、数据源、表单模式和静态校验语义。
6. Shell 根据 query schema 渲染轻筛选条和高级筛选；人员中心 V1 不渲染公司筛选。
7. 首次进入页面时应用 `query.defaults / defaultSort / defaultPageSize`。
8. 标准 CRUD client 按 query key 发起列表请求。
9. 表格按 columns schema 渲染，特殊列 renderer 由代码 registry 覆盖；人员中心 V1 不渲染公司列。
10. 页面动作和行动作按 metadata 声明渲染，执行流程由 Shell action runtime 处理。

### 8.2 创建/编辑抽屉装配

1. 用户点击 `create` 或 `edit` 标准动作。
2. Shell 读取 detail view 使用的 sections。
3. Shell 根据 model sections 和字段 `form.create/edit` 模式合成表单 schema。
4. lookup 字段根据 dataSource 装配 enum/model/provider/static 候选项；`model` 数据源首轮解析为 provider registry 中的受控 provider，后端标准 option endpoint 暂不自动接管。部门字段首轮仍按普通 lookup 渲染，不升级为树形选择器。
5. 用户保存时，Shell 按字段 schema 标准化提交值。
6. 标准 CRUD client 调用 create/update。
7. 后端 required/unique/delete guard 等内核校验失败时，Shell 显示稳定错误。

### 8.3 详情页装配

1. Shell 按 id 加载标准 CRUD detail。
2. Shell 根据 detail view sections 和 model field view/form 模式渲染只读详情。
3. summaryBlocks 由 detail 返回的 summaries 驱动，特殊 renderer 由模块代码注册。
4. pageActions 按 metadata 渲染，特殊 handler 由 handlerKey 找到模块代码。

## 9. 接口与集成点

### 9.1 metadata authoring

当前 `server/platform/metadata/file-type-mappings.json` 尚未声明 `view` adapter，但运行时投影已经包含 view 文件。由于元数据配置中心正式暂停开发，本轮不新增 `view` mapping，也不补 `ViewAuthoringAdapter`。未来若恢复元数据配置中心并要求编辑 `*.view.json`，再单独补齐 `view` authoring adapter 和校验链路。

本设计不要求新增 action/control metadata 类型。若补 adapter，语义类型仍是 `view`。

### 9.2 Shell resolver

Shell 需要形成一个标准 CRUD schema 合成入口：

- 输入：`view document`、`model document`、`resource registration`
- 输出：列表 schema、详情 schema、表单 schema、动作 schema、lookup schema

resolver 负责校验：

- view 引用的 model 是否存在。
- query/column 引用的 model field 是否存在。
- query defaults 是否引用已声明查询项。
- action `standardAction` 是否在允许集合内。
- action `handlerKey` 是否能在资源注册中找到。
- dataSource type 是否受支持。
- `uiType` 是否受支持。
- 标准 CRUD view/model 引用的 `labelKey / titleKey / descriptionKey / confirmMessageKey` 是否能在默认语言字典中解析。

### 9.3 模块资源注册

人员中心 `personStandardCrudResource` 后续只保留代码扩展：

- `resourceKey / apiRoute / listViewKey / detailViewKey / routePath`
- lookup provider 实现
- 特殊 summary renderer
- 特殊 action handler
- 特殊 column/cell renderer
- `mapListItem / mapDetail / getRowId`
- 必要的数据规范化函数

不再长期保留：

- 可声明字段数组
- 可声明表单分组
- 可声明标准列数组
- 可声明标准查询块 React 组件
- 可声明标准 page/row/detail actions

## 10. 数据与状态

- metadata authoring 真源仍在 `modules/<ModuleId>/metadata/**`。
- runtime metadata 只表示当前生效视图，不作为长期 authoring 真源。
- 查询状态、分页状态、抽屉状态、保存状态、lookup loading/error、action loading/error 仍归 Shell runtime 管理。
- 默认查询条件和默认排序来自 view metadata，只在首次进入和重置时应用。
- 表单初始值来自 detail 或 create default，不从 view metadata 直接写入运行时状态。
- `EID / ENAME / ECODE` 协议字段由后端模型投影和 DTO/SDK 内核统一处理。

## 11. 错误处理与风险

失败信号：

- view metadata 引用不存在的 model。
- query/column/default/action 引用不存在字段、未知 preset、未知 `uiType` 或未知 dataSource。
- `handlerKey` 未注册但 action 不是 Shell 可内建执行的标准动作。
- `dataSource.type = provider` 但 provider key 未注册。
- `dataSource.type = model` 无法解析到受控 provider，或尝试隐式调用尚未设计的后端通用 model option endpoint。
- `dataSource.type = enum` 但 enum key 不存在。
- 标准 CRUD metadata 引用的字典 key 在默认语言中不存在，或 dic entry 未覆盖 `languages.json` 声明的支持语言。
- 人员中心列表 metadata 重新声明公司筛选或公司列。
- 标准 CRUD V1 metadata 声明 `treeSelect / cascader / tree view` 等尚未纳入主线的树形能力。
- `source = projection` 列未声明 `field`，或契约测试发现样板资源声明的 projection 字段未被列表 DTO 稳定返回。
- `query.defaults.field` 没有出现在任何查询声明中。
- `list.export` 与 `pageActions` 重复表达导出。

风险控制：

- 第一批只回收人员中心，不同时复制到下一批资源。
- 保留代码注册扩展点，避免把特殊 renderer 和 handler 过度 metadata 化。
- 未知 schema 直接失败并显示可定位错误，不静默降级。
- 人员中心原 TSX 注册结构在回收前作为对照样本，必要时可回滚。

回滚思路：

- 回滚 Shell schema resolver 和 Host 消费 metadata 的改动。
- 恢复 `personStandardCrudResource.tsx` 中原字段、分组、列、查询和动作声明。
- 保留 runtime metadata 文件不影响旧代码路径；若 metadata schema 已被修改，可按 git 反向回退对应 `modules/Organization/metadata/**` 文件。

## 12. 验证方案

设计文档验证：

```powershell
./scripts/validate-ai-first-design.ps1 -Path docs/superpowers/specs/platform/2026-05-24-standard-crud-metadata-recovery-design.md
./scripts/check-ai-first-design-targets.ps1
./scripts/validate-dic-localization.ps1
```

后续实施最小验证：

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-schema-and-registry standard-crud-list-host standard-crud-detail-host person-center-crud-pro-sample
pnpm --dir frontend --filter @icp-platform/shell build
```

若涉及后端 model/schema 校验：

```powershell
dotnet test tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
dotnet test tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
```

手工 smoke：

- 打开 `/organization/persons`。
- 确认轻筛选、默认查询、列表列、创建/编辑/查看抽屉、删除/停用动作仍可用。
- 确认部门、岗位等 projection 列有值时正常展示，个别行缺值时不导致整页错误。
- 确认字段文案、按钮文案和确认文案来自 dic，而不是 TSX 硬编码。
- 切换到 `zhs / en` 时，标准 CRUD 标题、列名、查询项、动作、确认提示和枚举标签均来自 runtime localization manifest。

## 13. 未决问题

暂无。

已收口问题：

- 元数据配置中心正式暂停开发，因此本轮不新增 `view` 到 `server/platform/metadata/file-type-mappings.json`，也不补 `ViewAuthoringAdapter`。
- `model dataSource` V1 不自动调用后端标准 option endpoint；它只表达模型引用语义，实际候选项加载仍通过 Shell/模块 provider registry 承接。若找不到 provider，应明确失败。
- 人员中心 V1 不按多公司能力设计；列表继续不展示公司筛选和公司列。
- 部门树结构不进入标准 CRUD 元数据回收 V1。组织树页继续作为 Organization 模块专属页面存在；人员表单部门选择先使用扁平 lookup，路径展示由 provider label 承接。
- projection 列缺失采用分层处理：schema 未声明 `field` 或契约测试发现字段未稳定返回时强失败；生产运行时单行值为空时按当前列表容错显示为空；显示名退回 ID 等体验兜底留在模块 mapper/renderer，不新增通用 fallback metadata。
- 多语言沿用现有 `languages.json + dic metadata + runtime localization manifest`，标准 CRUD 只引用 key，不内联翻译，也不新增专属 i18n 类型。

## 14. 实施拆分建议

1. 固化 schema 类型和校验
   - 扩展 Shell 标准 CRUD schema resolver。
   - 增加 model/view schema 单元测试。

2. 回收人员中心列表声明
   - 让查询字段、默认查询、列表列和动作优先来自 `PersonList.view.json`。
   - 删除或收缩 `renderFilters / buildColumns / listActions` 的可声明内容。

3. 回收人员中心表单声明
   - 在 `Person.model.json` 补齐 `uiType`、form mode、dataSource、default、required。
   - 让 StandardCrudForm 从 metadata schema 生成。

4. 回收详情动作和 summary 声明
   - 让 `PersonDetail.view.json` 成为详情 sections、summaryBlocks、pageActions 的真源。
   - 模块代码只保留 summary renderer 和 disable handler。

5. 补齐文档和验证记录
   - 更新当前交接台账和 verification 记录。
   - 明确人员中心已从“代码注册驱动”收敛为“metadata 声明 + 代码内核执行”。
   - 补跑 dic localization 校验，确认新增/调整的 CRUD 文案覆盖所有支持语言。

## 15. AI-First 最小检查清单

- 入口在哪里：
  - 设计入口是 `docs/superpowers/specs/platform/2026-05-24-standard-crud-metadata-recovery-design.md`。
  - 实施入口应从 `frontend/packages/shell/src/metadata/standardCrudSchemaResolver.ts`、`modules/Organization/metadata/views/PersonList.view.json`、`modules/Organization/metadata/models/Person.model.json` 开始。

- 边界在哪里：
  - metadata 只声明标准结构、字段语义、数据源引用、动作入口。
  - Shell/SDK 负责执行、校验、状态机和错误边界。
  - Organization 模块代码只保留 provider、renderer、handler 和数据映射。

- 如何验证：
  - 先跑设计校验脚本。
  - 实施后跑 Shell 标准 CRUD 定向测试和构建。
  - 涉及后端模型或 CRUD descriptor 时补跑对应 dotnet tests。

- 失败信号是什么：
  - 未知 `uiType`/dataSource/action 被静默忽略。
  - 修改字段、列、查询、动作仍主要需要改 `personStandardCrudResource.tsx`。
  - 人员中心页面行为和 metadata 声明不一致。
  - 文案仍大量硬编码在 TSX 中。

- 如何回滚：
  - 反向回退 Shell metadata resolver/Host 改动。
  - 恢复人员中心前端注册代码中的字段、列、查询和动作声明。
  - 反向回退 `modules/Organization/metadata/**` 中本轮 schema 调整。
