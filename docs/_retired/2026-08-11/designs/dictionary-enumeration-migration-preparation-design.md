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

- `resources/localization/languages.json`（启用语言与默认语言入口）
- `resources/localization/`（静态多语言资源，权威来源）
- `resources/enumerations/`（静态枚举结构清单，权威来源）
- `frontend/packages/core/`（后续最小 i18n 基座接入点）

## 5. 设计约束

- 遵循 Phase 5 形成的迁移模板："样本冻结 → 规则冻结 → 执行冻结 → 验证冻结"。
- 转换路径通过受控脚本执行，不使用自动扫描或批量注入。
- 转换结果应可通过静态资源目录、结构校验和最小 i18n smoke 验证。
- 启用语言集合必须由独立配置文件决定，而不是在脚本或模型里硬编码。
- 迁移规则遵循"尽量保持旧资产语义，只在明显不合理时受控升级"的原则。

### 5.1 AI-first 最小检查补充

- 入口在哪里：当前准备阶段的设计入口是本文档，当前协作入口是 `docs/current-status.md` 与 `docs/phases/post-phase-6-stage-map.md` 中的 `WP4 Stage 8`；正式实施的执行入口草案见 `6.4` 与 `7.4`。
- 边界在哪里：本设计只覆盖样本冻结、映射规则草案、执行路径草案、验证方案草案与正式实施准入条件，不包含导入代码实现、批量导入、DB Enumeration、多语言模型扩展和高复杂元数据迁移。
- 如何验证：准备阶段通过样本存在性检查、设计与协作文档核对、最小静态资源路径检查、`ConfigCenter` 定向测试、`validate-ai-first-design.ps1` 校验，以及后续独立的 Stage 8 准入复核记录来验证是否具备进入正式实施的条件。
- 失败信号是什么：若首批冻结样本无法在当前协作范围内复核、语言配置入口未冻结、多语言策略未明确、或新增转换路径无法与 Phase 5 Setting POC 模板对齐，则 Stage 8 结论必须为“暂不可进入实现”。
- 如何回滚：准备阶段的回滚方式见第 `10` 节；若本轮只新增 Stage 8 文档记录，则删除对应 Stage 8 文档补充即可回退到本设计原状。

### 5.2 Stage 8 当前阶段性结论

- `Dictionary` 与 `Enumeration` 的权威多语言数据不再以 `ConfigCenter + ConfigItem` 作为正式目标形态。
- 当前正式目标形态改为：
  - `Dictionary`：源码受控的静态 i18n 资源文件
  - `Enumeration`：源码受控的静态枚举结构清单 + 配套 i18n 资源文件
- `ConfigCenter` 保留其配置中心职责，但不再作为 `Dictionary / Enumeration` 权威多语言数据的主承载仓库。
- `ValueListItem` 保留其当前值列表职责，不作为本轮 `Dictionary / Enumeration` 首批导入目标。

### 5.3 多语言策略修正结论

- 不能把 `Dictionary / Enumeration` 的正式承载设计锁死为 `zhs/en` 双语。
- `zhs/en` 当前只能作为首批样本验证和映射讨论的观察切片，不能被当成长期数据模型的上限。
- 正式设计改为：多语言资源以任意 `LanguageRef` 为键，按静态资源文件存放，不再把 `Value / Description` 当作权威多语言存储。
- 运行时启用哪些语言，由 `resources/localization/languages.json` 决定；新增语言时，先改配置，再重跑转换。
- `resources/localization/languages.json` 的首批初始值参考 `D:/Github/ICP-X/OldP/Runtime/1_Server/Config/Apps.language`，当前冻结为 `zhs` 与 `en`，但配置结构不限制后续扩展。
- 当前推荐形式：
  - `Dictionary`：`resources/localization/<language>/dictionary/<context>.json`
  - `Enumeration`：`resources/enumerations/<enumeration-name>.json` + `resources/localization/<language>/enumerations/<enumeration-name>.json`
