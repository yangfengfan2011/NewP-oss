# dic 与 Localization Manifest 一致性设计

## 1. 背景

当前仓库在多语言底层设计与显示层设计中，已经收敛出以下目标形态：

- `dic` authoring 真源位于 `modules/<ModuleId>/metadata/**`
- `dic` 资源应通过文件内容中的 `type = "dic"` 识别
- `view metadata` 通过 `dicKey + localKey` 消费文案
- 前端运行时继续只消费扁平化后的 `localization manifest`
- 前端 `t(key)` 继续只接受单个 flat key

但现有宿主实现仍保留一部分过渡约束：

- `LocalizationRuntimeManifestBuilder` 仅扫描 `metadata/dictionaries/*.dic.json`
- `dic` 主题名当前主要从文件名推导
- builder 尚未把 `type = "dic"` 当成真正的识别条件

这意味着当前运行时链路已经可用，但 authoring 真源识别规则还没有完全和现有设计收口保持一致。

本设计的目标，是把这条差异单独收成一份短设计，避免继续把 `dic` 本体规则散落在 UI 设计、多语言底层设计和实现细节说明之间。

## 2. 目标

- 明确 `dic` 资源在 manifest 生成链路中的识别方式
- 明确 `dicKey` 的来源
- 明确 `localization manifest` 的扁平化投影规则
- 明确宿主、前端与元数据设计各自的职责边界
- 为后续实现收口提供单独依据，不污染当前 UI 主线设计

## 3. 非目标

- 不重写前端 `LocalizationProvider` 或 `t(key)` 基座
- 不重新设计 `dic` 文件结构
- 不重新设计 `languages.json`
- 不扩展到 `enum` 的整体实现改造
- 不在本文中直接进入代码实现

## 4. 范围

本设计覆盖：

- `dic` 资源在宿主 manifest 生成链路中的识别方式
- `dicKey` 与 `localKey` 的运行时投影规则
- `runtime/localization-manifest.json` 的生成责任
- 前端 `LocalizationProvider` 对该 manifest 的消费边界

本设计不覆盖：

- `dic` 本体 authoring 结构细节的重新定义
- `view metadata` 全量结构
- 页面级多语言显示规则的重新定义
- `enum` 的 authoring 结构与显示逻辑

## 5. 设计约束

- `dic` 是否成立必须由文件内容中的 `type = "dic"` 判定，不能只依赖目录名或文件名
- `dicKey` 不得仅从文件名推导；文件名可以作为约束校验，但不应作为唯一语义来源
- `localization manifest` 继续保持“按语言分组后的扁平文本表”结构
- 前端运行时继续只消费 manifest，不直接扫描 authoring 元数据
- 扁平化规则必须与前端现有 `t(key)` 完全兼容
- 不引入第二套页面文案查询入口

## 6. 总体方案

### 6.1 方案概述

本轮采用以下收口方式：

1. `dic` authoring 继续位于 `modules/<ModuleId>/metadata/**` 范围内
2. manifest 生成链路在扫描模块 metadata 时，通过文件内容中的 `type = "dic"` 识别 `dic`
3. `dicKey` 来自 dic 文件内容中的业务根对象命名空间
4. 宿主在生成 `localization manifest` 时，把每个词条统一扁平化为：
   - `dicKey.localKey -> 文案`
5. 前端继续复用现有：
   - `LocalizationProvider`
   - `catalogs`
   - `t(key, fallback?)`
6. `view metadata` 继续声明：
   - `dicKey`
   - `localKey`
   运行时再组合成 `t("${dicKey}.${localKey}")`

### 6.2 为什么选这个方案

- 它与现有前端运行时结构完全兼容，不要求重做 `catalogs` 结构
- 它把 `dic` 的语义来源从“文件名约定”收回到“文件内容声明”
- 它与 `metadata-authoring-layout-unification-design.md` 中“类型由文件自描述”的规则一致
- 它与 `user-visible-text-localization-foundation-design.md` 中“manifest 是纯扁平文本表”的规则一致
- 它让 UI 设计文档只负责消费规则，不重复定义 `dic` 本体

### 6.3 为什么不继续保留当前实现方式

继续保留“只扫 `dictionaries/*.dic.json` + 从文件名推导主题名”的问题在于：

- 它和当前“以 `type` 判定元数据类型”的统一规则不一致
- 它会把文件名约定继续固化为长期语义来源
- 它使 `dic` 运行时装配和其他元数据类型的装配模型继续分叉

## 7. 关键模型

### 7.1 dic 资源识别

`dic` 资源识别规则：

- 扫描范围：`modules/<ModuleId>/metadata/**`
- 类型判定：文件内容中的 `type = "dic"`
- 目录名与文件名：仅作为组织与校验约束，不作为唯一识别依据

### 7.2 dicKey 来源

`dicKey` 来源规则：

- `dicKey` 来自 dic 文件内容中的业务根对象命名空间
- `dic` 文件除元数据头外只允许一个业务根对象
- 该业务根对象命名空间就是运行时扁平化时使用的 `dicKey`

例如：

```json
{
  "schemaVersion": "1.0",
  "type": "dic",
  "PersonListView": {
    "Title": {
      "zhs": "人员中心",
      "en": "People Center"
    }
  }
}
```

在这个例子中：

- `dicKey = PersonListView`
- `localKey = Title`

### 7.3 manifest 扁平化模型

宿主生成 `localization manifest` 时，统一把 dic 词条投影为：

