# 元数据配置中心详细设计

## 1. 背景

当前 `Setting / Dictionary / Enumeration` 已逐步明确新的长期承载方向：

- `Setting`：定义层仍属于模块元数据，值层由 `ConfigCenter` 承载
- `Dictionary`：模块内静态多语言资源为权威源
- `Enumeration`：模块内静态结构清单 + 多语言资源为权威源

这个方向解决了权威来源不清、结构与文案混杂、运行时承载与治理边界不清的问题，但也带来了一个新的现实问题：如果后续仍主要靠人工直接修改文件，开发、实施和运维人员在编辑、校验、热生效和协作上的成本会持续偏高。

本设计的目标，是在不改变“模块元数据文件仍为定义层权威源”的前提下，新增一个独立模块“元数据配置中心”，为模块元数据补一层受控的可视化编辑与热应用工作台。它既不把 `ConfigCenter` 重新拉回“数据库权威仓库”路线，也不把 Git 绑定为每次保存的强前置条件，而是把“编辑安全性、运行时快速生效、模块归属清晰”三件事同时稳定下来。

这份设计与 `Dictionary / Enumeration` 迁移准备设计、`ConfigCenter` 设计和当前插件化/组件化路线保持一致：元数据配置中心是独立模块，专门给开发、实施和运维人员使用；它只是新的输入与治理入口，不是新的架构中心。

## 2. 目标

- 为开发、实施、运维人员提供模块元数据的可视化编辑能力。
- 保持模块内元数据文件仍为定义层唯一权威源，并进入版本库管理。
- 将 `Setting` 明确定义为“定义层元数据 + 值层配置数据”的二层模型。
- 将元数据能力沉淀为独立模块“元数据配置中心”，而不是继续附着在 `ConfigCenter` 下演进。
- 将 `保存`、`应用`、`Git 同步` 三个动作显式分离。
- 支持手动触发热应用，尽量在不重启服务的情况下让元数据修改生效。
- 为后续元数据类型增加预留稳定接入机制，不破坏模块化和组件化边界。
- 让元数据配置中心能够按模块、按类型装配页面和能力，而不是依赖全局扫描和硬编码分支。

## 3. 非目标

- 不把元数据配置中心做成新的数据库权威源。
- 不要求保存后自动提交 Git。
- 不要求生产环境必须连接或同步任何代码仓库。
- 不在首版中实现复杂审批流、多人在线协同锁定或跨环境发布流程。
- 不要求首版覆盖所有元数据类型；首批只需服务 `Setting / Dictionary / Enumeration`。
- 不把运行时投影结果重新当成新的源数据入口。

## 4. 范围

本设计涉及：

- 独立模块：元数据配置中心
- 模块内元数据目录，例如：`modules/<ModuleId>/metadata/`
- 模块元数据接入声明：`modules/<ModuleId>/metadata/metadata.manifest.json`
- 元数据配置中心后端服务与接口
- 元数据配置中心前端工作台页面
- 模块级运行时投影与全局聚合视图
- `Setting / Dictionary / Enumeration` 首批类型 adapter、editor 与 validation profile

本设计不涉及：

- 旧平台 `Dictionary / Enumeration` 的具体转换脚本实现
- `Setting` 值层在 `ConfigCenter` 中的完整审批发布增强
- 复杂业务元数据类型（如 `.page`、`.view`、`.generator`）的首版页面细节
- 生产级审批、发布编排或运维审计中心
- 运行时消费侧所有细节 API 的一次性完备设计

## 5. 设计约束

