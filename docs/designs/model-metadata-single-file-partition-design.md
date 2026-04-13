# 单文件分区模型元数据设计

## 1. 背景

当前仓库已经明确两条长期约束：

- 模型结构语义需要越来越适合 AI 在小上下文内生成、修改、校验和回滚
- 元数据不能继续向“万能解释器”演化，装载、校验、切换、回滚仍必须收口在代码内核

现状中，模型定义主要以 C# 强类型形式存在，例如 `ModelDefinition / FieldDefinition / IndexDefinition`。这种形式在编译安全和执行链路上有优势，但也存在两个问题：

- 结构语义分散在代码中，AI 或开发者做批量生成、对比和审阅时上下文成本偏高
- 团队对“字段可编辑、字段处理类型、创建/编辑差异、详情固定显示值”等声明性 UI 语义有实际需求，但这些语义若直接塞回 C# 执行代码，后续会越来越难抽离

本设计的目标不是把老平台的模型设计器原样搬回新平台，而是在现有 `ModelDefinition` 路线之上，新增一种更利于 AI 和人工协作的 authoring 形态，并明确数据库结构语义、表单语义、浏览语义三者的边界。

## 2. 目标

- 为模块模型定义提供一种单文件、分区式的元数据 authoring 格式。
- 让数据库结构语义成为该文件的主要权威输入之一，并能受控投影为现有 `ModelDefinition`。
- 允许在同一文件中承载创建/编辑与详情展示所需的声明性 UI 语义，但保持与数据库结构语义分区隔离。
- 明确哪些属性可以影响数据库 migration，哪些只能影响前端 schema，哪些禁止进入元数据。
- 保持“元数据负责声明，代码内核负责校验、投影、迁移、回滚”的总体架构不变。

## 3. 非目标

- 不在本设计中实现运行时自动改库。
- 不在本设计中重建老平台式模型设计器或页面解释器。
- 不在本设计中纳入保存流程、处理类型驱动逻辑、事件脚本、动态分支算法。
- 不在本设计中取代现有 EF Core migration 工具链。
- 不要求本轮同时落地所有前端页面消费能力；`form/view` 分区可先定义契约，后续再按模块逐步接入。

## 4. 范围

本设计涉及：

- `server/modules/<ModuleId>/metadata/models/*.model.json`
- `server/platform-modeling/**`
- 现有 `ModelDefinition / FieldDefinition / IndexDefinition / ModelKind`
- 后续用于把元数据投影为 EF Core 模型或 migration 草案的适配层
- 后续前端或 authoring 服务可消费的 `form / view` 分区与字段级处理类型

本设计不涉及：

- Host / Shell 接入结构调整
- 旧平台 `.page` / `.view` / `.generator` 的迁移实现
- 运行时数据库自动同步器
- 复杂审批流、多人协同编辑、版本发布中心

## 5. 设计约束

- 元数据只能描述“它是什么、允许什么、如何展示”，不能描述“如何执行、如何切换、如何回滚”。
- 数据库结构变更必须经过显式校验、显式 migration 和测试，不得在运行时隐式改库。
- 单文件设计不能退化为属性大杂烩，必须通过固定分区维持边界。
- 前端 UI 语义可以与模型同文件 authoring，但不得反向污染数据库投影规则。
- 若某项语义只能通过人工口口相传解释其行为，则不应进入该设计。

### 5.1 AI-first 最小检查补充

- 入口在哪里：模型 authoring 入口建议为 `server/modules/<ModuleId>/metadata/models/*.model.json`；后端消费入口为元数据解析器与 `ModelDefinition` 投影适配器。
- 边界在哪里：`storage` 只负责数据库结构；`form` 只负责创建/编辑；`view` 只负责详情页是否显示该属性；字段级 `uiType` 负责输入控件语义与固定显示值语义；列表列编排不进入该文件；流程、切换、回滚、处理算法不进入该文件。
- 如何验证：通过 JSON schema 校验、`ModelDefinition` 投影测试、EF Core migration 生成测试、前端 schema 读取测试和 smoke 验证。
- 失败信号是什么：若实现后需要运行时自动改库、若 `form/view` 属性进入数据库投影、若后续新增场景只能继续平铺字段属性，说明设计边界失守。
- 如何回滚：删除或停用元数据 authoring 入口，保留现有 C# 定义与 migration 工具链；投影适配层可逐模块回退到纯 C# 提供者。

