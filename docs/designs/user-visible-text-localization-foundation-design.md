# 用户可见文本多语言底层设计

## 1. 背景

当前仓库已经具备最小多语言基座与元数据结构：

- `dic` 作为多语言词条元数据结构
- `enum` 作为枚举结构元数据
- `modules/Platform/metadata/languages.json` 作为全局语言配置入口

但现状仍有几个直接问题：

- 新增功能模块还没有被正式约束为“用户可见文本必须多语言化”
- 后端返回给前端展示的消息还没有统一的多语言契约
- 当前 `modules/Platform/metadata/dictionaries/*.dic.json` 与 `modules/Platform/metadata/enumerations/*.json` 主要是同步过来的过渡样例资产，内容质量不稳定、格式未整理，不适合作为后续正式模块开发基线
- 现有 Shell 中的多语言样例链路会继续给后续开发传递“可以先依赖 Platform 预置样例资产”的错误信号

当前仓库阶段已经进入“先把底层设计好，再开发功能模块”的节奏，因此需要先把用户可见文本多语言化的底层规则、资源归属、消息契约和过渡资产清理范围收成正式设计。

## 2. 目标

- 为后续所有新增或修改的功能模块建立统一的用户可见文本多语言规则。
- 明确“用户可见文本”的范围边界，只约束面向最终用户的展示文本。
- 明确前端 UI 文本与“后端返回给前端展示”的消息的统一契约。
- 保留并继续采用现有 `dic / enum / languages.json` 元数据结构。
- 明确未来正式资源按模块维护，不再依赖当前 `Platform` 下同步过来的 `dic/enum` 样例资产。
- 明确当前 `modules/Platform/metadata/dictionaries/*.dic.json` 与 `modules/Platform/metadata/enumerations/*.json` 进入清理范围。

## 3. 非目标

- 不在本设计中要求一次性整改现有全部模块的历史中文文本。
- 不在本设计中重新设计新的国际化框架或替换现有 `LocalizationProvider` 基座。
- 不在本设计中改变 `dic`、`enum`、`languages.json` 的文件结构。
- 不在本设计中设计邮件、导出、报表之外的复杂通知平台。
- 不在本设计中引入数据库权威的多语言仓库。

## 4. 范围

本设计覆盖：

- `modules/<ModuleId>/metadata/dictionaries/*.dic.json`
- `modules/<ModuleId>/metadata/enumerations/*.json`
- `modules/Platform/metadata/languages.json`
- 前端 `LocalizationProvider` 消费规则
- 前端模块声明文本、页面文本、字段显示文本
- 后端返回给前端展示的用户消息契约
- 当前 `modules/Platform/metadata/dictionaries/*.dic.json`
- 当前 `modules/Platform/metadata/enumerations/*.json`
- 当前 Shell 中依赖上述过渡资产的样例接线、样例页面展示与对应测试

本设计不覆盖：

- 只给开发/运维排障用的日志、trace、异常堆栈
- 设计文档、代码注释、XML 注释、测试断言文本
- 历史模块的大规模补翻译专项

## 5. 设计约束

- 保持平台模块边界清晰，资源默认按模块归属维护，不回到全局大字典仓库模式。
- 不改变 `dic / enum / languages.json` 既有结构，只规范其使用方式与消费契约。
- 多语言规则只强制约束“用户可见文本”，不扩展到纯开发诊断信息。
- 新规则要优先适合 AI 小上下文修改、验证与回滚。
- 任何用户可见文本新增规则都不能依赖口头说明，必须在资源、契约、设计文档和代码 review 中显式表达；自动化门禁不属于本轮范围。
- 当前 `modules/Platform/metadata/dictionaries/*.dic.json` 与 `modules/Platform/metadata/enumerations/*.json` 视为待清理过渡资产，不继续作为未来正式模块示例。

## 6. 总体方案

### 6.1 方案概述

本轮采用以下总体方案：

1. 保留现有 `dic / enum / languages.json` 元数据结构。
2. 新增或修改的用户可见文本必须进入模块自己的多语言元数据，不允许直接写中文用户可见文本。
3. 多语言元数据 authoring 采用“两层 key”结构：
   - 第一层：模块或共享归属
   - 第二层：该归属下的稳定业务词条