```text
dicKey.localKey -> 文案
```

例如：

```text
PersonListView.Title -> 人员中心
PersonListView.Query_Keyword -> 关键字
```

manifest 最终仍保持：

- `defaultLanguage`
- `supportedLanguages`
- `catalogs`

其中：

- `catalogs = 语言 -> flat key -> 文案`

### 7.4 view metadata 消费模型

`view metadata` 不直接保存最终 flat key，而继续保存两段：

- `dicKey`
- 局部 `titleKey / descriptionKey / labelKey / confirmKey`

运行时消费规则：

```text
t("${dicKey}.${localKey}")
```

例如：

- `dicKey = PersonListView`
- `labelKey = Query_Keyword`
- 运行时最终解析：
  - `t("PersonListView.Query_Keyword")`

## 8. 核心流程

### 8.1 宿主生成 manifest

1. 扫描 `runtime/modules/**/metadata/**`
2. 找到文件内容中 `type = "dic"` 的资源
3. 读取其唯一业务根对象，确定 `dicKey`
4. 遍历词条与各语言翻译
5. 投影成 `dicKey.localKey -> 文案`
6. 汇总到 `catalogs[language][flatKey]`
7. 输出 `runtime/localization-manifest.json`

### 8.2 前端消费 manifest

1. Shell 启动时请求 `/plugins/localization-manifest.json`
2. 把 `catalogs` 交给 `LocalizationProvider`
3. 页面或模块通过 `t(flatKey)` 读取当前语言文案
4. `view metadata` 先把 `dicKey + localKey` 组合成 flat key，再传给 `t()`

## 9. 接口与集成点

### 9.1 宿主侧

现有入口继续保留：

- `server/kernel/PluginPackages/LocalizationRuntimeManifestBuilder.cs`
- `server/kernel/PluginPackages/RuntimeViewMaterializer.cs`
- `apps/host-api/Program.cs`

### 9.2 前端侧

现有入口继续保留：

- `frontend/packages/core/src/localization/LocalizationContext.tsx`
- `frontend/packages/shell/src/localization/runtimeLocalizationManifest.ts`
- `frontend/packages/shell/src/App.tsx`

### 9.3 文档边界

- `dic` 本体结构与 authoring 规则：继续以
  - [user-visible-text-localization-foundation-design.md](/D:/Github/ICP-X/NewP/docs/designs/user-visible-text-localization-foundation-design.md)
  - [metadata-authoring-layout-unification-design.md](/D:/Github/ICP-X/NewP/docs/designs/metadata-authoring-layout-unification-design.md)
  为准
- `view` 如何消费 dic：继续以
  - [metadata-driven-backoffice-ui-design.md](/D:/Github/ICP-X/NewP/docs/designs/platform/metadata-driven-backoffice-ui-design.md)
  为准

## 10. 数据与状态

- authoring 真源：
  - `modules/<ModuleId>/metadata/**` 中的 `type = "dic"` 文件
- 运行时真源：
  - `runtime/localization-manifest.json`
- 前端内存态：
  - `LocalizationProvider.catalogs`
- 前端读取入口：
  - `t(flatKey, fallback?)`

## 11. 错误处理与风险

### 11.1 失败信号

- manifest builder 仍然只扫描 `metadata/dictionaries/*.dic.json`
- manifest builder 仍然主要依赖文件名推导 `dicKey`
- builder 未校验 `type = "dic"`
- 前端 `catalogs` 中找不到预期的 `dicKey.localKey`

### 11.2 风险

- 如果继续保留文件名推导，会让实现长期背离“类型由文件自描述”的平台规则
- 如果 UI 设计文档继续重复 `dic` 本体定义，后续容易形成双真源

### 11.3 回滚

- 本设计本身只涉及文档
- 若后续实现发现收口路径需要调整，可回退该设计文档，不影响现有运行时链路
- 若实现已开始，可先保留现有 builder 行为，再按阶段切换到 `type = "dic"` 识别

## 12. 验证方案

设计阶段最小验证：

1. 与现有多语言底层设计一致
2. 与元数据布局统一设计一致
3. 与前端现有 `LocalizationProvider` 消费结构一致
4. AI-first 文档校验通过

## 13. 未决问题

- builder 从“目录 + 文件名推导”切到“扫描 metadata + `type = dic` 识别”时，是否需要保留一段兼容窗口
- `dic` 文件内容中的业务根对象与文件名不一致时，是否继续直接失败，还是只做警告后迁移

## 14. 实施拆分建议

1. 先落本设计文档
2. 再做一次实现一致性 review，确认现有 builder 与本文差异
3. 若确认进入实施，再单独开实现计划：
   - 收口 `LocalizationRuntimeManifestBuilder`
   - 补对应测试
   - 验证 manifest 生成与前端消费链路

## 15. AI-First 最小检查清单

1. 入口在哪里  
`LocalizationRuntimeManifestBuilder`、`RuntimeViewMaterializer`、`LocalizationProvider`

2. 边界在哪里  
本文只收口 `dic` 识别与 manifest 投影一致性，不重写前端 i18n 基座，不重写 `dic` 本体 schema

3. 如何验证  
文档一致性 review + AI-first 文档校验 + 后续实现阶段的 manifest 生成测试

4. 失败信号是什么  
builder 继续依赖目录/文件名、未识别 `type = "dic"`、前端缓存缺少 `dicKey.localKey`

5. 如何回滚  
回退该设计文档；实现阶段若需要则保留现有 builder 路径并分阶段切换