- 模块内文件必须继续作为元数据定义层的唯一权威源，元数据配置中心不能形成第二套真相源。
- 元数据配置中心必须按模块组织元数据，不允许重新回到平台级“全局大杂烩”源目录。
- 元数据配置中心的保存动作只写文件，不自动应用，不自动提交 Git。
- 元数据配置中心的应用动作必须支持手动触发，并优先实现模块级增量热生效。
- Git 同步能力必须可按环境开关控制，生产环境默认可关闭。
- 平台支持哪些语言由全局配置决定，例如 `resources/localization/languages.json`，不由单个模块重复声明。
- 平台支持哪些元数据类型由平台注册表决定，单个模块只声明自己实际接入了哪些类型。
- `Setting` 的定义层与值层必须分离：定义层进入模块元数据，值层继续由 `ConfigCenter` 的 `ConfigItem` 承载。

### 5.1 AI-first 最小检查补充

- 入口在哪里：设计入口是本文；模块接入入口是 `modules/<ModuleId>/metadata/metadata.manifest.json`；元数据配置中心正式实现入口建议为独立模块；前端入口建议通过现有 Shell 模块注册机制接入。
- 边界在哪里：元数据配置中心只负责可视化编辑、校验、保存、应用和可选 Git 同步，不负责替代模块内源文件、不负责成为新的数据库权威仓库、不负责复杂审批流；`ConfigCenter` 继续负责 `Setting` 值的读写、覆盖、查询和管理页面。
- 如何验证：通过元数据配置中心定向后端测试、模块级投影测试、前端工作台交互测试，以及“保存后未应用不生效、应用后无需重启即可生效”的 smoke 验证。
- 失败信号是什么：若保存后仍依赖直接改运行时目录才能生效、若元数据配置中心不得不维护第二套数据库权威数据、若新类型接入需要反复修改平台主干硬编码分支，则说明本设计没有按预期落地。
- 如何回滚：关闭元数据配置中心入口，删除对应实现与模块接入声明增量，保留模块内权威文件与现有静态资源/静态清单主路径不变即可回退。

## 6. 总体方案

采用“模块内权威文件 + 元数据配置中心工作台 + 模块级热投影 + 可选 Git 同步”的方案：

1. 模块内元数据定义文件继续作为权威源。
2. 每个模块通过 `metadata.manifest.json` 显式声明自己的元数据接入信息。
3. 平台通过统一的元数据类型注册表声明已支持的类型、adapter、editor、validator 与 projection handler。
4. 元数据配置中心根据“平台注册表 + 模块 manifest”组装工作台页面、保存规则和应用规则。
5. 保存只回写模块文件；应用手动触发并执行模块级增量投影；Git 同步单独触发。
6. `Setting` 的定义由元数据配置中心维护；`Setting` 的值继续由 `ConfigCenter` 的 `ConfigItem` 模型承接，并负责读写、覆盖、查询与管理页面。

选择这个方案的原因：

- 能同时满足“文件仍为权威源”和“可视化、安全编辑”的诉求。
- 能保持模块归属清晰，避免所有元数据再次集中到平台级源目录。
- 能为未来元数据类型增加提供稳定接入面，而不是把工作台写成不断扩张的 if/else 集合。
- 能让开发和实施过程中的元数据修改尽量不依赖重启服务。
- 能把元数据定义层和配置值层的职责重新拉直，避免 `ConfigCenter` 边界继续膨胀。

本轮不选这些方案：

- 不选“元数据配置中心直接写数据库，数据库再反向生成文件”：会形成双权威源。
- 不选“保存即自动提交 Git”：会把编辑动作与协作动作强绑定，不符合不同环境和不同角色的实际使用习惯。
- 不选“只靠目录扫描猜模块支持什么类型”：隐式规则过多，不利于后续扩展、插件化和精细控制。
- 不选“保存即立即自动应用”：会降低操作可控性，也不利于实施/维护人员进行分步确认。

## 7. 关键模型

### 7.1 三层模型

1. 元数据定义层  
模块内元数据文件，进入版本库，是定义层唯一真相源。

2. 元数据配置中心  
提供可视化浏览、编辑、校验、保存、应用与可选 Git 同步能力。

3. 运行时承载层  
`Setting` 值由 `ConfigCenter` 承载；`Dictionary / Enumeration` 则投影为模块运行时快照和全局聚合视图，供前后端消费。