## 6. 总体方案

采用“单文件分区 authoring + 代码适配器投影 + 显式 migration”的方案。

每个模型对应一个 `*.model.json` 文件，在同一文件内固定分为三个责任区，并允许每个字段声明一个稳定的字段级处理类型键：

- `storage`：数据库结构语义
- `form`：新建/编辑语义
- `view`：详情展示语义

其中：

- `storage` 分区可以投影为现有 `ModelDefinition`，并进一步生成 EF Core 配置或 migration 草案
- `form / view` 分区与字段级处理类型仅供前端 schema 或 authoring 服务消费，不参与数据库 migration
- 文件整体作为 AI 与人工审核的 authoring 输入，真正的迁移执行仍通过代码工具链完成

选择这个方案的原因：

- 相比纯 C#，它更利于 AI 做结构化生成、批量比对和局部修改
- 相比拆成三个独立文件，它仍保持“一个模型一个文件”的低心智负担
- 相比单文件平铺，它能强制保留存储、表单、详情展示三类语义的边界，并把字段处理类型控制在受控声明范围内

本轮不选这些方案：

- 不选“全部继续写在 C#”：结构语义集中度不够，不利于 AI authoring
- 不选“拆成三个独立 JSON”：边界清楚，但对首轮落地偏重
- 不选“单文件平铺所有属性”：最省事，但后续最容易退化为元数据万能包
- 不选“元数据直接驱动运行时改库”：高风险，且违背当前仓库的代码内核边界

## 7. 关键模型

### 7.1 文件结构

建议路径：

```text
server/modules/<ModuleId>/metadata/models/<ModelName>.model.json
```

说明：

- 单文件 = 单模型一个文件。
- 一个模块可以包含多个 `*.model.json` 文件。
- 模块不需要额外清单文件，直接扫描 `metadata/models/*.model.json`。
- 模型名和表名同源，字段名和列名同源，不再单独维护 `table` 与 `column`。
- `kind` 必须保留，首轮至少支持 `Entity` 与 `Link`。

建议结构：

```json
{
  "schemaVersion": "1.0",
  "moduleId": "Authorization",
  "model": {
    "name": "Role",
    "kind": "Entity",
    "builtinFields": ["ENAME", "ECODE"],
    "sections": [
      {
        "id": "basic",
        "title": "基本信息"
      }
    ],
    "fields": [
      {
        "name": "ENAME",
        "uiType": "text",
        "layout": {
          "position": "010",
          "width": 50,
          "section": "basic"
        },
        "form": {
          "create": {
            "mode": "editable"
          },
          "edit": {
            "mode": "editable"
          }
        },
        "view": {
          "visible": true
        }
      },
      {
        "name": "Description",
        "storage": {
          "type": "text",
          "nullable": true
        },
        "uiType": "textarea",
        "layout": {
          "position": "020",
          "width": 100,
          "section": "basic"
        },
        "form": {
          "create": {
            "mode": "editable"
          },
          "edit": {
            "mode": "editable"
          }
        },
        "view": {
          "visible": true
        }
      }
    ],
    "indexes": [
      {
        "name": "UX_Role_Code",
        "fields": ["Code"],
        "unique": true
      }
    ]
  }
}
```

### 7.2 三类分区职责

`storage` 负责：

- 数据库结构类型与可空性
- 长度、精度、标度
- 可进入 `ModelDefinition` 与 migration 的字段结构语义

`layout` 负责：

- 字段静态布局
- `position`、`width`、`section`
- `create/edit` 共用的排布语义

`section` 负责：

- 页面静态分组边界
- `id`、`title`
- 不参与宽度计算，只作为分组容器
- section 内字段必须连续，不可被其他 section 或根层级字段打断

