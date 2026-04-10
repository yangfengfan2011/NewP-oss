# Dictionary 与 Enumeration 迁移准备设计

## 1. 背景

Phase 5 已完成 `.setting` 文件作为首个旧资产迁移 POC，验证了"样本冻结 → 映射规则 → 导入路径 → 验证记录"的迁移模板。根据资产处置矩阵（`docs/migration/oldp-asset-disposition-v1.md`），`Dictionary` 和 `Enumeration` 是下一波推荐的 Import 目标。

当前路线设计（`docs/designs/post-phase-6-governance-and-ai-first-roadmap-design.md`）将 WP4 定位为"最小迁移连续性"：不扩大战线，只维持迁移准备度，确保治理主线收口后可立即启动实施。

本设计覆盖 `Dictionary` 和 `Enumeration` 两类资产的样本冻结、映射规则草案、执行路径草案、验证方案草案和正式实施准入条件。

## 2. 目标

- 冻结 `Dictionary` 和 `Enumeration` 各自的首批迁移样本。
- 明确两类资产从旧平台到新平台的字段映射规则草案。
- 明确导入执行路径草案（API 入口、请求格式、执行方式）。
- 明确验证方案草案（如何确认导入结果正确）。
- 定义正式实施的准入条件，避免在治理收口前提前进入代码实现。

## 3. 非目标

- 不在本设计中编写导入代码或新增 API 端点。
- 不处理 `.page`、`.view`、`.generator` 等高复杂元数据资产。
- 不设计完整的多语言国际化框架。
- 不处理旧平台数据库中的动态枚举（DB Enumeration）迁移——本轮只覆盖静态 `.enum` 文件和 `.dic` 文件。
- 不启动正式实施，只完成准备阶段交付。

## 4. 范围

本设计涉及的旧平台资产：

- `OldP/Runtime/1_Server/Base/Config/Dictionary/*.dic`
- `OldP/Runtime/1_Server/Platform/Config/Dictionary/*.dic`
- `OldP/Runtime/1_Server/Platform/Config/Enumeration/*.enum`
- `OldP/Runtime/1_Server/Platform/Config/Enumeration/*.dic`

本设计涉及的新平台目标模块：

- `server/modules/ConfigCenter`（配置项承载）

## 5. 设计约束

- 遵循 Phase 5 形成的迁移模板："样本冻结 → 规则冻结 → 执行冻结 → 验证冻结"。
- 导入路径通过受控 API 执行，不使用自动扫描或批量注入。
- 导入结果应可通过现有 `ConfigCenter` 查询 API 验证。
- 不改变 `ConfigCenter` 现有模型定义，如果需要扩展模型应在实施阶段单独设计。
- 迁移规则遵循"尽量保持旧资产语义，只在明显不合理时受控升级"的原则。

## 6. Dictionary 资产

### 6.1 旧平台结构

Dictionary 文件（`.dic`）为 XML 格式，结构如下：

```xml
<DictionarySerializer>
  <Context Name="ContextName" IsWeb="true">
    <Label Name="LabelKey">
      <LocalizedLabel LanguageRef="zhs">中文文本</LocalizedLabel>
      <LocalizedLabel LanguageRef="en">English text</LocalizedLabel>
    </Label>
  </Context>
</DictionarySerializer>
```

核心字段：

- `Context.Name`：逻辑分组名称
- `Label.Name`：标签键
- `LocalizedLabel.LanguageRef`：语言代码（`zhs`、`en` 等）
- `LocalizedLabel` 内容：显示文本

文件分布：

- 基础层：`Runtime/1_Server/Base/Config/Dictionary/`（约 4 个文件）
- 平台层：`Runtime/1_Server/Platform/Config/Dictionary/`（约 16 个文件）

### 6.2 首批冻结样本

推荐从基础层选取结构最简单的文件作为首批样本：

