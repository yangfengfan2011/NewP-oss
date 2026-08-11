# 元数据 Authoring 布局统一设计

## 1. 背景

当前仓库中的权威元数据分散在多个位置：

- `modules/<ModuleId>/metadata/**`
- `server/modules/<ModuleId>/metadata/**`
- `resources/**`
- `server/platform/metadata/**`

这会带来几个直接问题：

- 很难快速判断某份元数据的权威源在哪里
- 同一类元数据容易出现双真源或过渡状态长期固化
- AI 和开发者需要先理解多套目录口径，才能安全修改
- 平台共享元数据与模块私有元数据边界不清

本设计的目标是统一 authoring 真源布局，并明确元数据文件的最小结构与平台装配方式。

## 2. 目标

- 所有权威 authoring 元数据统一收口到 `modules/<ModuleId>/metadata/**`
- 平台共享元数据统一收口到 `modules/Platform/metadata/**`
- `resources/**` 不再保留为元数据目录
- `server/platform/metadata/**` 只保留平台内核执行级元数据
- 取消模块清单 `metadata.manifest.json`
- 改为“固定模块根目录 + 文件自描述 `type` + 平台文件类型映射”的装配模型
- 保留 `schemaVersion`，但不承诺长期保留所有历史版本运行支持

## 3. 非目标

- 不在本设计中重写所有元数据消费端逻辑
- 不在本设计中一次性引入新的元数据类型
- 不在本设计中把内核执行规则下沉到普通模块元数据文件
- 不保留长期双真源兼容路径

## 4. 核心结论

### 4.1 取消模块清单

不再保留 `metadata.manifest.json`。

原因：

- 每个模块默认支持所有元数据类型
- 元数据文件本身可自描述 `type`
- `adapter / validator / handler` 不应由业务模块声明
- 继续保留模块清单只会重复表达恒真信息，并增加额外维护面

### 4.2 固定根目录，类型由文件自描述

统一规则为：

- 每个模块的权威元数据根目录固定为 `modules/<ModuleId>/metadata/**`
- 平台共享元数据的权威根目录固定为 `modules/Platform/metadata/**`
- 平台递归扫描该目录下的元数据文件
- 文件自身通过 `type` 声明类型
- 平台根据 `type` 去平台文件类型映射查找处理逻辑

这意味着：

- 目录用于表达 owner 和边界
- 文件内容用于表达类型
- 平台文件类型映射用于表达装配关系

### 4.3 平台统一维护文件类型映射

平台文件类型映射负责声明：

- 平台支持哪些元数据类型
- 每种类型对应的 adapter
- 每种类型对应的 validator / projection handler / editor key

业务模块不再负责声明这些内容。

### 4.4 保留 schemaVersion，但不长期背负历史版本

每个元数据文件保留 `schemaVersion`，用于识别文件结构版本。  
但平台不承诺永久支持所有旧版本，默认采用“有限期兼容 + 显式迁移”的策略。

### 4.5 覆盖与失败必须显式暴露

本次统一目录不是把“谁生效”继续交给文件系统扫描顺序决定，而是要求平台内核对覆盖顺序、类型特例和异常做显式判定。

最低要求如下：

- 模块装配顺序决定覆盖优先级，元数据目录扫描顺序必须与模块装配顺序一致
- 平台默认只提供覆盖，不提供通用递归合并
- 只有被平台显式声明为特例的类型，才允许使用专属合并规则
- 文件 `type` 未注册时，必须报出未知类型错误，不允许跳过后继续假装成功
- `schemaVersion` 不受支持时，必须报出版本不兼容错误，并提示迁移，而不是回退到猜测逻辑
- 文件名与文件内容中的业务根对象标识不一致时，必须直接失败

这条规则用于落实仓库中的“禁止依赖隐式覆盖、复制顺序、约定俗成决定最终有效状态”约束。

## 5. 统一目录规范

### 5.1 模块级权威目录

所有模块级权威元数据统一放入：

```text
modules/<ModuleId>/metadata/
```

平台共享元数据统一放入：

```text
modules/Platform/metadata/
```

### 5.2 目录只表达归属，不强制承担类型判定

在 `metadata/` 根目录下：

- 可以按类型分目录
- 也可以按领域分组
- 但平台语义以文件中的 `type` 为准，而不是以目录名为准

因此以下两种都允许：

```text
modules/ConfigCenter/metadata/models/ConfigItem.model.json
```

```text
modules/ConfigCenter/metadata/domain/ConfigItem.model.json
```

前提是文件本身能声明自己的 `type`。

### 5.3 类型增多时的演进规则

当前阶段类型较少，可以允许按少量一级目录组织，例如：