`form` 负责：

- `create/edit` 场景行为
- `hidden / readonly / editable` 模式差异

`view` 负责：

- 详情页是否显示该属性

字段级 `uiType` 负责：

- 输入控件语义
- 固定显示值语义

字段级 `dataSource` 负责：

- 选择型或引用型字段的数据来源
- 首轮只支持 `enum` 与 `model`
- 不属于 `storage`

独立 `.view` 元数据负责：

- 列表显示哪些列
- 列表布局、排序、列宽、分组等页面级编排

### 7.3 允许影响数据库的字段

只允许 `storage` 分区中的以下信息影响数据库结构与 migration：

- 模型名
- 字段名
- 模型类型对应的固定字段集合
- 基础类型
- 可空性
- 长度、精度、标度
- `linkedTypes` 提供的 `Link` 类型组合约束
- 普通索引与唯一索引

### 7.4 禁止影响数据库的字段

以下字段即使和模型写在同一文件中，也不得参与数据库投影：

- `layout`
- `position`
- `width`
- `section`
- `mode`
- `visible`
- `uiType`
- `dataSource`
- 任何 create/edit/view 专属声明
- 多语言显示文本

### 7.5 禁止进入该文件的语义

以下内容不应进入单文件分区模型元数据：

- 处理类型驱动的业务流程
- 保存前后钩子
- 条件分支算法
- 运行时装载顺序
- 回滚策略
- 自动改库策略
- 动态脚本表达式
- SQL 或脚本型数据源
- 条件显示、动态布局、联动查询
- 删除策略表达式，例如直接在元数据中声明 cascade / restrict 行为

如果后续确有需要，应以代码服务、显式规则引擎或独立设计文档承接，而不是继续扩充该文件。

### 7.6 模型类型与固定字段

`kind = Entity` 与 `kind = Link` 都对应平台内建基础字段集，由代码内核自动补入，而不是每个模型在元数据中完整重写。

`Entity` 首轮强制固定字段：

- `EID`
- `CreatedBy`
- `CreatedAt`
- `UpdatedBy`
- `UpdatedAt`

`Entity` 首轮可选固定字段：

- `ENAME`
- `ECODE`

`Link` 首轮强制固定字段：

- `LID`
- `PEID`
- `CEID`
- `LCreatedBy`
- `LCreatedAt`
- `LUpdatedBy`
- `LUpdatedAt`

`Link` 首轮可选固定字段：

- `LSequence`

可选固定字段通过模型级 `builtinFields` 启用，并按 `kind` 严格校验：

- `Entity` 只允许启用 `ENAME`、`ECODE`
- `Link` 只允许启用 `LSequence`

固定字段允许在 `fields` 中显式引用，用于补充：

- `uiType`
- `layout`
- `form`
- `view`

但不允许修改其基础存储定义。

### 7.7 布局与场景语义

布局只支持静态布局，不支持条件显示、动态重排或表达式驱动逻辑。

字段级共享 `layout` 至少包括：

- `position`
- `width`
- `section`

规则如下：

- `position` 在同一模型内必须唯一
- `width` 为任意整数百分比
- 同一行累计宽度超过 `100` 时自动换行
- 不写 `section` 的字段属于页面根层级字段
- `section` 本身不定义宽度和列数，只作为分组边界

`form` 负责 `create/edit` 行为差异，首轮采用：

- `form.create.mode`
- `form.edit.mode`

`mode` 首轮允许值：

- `hidden`
- `readonly`
- `editable`

`view` 首轮先保持：

- `visible`

### 7.8 Link 的类型边界与 linkedTypes

`Link` 是受控关系对象，不是普通实体变体。它既有自己的对象标识与审计字段，也必须声明允许连接哪些模型类型。

因此 `kind = Link` 的模型必须支持 `linkedTypes`，并且至少定义一条规则。每条规则首轮最小结构为：

- `parent`
- `child`

示例：