| 样本文件 | 来源路径 | 选择理由 |
|----------|----------|----------|
| `Common.dic` | `Runtime/1_Server/Base/Config/Dictionary/Common.dic` | 公共标签，结构简单，不依赖业务上下文 |
| `Platform.dic` | `Runtime/1_Server/Platform/Config/Dictionary/Platform.dic` | 平台层核心标签，覆盖面广 |

首批样本范围限制：

- 只选 2 个文件，不一次性导入全部 20+ 个 `.dic` 文件。
- 样本内容在实施前需做一次人工审阅，确认无敏感数据。

### 6.3 映射规则草案

| 旧字段 | 新字段 | 映射规则 |
|--------|--------|----------|
| `Context.Name` | `ConfigItem.Group` | 格式为 `Dictionary/{ContextName}` |
| `Label.Name` | `ConfigItem.Key` | 格式为 `{ContextName}.{LabelName}` |
| `LocalizedLabel` 内容（`zhs`） | `ConfigItem.Value` | 取中文简体作为主值 |
| `LocalizedLabel` 内容（`en`） | `ConfigItem.Description` | 取英文作为描述 |
| 文件名 | `ConfigItem.DisplayName` | 使用 `LabelName` 作为显示名 |
| 固定值 `ConfigCenter` | `ConfigItem.ModuleId` | 与 Setting 导入一致 |

待实施阶段确认的问题：

- 多语言值是否只取 `zhs` 作为主值，还是需要扩展 `ConfigItem` 模型以支持多语言字段。如果需要扩展，应先补模型扩展设计。
- `IsWeb` 属性是否需要映射，当前草案建议忽略。

### 6.4 执行路径草案

推荐新增 API 端点：

- `POST /api/config/import/dictionary`

请求格式：

```json
{
  "moduleId": "ConfigCenter",
  "sourceFileName": "Common.dic",
  "xmlContent": "<DictionarySerializer>...</DictionarySerializer>"
}
```

执行流程：

1. 接收 `.dic` 文件的 XML 内容。
2. 解析 `Context` → `Label` → `LocalizedLabel` 结构。
3. 按映射规则生成 `ConfigItem` 集合。
4. 写入 `ConfigCenter` 仓储（同 Key 覆盖）。
5. 返回导入结果（成功/失败/跳过的明细）。

### 6.5 验证方案草案

- 导入后通过 `GET /api/config/{key}` 查询单个标签。
- 通过 `GET /api/config/grouped` 查询 `Dictionary/Common` 或 `Dictionary/Platform` 分组。
- 验证导入数量与源文件中的 `Label` 节点数一致。
- 验证中文值和英文描述映射正确。

## 7. Enumeration 资产

### 7.1 旧平台结构

#### 静态枚举（`.enum` 文件）

```xml
<root>
  <Enumeration Name="Gender">
    <EnumerationItem Name="Female" />
    <EnumerationItem Name="Male" />
  </Enumeration>
</root>
```

核心字段：

- `Enumeration.Name`：枚举名称
- `EnumerationItem.Name`：枚举项名称

#### 枚举本地化（`.dic` 文件）

枚举的显示文本存储在同目录的 `.dic` 文件中，命名规则为 `{EnumerationName}{EnumerationItemName}`：

```xml
<Context Name="Enumerations" IsWeb="true">
  <Label Name="GenderFemale">
    <LocalizedLabel LanguageRef="zhs">女</LocalizedLabel>
    <LocalizedLabel LanguageRef="en">Female</LocalizedLabel>
  </Label>
</Context>
```

文件分布：

- `Runtime/1_Server/Platform/Config/Enumeration/Enumerations.enum`
- `Runtime/1_Server/Platform/Config/Enumeration/Enumerations.dic`

### 7.2 首批冻结样本

| 样本文件 | 来源路径 | 选择理由 |
|----------|----------|----------|
| `Enumerations.enum` | `Runtime/1_Server/Platform/Config/Enumeration/Enumerations.enum` | 平台核心静态枚举定义 |
| `Enumerations.dic` | `Runtime/1_Server/Platform/Config/Enumeration/Enumerations.dic` | 对应本地化文本 |

