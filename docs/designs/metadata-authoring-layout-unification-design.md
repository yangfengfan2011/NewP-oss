# 元数据 Authoring 布局统一设计

## 1. 背景

当前仓库中的权威元数据分散在多个位置：

- `modules/<ModuleId>/metadata/**`
- `server/modules/<ModuleId>/metadata/**`
- `resources/**`
- `server/platform-*/metadata/**`

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
- `server/platform-*/metadata/**` 只保留平台内核执行级元数据
- 取消模块清单 `metadata.manifest.json`
- 改为“固定模块根目录 + 文件自描述 `type` + 平台级类型注册表”的装配模型
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
- 平台根据 `type` 去类型注册表查找处理逻辑

这意味着：

- 目录用于表达 owner 和边界
- 文件内容用于表达类型
- 平台注册表用于表达装配关系

### 4.3 平台统一维护类型注册表

平台级类型注册表负责声明：

- 平台支持哪些元数据类型
- 每种类型对应的 adapter
- 每种类型对应的 validator / projection handler / editor key

业务模块不再负责声明这些内容。

### 4.4 保留 schemaVersion，但不长期背负历史版本

每个元数据文件保留 `schemaVersion`，用于识别文件结构版本。  
但平台不承诺永久支持所有旧版本，默认采用“有限期兼容 + 显式迁移”的策略。

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
- `dictionary/`
- `enumerations/`
- `localization/`

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
server/platform-*/metadata/
```

它们属于平台内核执行级元数据，不属于普通模块 authoring 数据，例如：

- `server/platform-modeling/metadata/storage-types.json`

### 5.5 废弃目录

`resources/**` 不再保留为元数据目录。  
其中仍属于权威元数据的内容必须迁移到：

- `modules/<ModuleId>/metadata/**`
- 或 `modules/Platform/metadata/**`

## 6. 元数据文件最小结构

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

## 8. 现有元数据与最终落点

| 类型 | 当前路径 | 最终路径 | 说明 |
| --- | --- | --- | --- |
| 设置定义 | `modules/ConfigCenter/metadata/settings/ConfigCenter.settings.json` | 保持在 `modules/ConfigCenter/metadata/**` | 已符合新口径 |
| 模型元数据 | `server/modules/ConfigCenter/metadata/models/ConfigItem.model.json` | `modules/ConfigCenter/metadata/**` | 需迁移 |
| 平台共享枚举结构 | `resources/enumerations/*.json` | `modules/Platform/metadata/**` | 需迁移 |
| 平台共享字典本地化 | `resources/localization/{lang}/dictionary/*.json` | `modules/Platform/metadata/**` | 需迁移 |
| 平台共享枚举本地化 | `resources/localization/{lang}/enumerations/*.json` | `modules/Platform/metadata/**` | 需迁移 |
| 语言配置 | `resources/localization/languages.json` | `modules/Platform/metadata/**` | 需迁移 |
| 平台类型映射 | `server/platform-modeling/metadata/storage-types.json` | 保持不变 | 作为内核执行级元数据保留 |

## 9. 迁移顺序

推荐顺序：

1. 先迁移 `ConfigCenter` 模型元数据到 `modules/ConfigCenter/metadata/**`
2. 建立 `modules/Platform/metadata/**`
3. 建立平台级类型注册表
4. 迁移平台共享 `enumerations`
5. 迁移平台共享 `localization`
6. 删除 `resources/**` 元数据入口

## 10. 验证要点

迁移完成后至少验证：

- 平台能扫描 `modules/*/metadata/**`
- 平台能从文件头读取 `schemaVersion` 与 `type`
- `ConfigCenter` 模型元数据能从新路径读取
- `Platform` 枚举与本地化元数据能从新路径读取
- 删除 `resources/**` 后不存在静默回退
- `storage-types.json` 仍只由内核侧读取

## 11. 最终结论

本设计正式收口为：

- 取消模块清单
- 统一权威源根目录为 `modules/<ModuleId>/metadata/**`
- 平台共享元数据统一放到 `modules/Platform/metadata/**`
- `resources/**` 完全退出
- 文件最小结构保留 `schemaVersion` 和 `type`
- 平台通过类型注册表统一绑定处理逻辑
- 平台不承诺长期保留所有历史 schema 版本支持

这套方案比“模块清单 + 固定类型目录”更轻，也更适合后续元数据类型持续增长时的维护。