```json
"linkedTypes": [
  { "parent": "Organization", "child": "Company" },
  { "parent": "Company", "child": "Group" },
  { "parent": "Group", "child": "Role" },
  { "parent": "Role", "child": "User" }
]
```

`linkedTypes` 的职责是：

- 限制该 `Link` 模型允许连接哪些模型类型组合
- 支撑保存时的类型合法性校验
- 支撑删除对象时快速定位可能关联的 `Link` 模型范围

`linkedTypes` 不负责：

- 声明删除策略
- 声明级联行为
- 声明运行时流程

删除时是阻止、级联删除还是执行其他清理动作，仍应由代码内核决定。

### 7.9 索引与唯一性

唯一性统一通过 `indexes` 表达，不单独作为字段属性存在。

平台自动补入以下内建索引：

- `Entity`：`EID` 唯一索引
- `Link`：`LID` 唯一索引
- `Link`：`PEID` 普通索引
- `Link`：`CEID` 普通索引

`(PEID, CEID)` 默认允许重复，不作为平台通用唯一约束。若某个具体 `Link` 模型要求父子组合唯一，必须在 `indexes` 中显式声明唯一复合索引。

元数据中不应重复声明已经由内核自动补入的内建索引。

### 7.10 数据源与多语言

`dataSource` 属于字段级 UI/schema 属性，不属于 `storage`。

首轮只支持两类数据源：

- `enum`
- `model`

其中：

- `enum` 引用已有枚举元数据文件
- `model` 首轮只声明目标模型，不声明 `valueField`、`labelField`、`filter`

`model` 数据源的默认显示规则：

- 优先 `ENAME`
- 再回退 `ECODE`
- 再回退主键标识（`EID` 或 `LID`）

多语言是平台级基础能力，应贯穿模型 authoring、schema 生成和前后端展示。模型元数据不直接内嵌多语言文本，而应通过稳定 key 规则映射到现有多语言元数据文件。

建议默认 key 规则：

- 模型：`Model.<ModelName>`
- 字段：`Model.<ModelName>.Field.<FieldName>`
- Section：`Model.<ModelName>.Section.<SectionId>`

### 7.11 存储类型映射文件

`storage.type` 不直接复用后端强类型枚举，也不直接暴露底层数据库类型名，而是使用平台级稳定枚举，并通过一份独立的类型映射元数据文件完成投影。

建议新增平台级元数据文件：

```text
server/platform-modeling/metadata/storage-types.json
```

该文件首轮承担以下职责：

- 定义合法的 `storage.type`
- 定义每种平台类型允许的约束能力
- 定义该类型在不同数据库 provider 下的默认存储映射

类型映射文件建议采用对象字典结构，`types` 的 key 直接作为合法的 `storage.type` 值：

```json
{
  "schemaVersion": "1.0",
  "types": {
    "string": {
      "capabilities": {
        "supportsLength": true,
        "supportsPrecision": false,
        "supportsScale": false
      },
      "providers": {
        "SqlServer": {
          "storeType": "nvarchar",
          "defaultLength": 200
        },
        "Oracle": {
          "storeType": "nvarchar2",
          "defaultLength": 200
        },
        "MySql": {
          "storeType": "varchar",
          "defaultLength": 200
        }
      }
    },
    "decimal": {
      "capabilities": {
        "supportsLength": false,
        "supportsPrecision": true,
        "supportsScale": true
      },
      "providers": {
        "SqlServer": {
          "storeType": "decimal",
          "defaultPrecision": 18,
          "defaultScale": 2
        },
        "Oracle": {
          "storeType": "number",
          "defaultPrecision": 18,
          "defaultScale": 2
        },
        "MySql": {
          "storeType": "decimal",
          "defaultPrecision": 18,
          "defaultScale": 2
        }
      }
    }
  }
}
```

设计规则如下：

- `types` 的 key 就是合法的 `storage.type`
- 首轮每种类型都必须同时定义 `SqlServer`、`Oracle`、`MySql` 三种 provider 映射
- `capabilities` 负责校验 `maxLength / precision / scale` 是否可用于该类型
- provider 节点负责提供数据库默认存储类型与默认约束值
- 字段自身声明的 `maxLength / precision / scale` 优先级高于 provider 默认值