4. 运行时消费可以继续使用扁平 key 形式，例如 `ConfigCenter.ListTitle`，但这是消费投影形态，不是 authoring 结构本身。
5. 宿主在启动阶段读取各模块 `dic` 元数据与全局语言配置，产出前端静态 `localization manifest`；前端 `LocalizationProvider` 只消费该 manifest，不直接 import 模块元数据文件。
6. `localization manifest` 只承载语言配置与文本 catalogs，不承载枚举结构、模型结构、页面结构或其他非多语言数据。
7. 后端返回给前端展示的用户消息默认采用 `messageKey + messageArgs + level` 作为主契约；`code` 仅在需要程序识别该结果语义时按需启用，由前端按当前语言翻译展示。
8. 后端只有在自己就是最终输出者的场景，才直接返回已翻译文本，例如导出文件名、报表标题、邮件/通知正文。
9. 当前 `modules/Platform/metadata/dictionaries/*.dic.json` 与 `modules/Platform/metadata/enumerations/*.json` 进入清理范围；`modules/Platform/metadata/languages.json` 保留。
10. 当前 Shell 中依赖上述过渡资产的多语言样例接线、首页样例区块和对应测试一并下线或改写。

### 6.2 为什么选这个方案

- 它保留了当前仓库已经认可的元数据结构，不会额外引入新模型。
- 它把“结构保留”和“过渡内容清理”拆开，避免误把旧内容当成未来正式基线。
- 它符合“按模块维护资源”的目标，降低全局耦合和多人并行冲突。
- 它把 authoring 真源与前端运行时消费显式分层，避免 Shell 直接依赖模块元数据文件。
- 它让前端继续掌握显示语言上下文，更适合 UI 切语言和多端复用。
- 它比“后端默认直接返回已翻译文本”更能保持 API 语义稳定。

### 6.3 为什么不选其他方案

#### 方案 A：把所有用户可见文本集中到全局共享字典

不选原因：

- 会把模块私有文案重新耦合回全局仓库。
- 不利于插件化和模块独立回滚。
- 不适合 AI-first 的小上下文修改。

#### 方案 B：后端默认直接返回已翻译文本

不选原因：

- 前端才掌握当前显示语言。
- API 契约会退化为展示文本契约。
- 不利于多端复用、缓存回放和统一展示规则。

#### 方案 C：保留当前 Platform 样例资产，等以后再删

不选原因：

- 更容易继续往旧过渡资产里新增词条。
- 会持续误导后续模块把 `Platform` 同步样例当作正式基线。
- 当前比以后更适合做受控清理。

## 7. 关键模型

### 7.1 用户可见文本

用户可见文本指最终用户会直接看到的文本，包括：

- 菜单名、页面标题、按钮文案、标签、字段显示名、表头、输入提示文本、帮助文案、空状态、确认框、Toast、状态文案
- 前端展示的 `Dictionary`、`Enumeration`、下拉选项名、状态名、`DisplayValue`
- 元数据中最终会渲染到界面的模块名、菜单名、字段显示名、描述、提示语
- 后端返回且会直接展示到 UI 的校验错误、业务错误、导入结果摘要、保存成功/失败提示、权限不足提示

不包括：

- 日志、trace、异常堆栈、开发脚本输出
- 代码注释、设计文档、测试断言文本
- 数据库对象名、DTO 字段名、内部常量名

### 7.2 `dic` authoring 结构

未来正式 authoring 继续采用：

```json
{
  "schemaVersion": "1.0",
  "type": "dic",
  "ConfigCenter": {
    "ListTitle": {
      "zhs": "配置中心",
      "en": "Config Center"
    },
    "DuplicateKey": {
      "zhs": "配置项已存在",
      "en": "Configuration item already exists"
    }
  }
}
```

约束：

- 第一层 key 表达模块或共享归属，例如 `ConfigCenter`、`Organization`、`Common`
- 第二层 key 表达稳定业务语义，例如 `ListTitle`、`DuplicateKey`
- 第二层不重复第一层信息
- 运行时如需扁平 key，可统一投影为 `ConfigCenter.ListTitle`