### 7.2 `Setting` 二层模型

`Setting` 在新平台中继续视为元数据，但不再把“定义”和“值”混成同一个承载层。

推荐拆分为：

- `SettingDefinition`：模块内声明，属于元数据定义层，进入版本库
- `ConfigItem`：运行时配置值，存储在 `ConfigCenter`
- `ConfigCenter`：负责值的读写、覆盖、查询与管理页面

这与老平台“静态 setting + 动态业务规则配置项”的思路一致，但在新平台中边界会更清晰：

- 静态定义归模块元数据
- 动态值归配置中心
- 元数据配置中心负责定义编辑
- `ConfigCenter` 负责值管理

### 7.3 模块接入声明模型

每个模块通过 `metadata.manifest.json` 接入元数据配置中心。manifest 的定位是“模块元数据接入契约”，而不是平台能力清单。

推荐结构示例：

```json
{
  "schemaVersion": "1.0",
  "moduleId": "Platform",
  "metadataRoot": "metadata",
  "types": {
    "settings": {
      "enabled": true,
      "sourcePath": "settings",
      "adapter": "SettingDefinitionAuthoringAdapter",
      "authoring": {
        "editable": true
      },
      "projection": {
        "enabled": false,
        "scope": "module-only",
        "applyMode": "incremental"
      },
      "validation": {
        "profile": "setting-definition-default",
        "allowWarningsOnSave": true,
        "requireFullValidationOnApply": false
      }
    },
    "dictionary": {
      "enabled": true,
      "sourcePath": "dictionary",
      "adapter": "DictionaryAuthoringAdapter",
      "authoring": {
        "editable": true
      },
      "projection": {
        "enabled": true,
        "scope": "module-and-global",
        "applyMode": "incremental"
      },
      "validation": {
        "profile": "dictionary-default",
        "allowWarningsOnSave": true,
        "requireFullValidationOnApply": true
      }
    },
    "enumerations": {
      "enabled": true,
      "sourcePath": "enumerations",
      "adapter": "EnumerationAuthoringAdapter",
      "authoring": {
        "editable": true
      },
      "projection": {
        "enabled": true,
        "scope": "module-and-global",
        "applyMode": "incremental"
      },
      "validation": {
        "profile": "enumeration-default",
        "allowWarningsOnSave": true,
        "requireFullValidationOnApply": true
      }
    }
  }
}
```

manifest 负责声明：

- 模块元数据根目录
- 模块实际启用的元数据类型
- 各类型的 `sourcePath`
- 各类型使用的 `adapter`
- 是否允许元数据配置中心编辑
- 是否允许应用到运行时
- 投影范围与应用模式
- 校验策略

manifest 不负责声明：

- 平台支持哪些元数据类型
- 平台支持哪些语言
- Git 环境策略
- 具体权限角色配置

### 7.4 平台元数据类型注册表

平台注册表负责声明平台能力，包括：

- 已支持的元数据类型
- 默认 adapter
- 默认 editor
- 默认 validation profile
- 默认 projection handler

平台能力与模块接入的装配关系为：

`平台注册表 + 模块 manifest = 元数据配置中心运行配置`

### 7.5 元数据配置中心编辑模型

页面编辑模型不应直接等于原始文件结构，而应以“更适合人编辑”为目标。

`Setting` 推荐使用“定义表单 + 默认值预览 + ConfigCenter 值入口链接”的编辑模型：

```json
{
  "moduleId": "Platform",
  "type": "settings",
  "group": "CompanyRuleSetting",
  "definitions": [
    {
      "key": "CompanyRule.AutoCode",
      "displayName": "自动编码规则",
      "description": "公司级自动编码开关",
      "valueType": "string",
      "defaultValue": "Enabled",
      "isSensitive": false,
      "supportsOverride": true
    }
  ]
}
```

`Dictionary` 推荐使用按 key 聚合的多语言表格模型：