- `ConfigCenter` 如果未来仍需消费这些数据，应通过受控投影或只读适配层接入，而不是回到“数据库权威来源”模式。

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
| `Context.Name` | 资源命名空间 | 格式为 `dictionary/{ContextName}` |
| `Label.Name` | 资源键 | 格式为 `{ContextName}.{LabelName}` |
| `LocalizedLabel.LanguageRef` | 语言目录名 | 直接保留为 `<language>`；不再只限于 `zhs/en` |
| `LocalizedLabel` 内容 | 资源值 | 写入对应语言文件中的键值对 |

当前已知约束：

- `IsWeb` 属性首批建议忽略，不作为资源值的一部分
- 首批实现只要求 `Common.dic` 与 `Platform.dic` 成功转换为静态资源，不要求一次性接入全部 `.dic` 文件
- 若某个 `Label` 缺少部分语言，只为现有语言产出资源项，不人为补齐伪翻译

### 6.4 执行路径草案

推荐新增转换入口：

- `scripts/migration/dictionary-to-i18n.<ps1|py>`

执行流程：

1. 读取冻结的 `.dic` 文件内容。
2. 解析 `Context` → `Label` → `LocalizedLabel` 结构。
3. 按语言分组生成静态资源文件。
4. 输出到受控目录 `resources/localization/<language>/dictionary/`。
5. 仅为 `resources/localization/languages.json` 中已启用的语言输出资源；遇到未启用的 `LanguageRef` 时记录 warning。
6. 返回转换结果（成功/失败/跳过的明细）。

### 6.5 验证方案草案

- 转换后检查 `resources/localization/<language>/dictionary/` 下是否生成目标文件。
- 验证 `resources/localization/languages.json` 中的启用语言与生成目录一致。
- 验证生成键数量与源文件中的 `Label` 节点数量一致。
- 验证 `Common.SystemTitle`、`Common.OK` 等代表性键在 `zhs/en` 中值正确。
- 验证遇到未配置的新 `LanguageRef` 时，转换器会记录 warning，而不是静默丢弃或偷偷创建未知目录。

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
| `Enumeration.Name` | 枚举清单名 | 格式为 `<EnumerationName>` |
| `EnumerationItem.Name` | 枚举项键 | 直接写入静态枚举清单 |
| `.dic` 中 `LocalizedLabel.LanguageRef` | 语言目录名 | 直接保留为 `<language>`；不再只限于 `zhs/en` |
| `.dic` 中 `LocalizedLabel` 内容 | 枚举本地化资源值 | 写入对应语言文件中的 `{EnumerationName}.{ItemName}` |

#### `.enum` 与 `.dic` 不完全对齐的处理规则

`.enum` 文件定义枚举项，`.dic` 文件提供本地化文本。两者不一定一一对应，需要写死以下处理规则：

| 场景 | 处理方式 | 理由 |
|------|----------|------|
| `.enum` 中有项，`.dic` 中无对应标签 | 正常生成静态枚举结构；本地化资源缺失时由消费侧回退到 `ItemName`，并记录 warning。 | `.enum` 是结构定义的权威来源，缺翻译不阻塞结构落地。 |
| `.dic` 中有标签，`.enum` 中无对应项 | 跳过该标签，记录警告日志。 | `.enum` 是结构定义的权威来源，孤立的翻译标签不产生新的枚举项。 |
| `.dic` 中缺少默认语言，但有其他已启用语言 | 正常生成已有语言资源，并记录 warning。 | 配置文件决定启用语言集合，缺少默认语言不应阻塞转换。 |
| `.dic` 中出现未在 `languages.json` 启用的语言 | 跳过该语言，记录 warning；如需启用，先更新 `resources/localization/languages.json` 再重跑转换。 | 语言集合由配置文件显式决定，但模型不锁死为双语。 |
| 同一 `EnumerationItem` 在 `.dic` 中有多个匹配标签 | 使用精确匹配 `{EnumerationName}{ItemName}` 的标签。如果仍有多个，取第一个。 | 命名规则是约定唯一的，多个匹配说明源数据异常，取第一个并记录警告。 |