### 7.3 `enum` 结构

未来正式 authoring 继续采用：

```json
{
  "schemaVersion": "1.0",
  "type": "enum",
  "Gender": {
    "items": [
      { "name": "Female" },
      { "name": "Male" }
    ]
  }
}
```

约束：

- 枚举结构与枚举多语言仍然分离
- 枚举结构进入模块自己的 `metadata/enumerations/`
- 枚举显示文本进入模块自己的 `metadata/dictionaries/`

### 7.4 语言配置

全局语言集合继续由：

- `modules/Platform/metadata/languages.json`

负责声明。

约束：

- 本轮保留该文件
- 不允许各模块重复声明支持语言集合
- 新增语言时先改全局语言配置，再补各模块资源

### 7.5 前端运行时 `localization manifest`

宿主启动阶段应产出一份纯多语言的前端静态 `localization manifest`。

最小示意：

```json
{
  "defaultLanguage": "zhs",
  "supportedLanguages": [
    { "code": "zhs", "displayName": "简体中文" },
    { "code": "en", "displayName": "English" }
  ],
  "catalogs": {
    "zhs": {
      "Common.Save": "保存",
      "ConfigCenter.ListTitle": "配置中心",
      "Gender.Female": "女"
    },
    "en": {
      "Common.Save": "Save",
      "ConfigCenter.ListTitle": "Config Center",
      "Gender.Female": "Female"
    }
  }
}
```

约束：

- 该 manifest 只承载多语言相关运行时结果。
- 该 manifest 最小字段为 `defaultLanguage`、`supportedLanguages`、`catalogs`。
- `catalogs` 是按语言分组后的扁平文本表。
- 枚举项显示名属于用户可见文本，因此应投影为普通文本 key，例如 `Gender.Female`、`Gender.Male`。
- 枚举结构定义本身不属于多语言，不进入该 manifest。
- 模型结构、页面结构、模块结构及其他运行时清单不进入该 manifest。
- 前端 `LocalizationProvider` 应消费该 manifest，而不是直接 import `modules/<ModuleId>/metadata/**`。

### 7.6 前后端消息契约

后端返回给前端展示的用户消息默认模型：

```json
{
  "success": false,
  "level": "error",
  "messageKey": "ConfigCenter.DuplicateKey",
  "messageArgs": {
    "key": "TenantName"
  }
}
```

可选兼容字段：

```json
{
  "code": "CONFIG_DUPLICATE_KEY",
  "fallbackMessage": "Configuration item TenantName already exists."
}
```

带独立语义字段的示意：

```json
{
  "success": false,
  "level": "error",
  "messageKey": "Errors.RequiredField",
  "fieldKey": "Fields.DisplayName",
  "messageArgs": {
    "entityName": "齿轮箱总成"
  }
}
```

约束：

- `messageKey + messageArgs` 是主契约
- `messageArgs` 只承载动态原始值，例如对象名称、编码、单据号、用户输入内容
- 静态多语言引用不得作为普通参数放进 `messageArgs`
- 若后端需要表达“哪个字段/哪个动作/哪个状态”，应使用独立语义字段，例如 `fieldKey`、`actionKey`、`statusKey`
- `code` 只在前端分支处理、测试稳定断言、日志聚合或监控统计等需要程序识别结果语义时才启用
- `fallbackMessage` 只是兼容/降级字段，不是长期主入口

## 8. 核心流程

### 8.1 新增前端用户可见文本

1. 在模块自己的 `metadata/dictionaries/*.dic.json` 中新增词条
2. 模块声明文本只引用 key，不写中文字面量
3. 宿主启动阶段将各模块 `dic` 词条投影到前端静态 `localization manifest`
4. 前端通过统一 `LocalizationProvider` / `t()` 读取文本
5. 构建或测试时检查资源存在性和消费结果

### 8.2 新增后端返回给前端展示的消息