该文件的职责是“平台类型到建模与存储默认值的映射”，不是旧平台 `DataTypes.datatype` 的直接平移。旧平台文件可作为参考来源，但新平台不再采用把长度或精度固化在类型名中的做法，例如不再使用 `STRING200`、`FLOAT10_5` 这类命名作为模型元数据真源。

## 8. 核心流程

### 8.1 主流程：authoring 与校验

1. AI 或开发者编辑 `*.model.json`
2. JSON schema 校验文件结构、模型类型、分区边界是否合法
3. 语义校验器校验模型名、固定字段、索引、`linkedTypes`、字段重复与布局连续性等规则
4. 若校验通过，进入后续投影或生成环节

### 8.2 主流程：数据库投影

1. 后端读取 `*.model.json`
2. 根据 `kind` 自动补入固定字段与内建索引
3. 仅提取 `storage` 分区与允许影响数据库的模型级结构语义
4. 将其转换为现有 `ModelDefinition / FieldDefinition / IndexDefinition`
5. 由现有持久化适配层继续投影到 EF Core 模型

### 8.3 主流程：migration 生成

1. 在 `storage` 分区变更后触发模型对比
2. 生成 migration 草案或显式 migration
3. migration 进入版本库
4. 通过测试和人工审核后执行数据库升级

### 8.4 主流程：UI schema 消费

1. 前端或 authoring 服务读取同一份文件
2. 提取字段级 `uiType`、`dataSource`、`layout`、`form / view` 分区与 section
3. 结合多语言 key 生成新建/编辑 schema 与详情 schema
4. 列表列仍由独立 `.view` 元数据决定，不反向影响数据库结构

### 8.5 异常流程

- `storage` 中缺少主键：拒绝进入数据库投影
- `builtinFields` 与 `kind` 不匹配：校验失败
- 索引引用不存在字段：拒绝进入 migration 生成
- `linkedTypes` 缺失或声明非法：拒绝 `Link` 模型投影
- section 内字段不连续或 `position` 重复：拒绝进入前端 schema 生成
- `form/view` 或字段级处理类型引用不存在字段：拒绝进入前端 schema 生成
- 某属性出现在错误分区：校验失败
- `storage` 变更试图隐式触发运行时改库：拒绝执行

## 9. 接口与集成点

### 9.1 后端集成点

建议新增或扩展以下能力：

- `IModelMetadataSource`：读取 `*.model.json`
- `ModelMetadataValidator`：校验结构与语义
- `ModelMetadataProjector`：将 `storage` 分区投影为 `ModelDefinition`
- migration 生成工具对接层：基于投影结果生成或比对 EF Core migration

### 9.2 前端集成点

后续若引入模型 authoring 工作台或模块页面生成能力，可消费：

- `uiType`
- `dataSource`
- `layout`
- `sections`
- `form.create`
- `form.edit`
- `view.visible`

前端必须通过受控 schema 消费，不得直接用该文件承载页面脚本。

### 9.3 与现有 C# 定义的关系

首轮建议采用并存策略：

- C# 定义仍可保留为执行期消费接口
- 新元数据文件作为 authoring 源
- 通过适配器把元数据投影为现有强类型对象

待投影链路稳定后，再逐模块决定是否逐步下沉原始 C# authoring 代码。

## 10. 数据与状态

### 10.1 权威源

`*.model.json` 是 authoring 权威源，进入版本库。

### 10.2 运行时消费

运行时数据库结构不直接依赖 JSON 文件热解释，而依赖：

- JSON -> `ModelDefinition`
- `ModelDefinition` -> EF Core 模型
- EF Core migration -> 数据库结构

### 10.3 状态线

建议分离两条状态线：

- 结构状态线：authoring 修改、校验、migration 生成、数据库升级
- UI 状态线：authoring 修改、schema 生成、页面消费

二者可共享同一文件，但不能共享同一执行语义。

## 11. 错误处理与风险