```json
{
  "moduleId": "Platform",
  "type": "dictionary",
  "context": "Common",
  "entries": [
    {
      "key": "Common.OK",
      "values": {
        "zhs": "确定",
        "en": "OK"
      },
      "status": {
        "hasWarning": false,
        "isDirty": false
      }
    }
  ]
}
```

`Enumeration` 推荐使用“结构 + 文案”联动模型：

```json
{
  "moduleId": "Platform",
  "type": "enumerations",
  "name": "Gender",
  "items": [
    {
      "name": "Female",
      "displayValues": {
        "zhs": "女",
        "en": "Female"
      },
      "status": {
        "missingLanguages": [],
        "orphanTranslation": false,
        "isDirty": false
      }
    }
  ]
}
```

### 7.6 文件包与投影包

元数据配置中心保存和应用不应只处理“单个 JSON 字符串”，而应面向文件包和投影包：

- `MetadataSourceBundle`：adapter 根据编辑模型生成的待回写文件集合
- `RuntimeProjectionBundle`：adapter 根据权威文件生成的运行时投影结果集合

这样 `Enumeration` 这类需要同时操作结构文件与多语言文件的类型，才能保持统一保存与统一应用。

## 8. 核心流程

### 8.1 主流程：模块发现与页面装配

1. 平台加载元数据类型注册表。
2. 元数据配置中心扫描模块目录并读取 `metadata.manifest.json`。
3. 对每个启用的 `type`，根据 manifest 和注册表解析对应 adapter、editor、validation profile 与 projection handler。
4. 元数据配置中心按模块展示“可编辑元数据类型列表”，而不是按平台全局平铺所有元数据文件。

### 8.2 主流程：读取编辑模型

1. 前端请求某模块某元数据类型的编辑页面。
2. 元数据配置中心读取模块 manifest。
3. 根据 `adapter` 定位对应类型适配器。
4. 适配器读取模块内权威文件并转换为页面编辑模型。
5. 页面加载结构化编辑视图，而不是直接显示裸 JSON。

### 8.3 主流程：保存

1. 用户在页面完成编辑后点击 `保存`。
2. 元数据配置中心根据 manifest 和 adapter 对编辑模型执行类型语义校验。
3. 校验通过后，由 adapter 生成 `MetadataSourceBundle`。
4. `ModuleMetadataStore` 将文件包原子写回模块内权威目录。
5. 返回保存结果、版本摘要、warning 列表以及“当前已保存未应用”的状态。

保存动作不做的事情：

- 不自动应用
- 不自动提交 Git
- 不直接修改运行时投影目录

### 8.4 主流程：应用

1. 用户手动点击 `应用`。
2. 元数据配置中心重新读取模块内最新文件，避免基于旧页面缓存直接投影。
3. 若 `requireFullValidationOnApply = true`，执行完整校验。
4. adapter 生成 `RuntimeProjectionBundle`。
5. `RuntimeProjectionService` 按 manifest 声明的 `scope` 和 `applyMode` 执行模块级增量投影。
6. 原子替换模块运行时快照；若需要全局可见，再同步更新全局聚合视图。
7. 刷新缓存、注册表或消费侧快照。
8. 返回应用结果与影响摘要。

### 8.5 主流程：Git 同步

1. 用户手动点击 `Git 同步`。
2. 平台先判断当前环境是否允许暴露 Git 同步能力。
3. 若允许，则只展示当前模块元数据目录对应的差异范围。
4. 用户选择执行 `提交` 或 `推送`。
5. Git 服务执行实际同步动作并返回结果。

Git 同步不作为保存或应用的前提条件。

### 8.6 异常流程

- 页面保存时发现文件已被其他操作修改：拒绝覆盖并提示冲突。
- 保存通过但应用失败：保留模块内最新权威文件，不回滚保存结果；运行时继续使用上一个可用快照，并返回应用失败信息。
- 某类型未启用 `projection.enabled`：允许保存，不允许应用。
- 某环境禁用 Git 同步：工作台不显示或不开放对应入口。