这些规则在实施阶段必须在导入代码中严格执行，对应的单元测试必须覆盖上述每个场景。

当前已知约束：

- 首批不保留排序信息；如后续确需显式排序，再单独开设计
- 首批不处理层级关系（`PEID`）
- 首批只要求静态枚举结构清单与本地化资源落地，不要求额外的数据库投影
- 如后续需要运行时查询 API，应由静态清单投影生成，而不是反过来把数据库当权威源

### 7.4 执行路径草案

推荐新增转换入口：

- `scripts/migration/enumeration-to-static-resources.<ps1|py>`

执行流程：

1. 读取冻结的 `.enum` 文件与对应 `.dic` 文件。
2. 解析 `.enum` 中的 `Enumeration` → `EnumerationItem` 结构。
3. 从 `.dic` 中查找 `{EnumerationName}{ItemName}` 对应的本地化文本。
4. 输出静态枚举结构清单到 `resources/enumerations/`。
5. 仅为 `resources/localization/languages.json` 中已启用的语言输出本地化资源到 `resources/localization/<language>/enumerations/`。
6. 对未启用语言记录 warning。
7. 返回转换结果。

### 7.5 验证方案草案

- 验证 `resources/enumerations/` 下生成的结构清单数量与源 `.enum` 中的 `Enumeration` 数一致。
- 验证 `resources/localization/languages.json` 中的启用语言与生成目录一致。
- 验证 `Gender`、`MessageState` 等代表性枚举的项数量与源文件一致。
- 验证 `resources/localization/<language>/enumerations/` 中的本地化键值正确。
- 验证 `.dic` 中有未启用的 `LanguageRef` 时，转换器会输出 warning。
- 验证 `.dic` 中有标签但 `.enum` 中无对应项时，该标签被跳过且结果中包含 warning。

## 8. 正式实施准入条件

以下条件全部满足后，方可从准备阶段进入正式实施：

1. **WP1–WP3 收口**：平台治理、AI-first 标准和文档一致性工作包已完成或达到可接受状态。
2. **样本审阅**：首批冻结样本已经过人工审阅，确认无敏感数据、结构符合预期。
3. **目标形态确认**：确认 `Dictionary` 与 `Enumeration` 的权威目标形态分别为静态资源与静态枚举清单，而不是继续默认落到数据库配置项。
4. **多语言策略确认**：确认正式资源模型可以表达任意 `LanguageRef`，而不是把承载模型锁死为 `zhs/en`。
5. **语言配置入口确认**：确认启用语言集合由 `resources/localization/languages.json` 决定，并已有首批冻结内容。
6. **执行路径与 Phase 5 模板对齐**：确认本轮仍遵循“样本冻结 → 规则冻结 → 执行冻结 → 验证冻结”，即使具体执行入口从 API 转成静态转换脚本，也不引入隐式扫描或不可复跑流程。

### 8.1 当前 Stage 8 收口结论

- 当前样本审阅、目标形态、多语言承载策略与执行路径都已形成明确方向。
- `WP4` 可进入正式实施准备阶段，但下一包应优先从 `Dictionary` 的静态资源转换与最小 i18n 基座开始，而不是回到数据库导入实现。
- 建议下一包改定义为：`Stage 9：Dictionary 静态资源转换与最小 i18n 基座`

## 9. 风险

| 风险 | 影响 | 控制方式 |
|------|------|----------|
| 当前前端尚无现成 i18n 基座 | Stage 9 需要同时落最小资源加载能力 | 将最小 i18n 基座明确纳入 Stage 9 范围，而不是在 Stage 8 继续空转 |
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
| 执行路径 | `POST /api/config/import/setting` | `scripts/migration/dictionary-to-i18n.<ps1|py>` | `scripts/migration/enumeration-to-static-resources.<ps1|py>` |
| 验证方式 | 按 Key 查询、按 Group 查询 | 静态资源目录检查、键数量核对、语言配置核对 | 静态清单检查、本地化资源核对、语言配置核对 |
| 覆盖策略 | 同 Key 覆盖 | 同 Key 覆盖 | 同 Key 覆盖 |