1. 后端先定义 `messageKey + messageArgs + level`
2. 如消息需要表达静态 UI 语义，单独补 `fieldKey`、`actionKey`、`statusKey` 等独立字段
3. 如该结果需要程序识别，再补稳定业务语义 `code`
4. 前端收到后按当前语言翻译展示
5. 如需降级，前端可使用 `fallbackMessage`

### 8.3 后端直接交付最终文本

仅以下场景允许后端直接返回已翻译文本：

- 导出文件名
- 报表标题
- 邮件正文
- 通知正文
- 后端生成并直接交付给用户的结果文本

### 8.4 清理当前 Platform 过渡资产

1. 删除 `modules/Platform/metadata/dictionaries/*.dic.json`
2. 删除 `modules/Platform/metadata/enumerations/*.json`
3. 保留 `modules/Platform/metadata/languages.json`
4. 下线当前 Shell 中依赖上述过渡资产的样例接线、样例页面展示与对应测试
5. 同步更新把这些样例资产写成正式基线的设计/验证文档

## 9. 接口与集成点

### 9.1 前端接入点

- `frontend/packages/core/src/localization/LocalizationContext.tsx`
- 各模块自己的前端页面与模块声明
- Shell 只保留统一 Provider 和受控注册，不再保留依赖 Platform 过渡样例资产的展示样例
- 前端只消费宿主启动阶段产出的 `localization manifest`

### 9.2 后端接入点

- 各模块 API 的用户消息返回模型
- 各模块应用服务/领域服务中的用户消息语义定义
- 宿主启动阶段的多语言元数据投影入口

### 9.3 元数据入口

- `modules/<ModuleId>/metadata/dictionaries/*.dic.json`
- `modules/<ModuleId>/metadata/enumerations/*.json`
- `modules/Platform/metadata/languages.json`

### 9.4 运行时投影边界

- `dic` 是多语言 authoring 真源
- 宿主启动阶段负责将各模块 `dic` 与全局语言配置投影成前端静态 `localization manifest`
- `enum` 继续作为枚举结构 authoring 真源，但其结构定义不进入多语言 manifest
- 枚举项显示文本通过 `dic` 投影为普通多语言 key

## 10. 数据与状态

### 10.1 正式权威源

未来正式权威源为：

- 各模块自己的 `dic`
- 各模块自己的 `enum`
- 平台全局 `languages.json`

### 10.2 待清理过渡资产

以下内容进入当前清理范围：

- `modules/Platform/metadata/dictionaries/*.dic.json`
- `modules/Platform/metadata/enumerations/*.json`

这些文件的定位是：

- 历史同步过来的样例/过渡内容
- 不再作为未来正式模块开发的推荐示例
- 不继续承接新增正式词条

### 10.3 共享词典策略

- 默认按模块维护资源
- 不允许把模块私有文案沉到全局共享字典
- 平台后续如确需共享基础词典，只允许建立极小范围的 `Common` 共享词典
- 当前不保留旧的 `Platform/Common` 同步样例资产作为默认基线

## 11. 错误处理与风险

### 11.1 风险

| 风险 | 影响 | 控制方式 |
| --- | --- | --- |
| 继续往 Platform 过渡资产里加新词条 | 后续清理成本升高 | 当前即进入清理范围 |
| 前端继续直接 import 模块元数据文件 | 运行时消费与 authoring 真源耦合 | 统一改为宿主启动阶段产出 `localization manifest` |
| 模块声明继续直接写中文 | 新规则失效 | 设计中明确禁止，当前先通过代码 review 控制 |
| 后端默认直接返回已翻译文本 | API 契约退化为展示契约 | 明确 `messageKey + messageArgs` 为主契约，`code` 仅按需启用 |
| 把共享字典扩大成全局大仓库 | 模块边界模糊 | 明确共享词典只允许极小范围 |
| 清理时漏掉 Shell 样例接线和测试 | 构建或测试失败 | 清理时将页面、测试、文档一起收口 |

### 11.2 失败信号

- 新模块仍然直接写中文用户可见文本
- API 返回给前端展示的消息仍以已翻译文本为主契约
- 前端仍直接 import 模块 `dic/enum` 文件，而不是消费宿主产出的 `localization manifest`
- 删除 Platform 过渡资产后，Shell 样例链路残留并导致构建/测试失败
- 文档仍把当前 Platform 样例资产写成正式推荐基线