### 11.1 主要风险

- 单文件设计若缺少严格分区校验，后续会重新退化为“一个字段挂满所有属性”的大杂烩
- 若团队把 `uiType/editable` 之类字段也纳入数据库投影，会破坏当前边界
- 若试图让元数据直接运行时改库，会把高风险迁移问题塞回元数据链路
- 若前端消费端过早绑定这份文件的内部细节，后续演进会受限

### 11.2 控制方式

- 固定 `storage/form/view` 分区，不允许平铺混写
- 通过 JSON schema 与语义校验器阻止越界字段
- 强制 migration 作为数据库变更唯一执行路径
- 前端通过受控 DTO 或 schema API 消费，而不是耦合底层文件细节

### 11.3 回滚思路

- 某模块若发现元数据 authoring 不稳定，可退回到该模块原有 C# 定义方式
- 若某次 migration 草案错误，通过常规 migration 回滚，而不是修改运行时元数据
- 若 `form/view` 设计未成熟，可先只启用 `storage` 分区，不影响数据库模型主链路

## 12. 验证方案

最小验证至少应覆盖：

- 元数据文件结构校验测试
- `storage` -> `ModelDefinition` 投影测试
- `form/view/uiType` 字段引用一致性测试
- EF Core migration 生成测试
- 定向模块测试，例如 `ConfigCenterModelDefinitionTests` 风格测试扩展

推荐最小验证命令方向：

- `dotnet build`
- `dotnet test` 对应模块与建模测试
- migration 生成 smoke

本设计正式落地时，应补充一份单独验证文档，记录：

- 样本模型
- 元数据变更前后差异
- 生成的 migration 摘要
- 回滚验证结果

## 13. 未决问题

- `storage-types.json` 中首轮平台类型枚举的最终集合应收敛为哪些值
- `Link` 的 `linkedTypes` 在后续是否需要扩展更多静态属性，例如是否允许隐藏某些组合
- `form/view/uiType/dataSource` 是否直接由前端读取文件，还是先统一经过后端 schema API 暴露
- `uiType` 的稳定键空间是否完全复用旧平台处理类型命名，还是定义新平台受控映射表

## 14. 实施拆分建议

1. 先冻结 JSON 契约与分区规则  
   最小交付：设计文档、示例文件、字段白名单/黑名单

2. 再实现 `storage` 分区的解析、校验与 `ModelDefinition` 投影  
   最小交付：单模块可从 JSON 投影出现有强类型模型

3. 再打通 migration 生成与回归测试  
   最小交付：样本模型变更可生成受控 migration

4. 最后补 `form/view/uiType` 消费链路  
   最小交付：一个样本模块可以按 create/edit/详情读取 schema，并与独立 `.view` 列表元数据协同

这样能保证第一阶段先解决“AI 更好地 authoring 结构模型”这个主问题，不把前端 schema 和复杂行为元数据一起捆绑进来。

## 15. AI-First 最小检查清单

1. 入口在哪里  
   模型 authoring 入口建议为 `server/modules/<ModuleId>/metadata/models/*.model.json`；后端实现入口为模型元数据解析、校验与投影适配层。

2. 边界在哪里  
   `storage` 负责数据库结构，`form` 负责创建/编辑，`view` 只负责详情页是否显示该属性，字段级 `uiType` 负责输入控件语义与固定显示值语义；列表列行为不在本设计范围内。

3. 如何验证  
   通过 JSON schema 校验、`ModelDefinition` 投影测试、migration 生成测试、前端 schema 引用一致性测试和最小 smoke 验证证明设计落地。

4. 失败信号是什么  
   若实现后出现平铺属性膨胀、`form/view/uiType` 反向影响数据库结构、运行时隐式改库，或必须靠人工约定才能解释字段语义，则说明该设计没有按预期落地。

5. 如何回滚  
   可以逐模块退回到原有 C# 定义方式；migration 问题通过常规迁移回滚处理；若仅 `form/view/uiType` 消费不成熟，可先停用 UI 分区，仅保留 `storage` 主链路。