## 9. 接口与集成点

### 9.1 后端接口分类

元数据配置中心后端建议至少提供以下接口族：

- 模块发现接口
- 编辑模型读取接口
- 预校验接口
- 保存接口
- 应用接口
- Git 同步接口

接口设计重点：

- 面向“模块 + 类型 + 编辑模型”，而不是面向裸文件路径
- 面向结构化 diff、warning 和状态结果，而不是只返回成功/失败

### 9.2 前端工作台集成点

前端建议作为独立平台管理模块接入 Shell，并提供：

- 模块树
- 模块下的元数据类型列表
- 类型化编辑器
- `保存 / 应用 / Git 同步` 顶部操作区
- warning、validation、应用结果与最近操作状态展示区

当前 foundation 首版还补充了一条运行时接线约束：

- `metadata-config-center` 当前以前端 host-managed 模块方式随 Shell 一起注册。
- 当宿主存在运行时 frontend manifest，但尚未安装对应统一插件包时，页面入口仍应保持可访问。
- 这条 host-managed 接线只用于保证 foundation 阶段的宿主可用性，不等于它已经进入统一插件包分发主路径。

### 9.3 与模块系统的集成

- 模块通过 `metadata.manifest.json` 声明自己的元数据接入信息。
- 模块打包、安装、启用、禁用、回滚时，应将 manifest 与模块元数据目录一并视为模块资产。
- 元数据配置中心不得绕过模块边界去直接编辑其他模块的权威文件目录。

### 9.4 与运行时消费层的集成

- 运行时消费层优先读取模块运行时快照或全局聚合视图，而不是直接读取元数据配置中心页面状态。
- `Dictionary / Enumeration` 的消费端应被设计为依赖投影结果，而不是反向依赖元数据配置中心实现细节。
- `Setting` 的运行时值消费应继续经过 `ConfigCenter`，而不是直接读取 `SettingDefinition` 文件。

## 10. 数据与状态

### 10.1 模块内权威目录建议

推荐结构如下：

```text
modules/<ModuleId>/metadata/
  metadata.manifest.json
  settings/
  dictionary/
  enumerations/
  localization/
```

后续如需细化，可继续扩展为：

```text
modules/<ModuleId>/metadata/
  metadata.manifest.json
  settings/
    CompanyRuleSetting.json
  dictionary/
    Common.json
  enumerations/
    Gender.json
  localization/
    zhs/
      dictionary/
      enumerations/
    en/
      dictionary/
      enumerations/
```

### 10.2 运行时投影目录建议

推荐同时保留两层：

```text
runtime/metadata/modules/<ModuleId>/...
runtime/metadata/global/...
```

保留这两层的原因：

- `modules/` 视图便于模块级热更新、诊断与回滚
- `global/` 视图便于运行时统一消费

### 10.3 状态模型

页面和后端建议统一使用以下状态标签：

- `未保存`
- `已保存未应用`
- `已应用`
- `Git 未同步`

状态线必须分离：

- 文件状态线：保存、应用
- 协作状态线：Git 同步

## 11. 错误处理与风险

### 11.1 主要风险

- 若元数据配置中心内部偷偷持有另一份数据库态元数据，会重新形成双权威源。
- 若新类型接入必须反复改平台主干代码，后续类型增长时复杂度会失控。
- 若应用流程无法保证原子替换和失败回退，运行时会出现半生效状态。
- 若保存与应用强绑定，会降低实施与运维操作的可控性。
- 若 Git 同步能力不按环境收口，生产环境可能被不必要地绑定到代码仓库流程。

### 11.2 控制方式

- 强制模块内文件作为唯一权威源。
- 使用 `manifest + 注册表 + adapter` 作为唯一扩展机制。
- 应用流程要求基于最新文件重新校验与重新投影。
- 运行时投影层必须支持失败时保留上一版可用快照。
- Git 同步按环境策略控制入口是否开放。