- `models/`
- `settings/`
- `enumerations/`
- `dictionaries/`

当类型继续增长时，可以升级为分组目录模式，例如：

```text
modules/<ModuleId>/metadata/
  domain/
  reference/
  presentation/
  integration/
```

但无论是否分组，下面两条始终不变：

- 唯一权威根目录仍然是 `modules/<ModuleId>/metadata/**`
- 类型判定仍然以文件中的 `type` 为准

### 5.4 平台内核执行级元数据目录

以下内容继续保留在：

```text
server/platform/metadata/
```

它们属于平台内核执行级元数据，不属于普通模块 authoring 数据。

这类目录的约束是：

- 只承载平台内核运行时直接消费的执行级元数据
- 不参与普通模块 authoring 覆盖规则
- 不允许被业务模块通过普通模块元数据覆盖
- 变更时必须同步说明内核消费入口、验证方式与失败信号

当前阶段至少包括：

- `server/platform/metadata/file-type-mappings.json`

### 5.5 平台内核文件类型映射

平台需要保留一份独立于普通业务 authoring 数据之外的文件类型映射，用于描述“某类 authoring 文件应接到哪条平台处理链”。

该映射属于内核执行级元数据，而不是业务模块可自由 authoring 的普通元数据。  
它的职责是：

- 声明平台支持哪些 `type`
- 为每种 `type` 指向稳定的 adapter / validator / handler 组合
- 为类型特例声明是否允许专属合并语义
- 为宿主、工具和校验链提供统一的静态入口

该映射不负责：

- 决定模块装配顺序
- 让业务模块自行声明覆盖优先级
- 把执行逻辑下沉成可随意编辑的业务元数据

当前阶段建议最小字段如下：

- `adapter`：该类型进入 authoring 处理链时对应的稳定适配器标识
- `mergeMode`：该类型在发生冲突时的内核收敛策略

其中 `mergeMode` 当前只允许以下两种值：

- `replace`：默认模式，表示同类型、同逻辑对象标识冲突时，后者整对象覆盖前者
- `dic-entry`：仅用于 `dic`，表示先按词条名合并，再对同词条执行整组多语言覆盖

约束：

- `replace` 是默认模式
- `dic-entry` 仅允许用于 `dic`
- 当前阶段不得新增其他 `mergeMode` 取值
- 若未来需要新增取值，必须先补充新的类型专属设计、验证方式与失败信号

建议最小示意如下：

```json
{
  "schemaVersion": "1.0",
  "mappings": {
    "settings": {
      "adapter": "SettingAuthoringAdapter",
      "mergeMode": "replace"
    },
    "enum": {
      "adapter": "EnumerationAuthoringAdapter",
      "mergeMode": "replace"
    },
    "model": {
      "adapter": "ModelAuthoringAdapter",
      "mergeMode": "replace"
    },
    "dic": {
      "adapter": "DictionaryAuthoringAdapter",
      "mergeMode": "dic-entry"
    }
  }
}
```

### 5.6 废弃目录

`resources/**` 不再保留为元数据目录。  
其中仍属于权威元数据的内容必须迁移到：

- `modules/<ModuleId>/metadata/**`
- 或 `modules/Platform/metadata/**`

## 6. 元数据文件最小结构与最小示例

每个权威元数据文件至少应包含：

```json
{
  "schemaVersion": "1.0",
  "type": "..."
}
```

说明：

- `schemaVersion`：必须保留，用于识别文件结构版本
- `type`：必须保留，用于让平台识别元数据类型

`moduleId` 不再作为强制字段。  
默认情况下，模块归属由文件路径 `modules/<ModuleId>/metadata/**` 推断。

### 6.1 `settings` 最小示例

```json
{
  "schemaVersion": "1.0",
  "type": "settings",
  "settings": {
    "ConfigCenter.CacheEnabled": {
      "value": "true",
      "description": "是否启用配置项的内存缓存"
    }
  }
}
```

说明：

- `settings` 使用对象映射结构
- `settings` 下的属性名就是 setting 标识
- setting 项最小字段为 `value`、`description`

### 6.2 `enum` 最小示例

```json
{
  "schemaVersion": "1.0",
  "type": "enum",
  "Gender": {
    "items": [
      {
        "name": "Female"
      },
      {
        "name": "Male"
      }
    ]
  }
}
```

说明：

- `enum` 采用整对象覆盖
- 文件除元数据头外只允许一个业务根对象
- 业务根对象名应与文件名一致

### 6.3 `model` 最小示例