首批样本范围限制：

- 只处理 `.enum` + 对应 `.dic` 的静态枚举。
- 数据库动态枚举（DB Enumeration）不在本轮范围。

### 7.3 映射规则草案

| 旧字段 | 新字段 | 映射规则 |
|--------|--------|----------|
| `Enumeration.Name` | `ConfigItem.Group` | 格式为 `Enumeration/{EnumerationName}` |
| `EnumerationItem.Name` | `ConfigItem.Key` | 格式为 `{EnumerationName}.{ItemName}` |
| `.dic` 中对应 `LocalizedLabel`（`zhs`） | `ConfigItem.Value` | 取中文简体显示文本 |
| `.dic` 中对应 `LocalizedLabel`（`en`） | `ConfigItem.Description` | 取英文作为描述 |
| `EnumerationItem.Name` | `ConfigItem.DisplayName` | 使用枚举项名称 |
| 固定值 `ConfigCenter` | `ConfigItem.ModuleId` | 与 Setting/Dictionary 导入一致 |

#### `.enum` 与 `.dic` 不完全对齐的处理规则

`.enum` 文件定义枚举项，`.dic` 文件提供本地化文本。两者不一定一一对应，需要写死以下处理规则：

| 场景 | 处理方式 | 理由 |
|------|----------|------|
| `.enum` 中有项，`.dic` 中无对应标签 | 正常导入。`Value` 使用 `ItemName` 作为 fallback，`Description` 留空。 | 枚举项定义是权威来源，缺翻译不阻塞导入。 |
| `.dic` 中有标签，`.enum` 中无对应项 | 跳过该标签，记录警告日志。 | `.enum` 是结构定义的权威来源，孤立的翻译标签不产生新的枚举项。 |
| `.dic` 中有 `zhs` 但无 `en` | 正常导入。`Value` 使用 `zhs` 文本，`Description` 留空。 | 中文文本是主值，英文描述为可选。 |
| `.dic` 中有 `en` 但无 `zhs` | 正常导入。`Value` 使用 `en` 文本作为 fallback，`Description` 使用同一 `en` 文本。 | 有翻译优于无翻译，即使主语言缺失也应尽量保留可读值。 |
| `.dic` 中有其他语言（非 `zhs`/`en`） | 忽略。首批只处理 `zhs` 和 `en` 两种语言。 | 控制首批范围，避免多语言扩展影响模型。 |
| 同一 `EnumerationItem` 在 `.dic` 中有多个匹配标签 | 使用精确匹配 `{EnumerationName}{ItemName}` 的标签。如果仍有多个，取第一个。 | 命名规则是约定唯一的，多个匹配说明源数据异常，取第一个并记录警告。 |

这些规则在实施阶段必须在导入代码中严格执行，对应的单元测试必须覆盖上述每个场景。

待实施阶段确认的问题：

- 枚举项是否需要保留排序信息（当前 `.enum` 文件中项的顺序隐含排序语义）。
- 枚举项之间的层级关系（`PEID`）是否需要在 `ConfigItem` 中表达，当前草案建议首批不处理层级。
- `ConfigItem` 模型是否需要增加"类型"标记以区分 Setting / Dictionary / Enumeration 来源，当前草案建议通过 `Group` 前缀区分。

### 7.4 执行路径草案

推荐新增 API 端点：

- `POST /api/config/import/enumeration`

请求格式：

```json
{
  "moduleId": "ConfigCenter",
  "enumSourceFileName": "Enumerations.enum",
  "enumXmlContent": "<root>...</root>",
  "dicSourceFileName": "Enumerations.dic",
  "dicXmlContent": "<DictionarySerializer>...</DictionarySerializer>"
}
```

执行流程：