### 11.3 回滚思路

- 若清理 Platform 过渡资产后发现当前实现尚未准备好，可回滚本轮删除的 `dic/enum` 文件、Shell 样例接线和测试修改
- 若宿主启动阶段的 manifest 投影链路未准备好，可回滚到清理前的样例链路，但不改变“前端最终应消费 manifest”的设计方向
- 若新消息契约实施后发现前端未准备好，可短期保留 `fallbackMessage` 作为兼容降级字段
- 回滚不改变 `dic / enum / languages.json` 的结构设计，只恢复本轮清理前的样例资产与消费链路

## 12. 验证方案

设计阶段最小验证：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\designs\user-visible-text-localization-foundation-design.md
```

实施阶段最小验证建议：

```powershell
pnpm --dir .\frontend build
```

```powershell
pnpm --dir .\frontend\packages\shell exec vitest --run
```

```powershell
dotnet build .\ICP-Platform.sln -nr:false -v minimal
```

并在后续补充：

- API 用户消息返回模型的定向单元测试
- 宿主启动阶段 `localization manifest` 投影结果的定向验证
- 清理 Platform 过渡资产后的前端构建与测试验证

## 13. 未决问题

当前轮已无未决问题。

- “用户可见文本禁止直接写中文” 当前先不引入自动化门禁，先通过设计约束与代码 review 控制。
- `fallbackMessage` 当前固定为保留的兼容字段，但不是主契约，也不作为未来功能模块设计的默认入口。

## 14. 实施拆分建议

### 14.1 第一步：清理过渡资产

- 删除 `modules/Platform/metadata/dictionaries/*.dic.json`
- 删除 `modules/Platform/metadata/enumerations/*.json`
- 保留 `modules/Platform/metadata/languages.json`
- 改由宿主启动阶段产出前端静态 `localization manifest`
- 下线 Shell 当前样例接线、样例页面与对应测试

最小交付：

- 过渡资产已清理
- `localization manifest` 最小投影链路已可用
- 前端构建和对应测试恢复稳定

### 14.2 第二步：冻结模块接入规则

- 明确模块声明文本只允许写 key
- 明确资源按模块归属维护
- 明确共享词典的极小边界

最小交付：

- 设计、计划和实施文档统一采用同一口径

### 14.3 第三步：冻结后端消息契约

- 为后端返回给前端展示的用户消息定义统一 DTO 结构
- 明确 `messageKey + messageArgs + level` 为主契约，`code` 为按需启用字段

最小交付：

- 至少一个模块按新消息契约落地

### 14.4 第四步：按需要补受控校验

- 为模块声明文本、前端 UI 文本和 API 用户消息补受控检查入口

最小交付：

- 新增功能模块不再依赖人工口头提醒才能保持多语言规范

## 15. AI-First 最小检查清单

1. 入口在哪里

- 设计入口是本文档
- 未来正式 authoring 入口是 `modules/<ModuleId>/metadata/dictionaries/*.dic.json`、`modules/<ModuleId>/metadata/enumerations/*.json`
- 全局语言入口是 `modules/Platform/metadata/languages.json`

2. 边界在哪里

- 本设计只定义用户可见文本多语言化的底层规则、消息契约和 Platform 过渡资产清理范围
- 不进入历史模块全量补翻译
- 不改变 `dic / enum / languages.json` 结构

3. 如何验证

- 设计阶段通过 `validate-ai-first-design.ps1` 校验
- 实施阶段通过前端构建、Shell 测试、后端构建、`localization manifest` 投影验证和定向消息契约测试验证

4. 失败信号是什么

- 新模块继续直接写中文用户可见文本
- API 用户消息继续以已翻译文本为主契约
- 前端未切换到消费宿主启动阶段产出的 `localization manifest`
- Platform 过渡资产未清理干净或清理后残留样例链路失败

5. 如何回滚

- 回滚本轮删除的 Platform 过渡资产、Shell 样例接线和测试改动
- 不回滚 `dic / enum / languages.json` 结构本身