```json
{
  "schemaVersion": "1.0",
  "type": "model",
  "ConfigItem": {
    "kind": "Entity",
    "fields": [
      {
        "name": "Value",
        "storage": {
          "type": "text"
        }
      }
    ]
  }
}
```

说明：

- `model` 采用整对象覆盖
- 文件除元数据头外只允许一个业务根对象
- 业务根对象名应与文件名一致
- 模型相关多语言不内嵌在 `model` 文件中
- 设计模型时，必须同步修改其对应的 `dic` 文件
- 模型多语言 key 延续既有约定，由对应 `dic` 文件承载并校验完整性

### 6.4 `dic` 最小示例

```json
{
  "schemaVersion": "1.0",
  "type": "dic",
  "Common": {
    "Add": {
      "en": "Add",
      "zhs": "添加"
    }
  }
}
```

说明：

- `dic` 除元数据头外只允许一个业务根对象
- 业务根对象名应与文件名一致
- 每个词条必须提供全部受支持语言
- 同词条冲突时，后者整组多语言覆盖前者

## 7. 版本兼容策略

版本策略收口为：

1. 默认只存在一个当前正式版本，例如 `1.0`
2. 只有文件结构规则变化时才升版
3. 升版后优先提供迁移工具或升级脚本
4. 平台只做有限期兼容，不承诺永久支持旧版本
5. 支持哪些旧版本必须显式声明，不能靠隐式猜测

因此：

- 保留 `schemaVersion`
- 不保留无限期历史兼容负担

## 8. 覆盖判定与失败策略

### 8.1 覆盖判定原则

平台在扫描 `modules/*/metadata/**` 后，必须先按模块装配顺序形成稳定输入，再进入后续 adapter / validator / handler 流程。

最小判定规则如下：

1. 文件路径只能决定归属模块，不能决定优先级
2. 模块装配顺序决定覆盖优先级；目录扫描顺序只是该顺序的实现，不得形成额外语义
3. 文件中的 `type` 只能决定处理链路，不能自动授予额外覆盖权
4. 平台默认规则是：同类型、同逻辑对象标识时，后者整对象覆盖前者
5. 只有平台文件类型映射显式声明的特例类型，才允许使用专属合并规则

### 8.2 类型粒度与特例

当前阶段各类型按以下粒度判定：

- `settings`：使用对象映射结构，`settings` 下的属性名就是 setting 标识；同一标识冲突时整项覆盖
- `enum`：整对象覆盖
- `model`：整对象覆盖
- `dic`：允许词条级合并；同一词条冲突时，后者整组多语言覆盖前者

其中：

- 平台不提供通用对象递归合并
- 平台不提供数组元素级合并
- `dic` 是显式特例，其合并边界只到词条级，不下沉到语言级
- 对应到文件类型映射时，`settings / enum / model` 使用 `mergeMode = replace`，`dic` 使用 `mergeMode = dic-entry`

### 8.3 `dic` 类型专属规则

`dic` 的设计目标是让词条可以按对象级增量收口，同时避免语言级局部覆盖造成半成品状态。

专属规则如下：

1. 一个 `dic` 对象对应一个业务根对象，例如 `Common`
2. 文件除 `schemaVersion`、`type` 等元数据头外，只允许一个业务根对象
3. 同一业务根对象内，词条按词条名合并
4. 同一词条冲突时，后者必须用完整多语言集合整体覆盖前者
5. 不允许只覆盖单一语言
6. 每个词条必须提供全部受支持语言
7. 受支持语言集合来自全局唯一语言配置
8. 出现缺失语言或未知语言时直接失败
9. 当前阶段只支持新增与覆盖，不支持删除词条

### 8.4 失败信号

以下情况必须直接失败，并输出可定位到文件的错误：

- 缺少 `schemaVersion`
- 缺少 `type`
- `type` 未在平台文件类型映射中声明
- `schemaVersion` 不在当前支持范围内
- 文件内容无法通过该类型的基础结构校验
- 文件名与文件内容中的业务根对象标识不一致
- `dic` 词条缺少受支持语言或包含未知语言
- 运行时发现未声明特例类型却试图触发合并语义

失败时的要求：

- 错误信息必须至少包含模块、文件路径、失败类型、受影响标识
- 不允许以“跳过坏文件，其余继续生效”的方式制造部分成功假象
- 不允许回退到 `resources/**` 或旧 manifest 入口兜底

### 8.5 平台文件类型映射职责边界

平台文件类型映射只负责声明“平台认识什么类型，以及该类型接到哪条处理链”。

它不负责：

- 为业务模块声明可选能力
- 为某个模块配置特殊覆盖优先级
- 把覆盖顺序转移成文件系统顺序
- 把类型专属合并策略转移成随意 authoring 的元数据配置