1. 接收 `.enum` 文件和对应 `.dic` 文件的 XML 内容。
2. 解析 `.enum` 中的 `Enumeration` → `EnumerationItem` 结构。
3. 从 `.dic` 中查找 `{EnumerationName}{ItemName}` 对应的本地化文本。
4. 按映射规则生成 `ConfigItem` 集合。
5. 写入 `ConfigCenter` 仓储（同 Key 覆盖）。
6. 返回导入结果。

### 7.5 验证方案草案

- 导入后通过 `GET /api/config/grouped` 查询 `Enumeration/Gender` 等分组。
- 验证枚举项数量与源 `.enum` 文件中的 `EnumerationItem` 数一致。
- 验证本地化文本映射正确（如 `Gender.Female` → `女`）。
- 验证缺少本地化文本的枚举项以 `ItemName` 作为 fallback 值。
- 验证 `.dic` 中有标签但 `.enum` 中无对应项时，该标签被跳过且导入结果中包含对应警告。
- 验证同一 `EnumerationItem` 在 `.dic` 中有多个匹配标签时，取第一个且导入结果中包含对应警告。
- 验证仅有 `en` 而无 `zhs` 的枚举项，`Value` 使用 `en` 文本作为 fallback。

## 8. 正式实施准入条件

以下条件全部满足后，方可从准备阶段进入正式实施：

1. **WP1–WP3 收口**：平台治理、AI-first 标准和文档一致性工作包已完成或达到可接受状态。
2. **样本审阅**：首批冻结样本已经过人工审阅，确认无敏感数据、结构符合预期。
3. **模型兼容确认**：确认 `ConfigItem` 模型无需扩展即可承载 Dictionary 和 Enumeration 映射结果，或已有对应的模型扩展设计。
4. **多语言策略确认**：确认多语言值的处理方式（只取主语言 vs 扩展模型）。
5. **导入路径与 Setting POC 对齐**：确认新增 API 端点遵循 Phase 5 形成的导入模板，不引入新的特殊机制。

## 9. 风险

| 风险 | 影响 | 控制方式 |
|------|------|----------|
| `ConfigItem` 模型无法自然表达多语言值 | 映射规则需要调整，可能需先补模型扩展设计 | 在准入条件中要求先确认模型兼容性 |
| Dictionary 文件数量多（20+），首批范围可能被要求扩大 | 准备文档与实际实施范围不一致 | 严格限制首批只选 2 个文件，后续按波次扩展 |
| 枚举层级关系在平铺为 `ConfigItem` 时丢失语义 | 后续使用方无法还原层级结构 | 首批不处理层级，后续如有需求再补设计 |
| 旧 `.dic` 文件编码不统一 | 中文值在导入过程中出现乱码（Phase 5 已暴露类似风险） | 实施阶段需显式指定 UTF-8 编码处理 |

## 10. 回滚思路

- 准备阶段为纯文档，删除本设计文档即可回退。
- 正式实施阶段的导入可通过覆盖策略（同 Key 重新导入）或数据清理脚本回退。
- 具体回滚脚本应在实施阶段补充，不在准备阶段预先编写。

## 11. 与 Phase 5 迁移模板的关系

本设计遵循 Phase 5 在 Setting 导入 POC 中形成的迁移模板：

| 模板要素 | Setting POC | Dictionary（本设计） | Enumeration（本设计） |
|----------|-------------|---------------------|----------------------|
| 样本冻结 | `CompanyRuleSetting.setting` | `Common.dic`、`Platform.dic` | `Enumerations.enum` + `Enumerations.dic` |
| 映射规则 | Key → Key, Value → Value | Label → Key, LocalizedLabel → Value | EnumItem → Key, dic → Value |
| 执行路径 | `POST /api/config/import/setting` | `POST /api/config/import/dictionary` | `POST /api/config/import/enumeration` |
| 验证方式 | 按 Key 查询、按 Group 查询 | 按 Key 查询、按 Group 查询 | 按 Key 查询、按 Group 查询 |
| 覆盖策略 | 同 Key 覆盖 | 同 Key 覆盖 | 同 Key 覆盖 |