### 11.3 回滚思路

- 若元数据配置中心实现本身不满足预期，可关闭模块入口并回退对应实现，不影响模块权威文件结构。
- 若某次应用失败，运行时继续保留上一版可用投影快照。
- 若某次保存写入错误，按文件版本和版本库记录执行常规文件回退。

## 12. 验证方案

需要至少覆盖以下验证：

- manifest 解析与平台注册表装配测试
- `SettingDefinitionAuthoringAdapter` 定向测试
- `DictionaryAuthoringAdapter` 定向测试
- `EnumerationAuthoringAdapter` 定向测试
- `ModuleMetadataStore` 原子写入与并发冲突测试
- `RuntimeProjectionService` 模块级增量应用测试
- 前端工作台保存/应用状态流转测试
- “保存后未应用不生效、应用后无需重启即可生效”的 smoke 验证

推荐最小验证目标：

- `SettingDefinition` 修改后，定义文件保存成功；对应默认值同步与 `ConfigCenter` 值管理路径保持边界清晰。
- `Dictionary` 的某个 key 修改后，保存成功但运行时仍保持旧值；执行应用后，运行时读取到新值。
- `Enumeration` 的某个枚举项文案修改后，结构与文案仍保持一致；执行应用后，消费端读取到新显示值。
- 生产环境下工作台不暴露 Git 同步能力。

foundation 首版额外验证重点：

- 当 `Persistence__AutoMigrate=false` 时，宿主仍可完成 `SettingDefinition` 的读取与保存 smoke，因为定义层 authoring 走的是模块文件存储链路，而不是 `ConfigCenter` 值层数据库链路。
- 当运行时 frontend manifest 存在但未包含 `metadata-config-center` 时，Shell 仍会把该模块按 host-managed 入口注册出来。

## 13. 未决问题

- 模块元数据编辑权限如何与现有权限体系对接。
- 应用结果是否需要追加最小审计记录，若需要，应与 `Audit` 如何协同。
- `SettingDefinition` 修改后，是否需要显式触发默认值同步入口，还是在独立工作流中完成。
- 后续新增复杂元数据类型时，是否需要逐步引入更细粒度的 impact analysis 与 diff 能力。

## 14. 实施拆分建议

1. 先固化平台注册表、模块 manifest 契约和模块内权威目录约定。
2. 再实现独立的元数据配置中心模块、模块文件存储和运行时投影主链路。
3. 首批落 `Setting / Dictionary / Enumeration` 三个 adapter 与前端工作台页面。
4. 完成 `保存 / 应用 / Git 同步` 三动作分离与环境开关策略。
5. 最后再为更多元数据类型补 editor、adapter、validation profile 与 projection handler。

每一步的最小交付应保持可独立验证，避免一开始就把所有类型和所有治理能力都压进同一轮实施。

## 15. AI-First 最小检查清单

1. 入口在哪里  
   设计入口是本文；模块接入入口是 `modules/<ModuleId>/metadata/metadata.manifest.json`；实现入口建议为独立的元数据配置中心后端模块与前端工作台模块。

2. 边界在哪里  
   元数据配置中心只负责可视化编辑、校验、保存、应用和可选 Git 同步；模块内文件仍是定义层权威源；运行时投影仍是消费视图，不回流为权威源；`ConfigCenter` 继续负责 `Setting` 值层。

3. 如何验证  
   通过 manifest 装配测试、adapter 测试、运行时投影测试、前端工作台状态流转测试以及热生效 smoke 验证证明设计落地。

4. 失败信号是什么  
   若实现后仍需直接手改运行时目录、若元数据配置中心形成第二套数据库权威源、若新类型接入持续依赖平台主干硬编码，则说明实现偏离本设计。

5. 如何回滚  
   关闭元数据配置中心入口并回退对应实现增量即可；模块内权威文件路径与静态资源/静态清单主路线保持不变，运行时应用失败时保留上一版可用投影快照。