冲突处理、版本校验、生效切换、回滚恢复仍然必须收口在代码内核。

## 9. 现有元数据与最终落点

| 类型 | 当前路径 | 最终路径 | 说明 |
| --- | --- | --- | --- |
| 设置定义 | `modules/ConfigCenter/metadata/settings/ConfigCenter.settings.json` | 保持在 `modules/ConfigCenter/metadata/**` | 已符合新口径 |
| 模型元数据 | `server/modules/ConfigCenter/metadata/models/ConfigItem.model.json` | `modules/ConfigCenter/metadata/**` | 需迁移，且模型变更时必须同步更新对应 `dic` 文件 |
| 平台共享枚举结构 | `resources/enumerations/*.json` | `modules/Platform/metadata/enumerations/*.json` | 迁移后按 `enum` 整对象覆盖 |
| 平台共享字典本地化 | `resources/localization/{lang}/dictionary/*.json` | `modules/Platform/metadata/dictionaries/*.dic.json` | 迁移后改为文件内承载完整多语言的 `dic` 结构 |
| 平台共享枚举本地化 | `resources/localization/{lang}/enumerations/*.json` | `modules/Platform/metadata/dictionaries/*.dic.json` | 迁移后改为文件内承载完整多语言的 `dic` 结构 |
| 语言配置 | `resources/localization/languages.json` | `modules/Platform/metadata/languages.json` | 作为全局唯一语言配置保留 |
| 平台文件类型映射 | 新增 | `server/platform/metadata/file-type-mappings.json` | 作为内核执行级元数据新增 |

## 10. 迁移顺序

推荐顺序：

1. 先迁移 `ConfigCenter` 模型元数据到 `modules/ConfigCenter/metadata/**`
2. 建立 `modules/Platform/metadata/**`
3. 建立 `server/platform/metadata/file-type-mappings.json`
4. 迁移平台共享 `enumerations` 为 `enum`
5. 迁移平台共享字典与枚举本地化为 `dic`
6. 删除 `resources/**` 元数据入口

## 11. 验证与回滚

### 11.1 验证要点

迁移完成后至少验证：

- 平台能扫描 `modules/*/metadata/**`
- 平台能从文件头读取 `schemaVersion` 与 `type`
- `ConfigCenter` 模型元数据能从新路径读取
- 模型变更对应的多语言 `dic` 文件已同步更新，且 key 完整可解析
- `Platform` 枚举元数据能以 `enum` 新结构从新路径读取
- `Platform` 字典与枚举本地化元数据能以 `dic` 新结构从新路径读取
- 删除 `resources/**` 后不存在静默回退
- `file-type-mappings.json` 仍只由内核侧读取
- 后端构建成功
- 相关后端测试成功
- 前端构建成功
- 相关前端测试成功
- 至少完成一次契约检查或 smoke 验证，证明扫描入口和消费入口在真实装配下能工作

### 11.2 回滚触发条件

出现以下任一情况，应停止继续迁移并进入回滚：

- 新扫描入口无法完整发现原有权威元数据
- 新平台文件类型映射导致既有类型无法装配
- 删除 `resources/**` 后前端或运行时出现无法恢复的缺失
- 目录迁移后出现冲突误判、漏判，导致有效结果与迁移前不一致
- 验证阶段无法在可接受成本内修复阻塞性失败

### 11.3 回滚原则

- 回滚以“恢复上一个已验证通过的权威源布局”为目标
- 回滚动作必须同时恢复代码入口、元数据文件位置和验证文档结论
- 未完成整批迁移验证前，不得删除仍被旧链路依赖的文件
- 回滚期间不得引入新的目录口径，避免形成第三套真源

### 11.4 回滚步骤

建议最小回滚步骤如下：

1. 恢复旧的扫描/读取入口代码
2. 将已迁移但尚未完成验证的元数据文件移回原路径
3. 恢复 `resources/**` 或旧 manifest 入口的引用
4. 重新执行迁移前已通过的构建、测试和 smoke 验证
5. 在验证文档中记录回滚触发原因、失败信号和后续修正项

## 12. 最终结论

本设计正式收口为：

- 取消模块清单
- 统一权威源根目录为 `modules/<ModuleId>/metadata/**`
- 平台共享元数据统一放到 `modules/Platform/metadata/**`
- `resources/**` 完全退出
- 文件最小结构保留 `schemaVersion` 和 `type`
- 平台通过文件类型映射统一绑定处理逻辑
- 平台不承诺长期保留所有历史 schema 版本支持

这套方案比“模块清单 + 固定类型目录”更轻，也更适合后续元数据类型持续增长时的维护。
