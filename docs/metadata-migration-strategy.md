# 元数据迁移与兼容策略

## 1. 文档目的

本文档基于 OldP 运行时元数据资产盘点，设计新平台的元数据迁移与兼容策略。

**核心原则：**
- 不把旧元数据机制原样复制为新平台核心机制
- 先做设计和边界判断，再实施转换
- 优先处理平台公共能力相关的元数据
- 当前阶段不把文件服务、WinApp、Studio 相关资产作为优先 POC

## 2. 元数据资产盘点

基于 `OldP/Runtime` 目录扫描结果：

| 资产类型 | 数量 | 位置示例 |
|---------|------|---------|
| `.dic` | 2,501 | `Runtime/1_Server/Base/Config/Dictionary/*.dic` |
| `.generator` | 2,588 | `Runtime/1_Server/*/Config/Generator/**/*.generator` |
| `.page` | 1,046 | `Runtime/1_Server/*/Config/Pages/*.page` |
| `.view` | 388 | `Runtime/1_Server/*/Config/Views/*.view` |
| `.model` | 399 | `Runtime/1_Server/*/Config/Model/*.model` |
| `.schema` | 401 | `Runtime/1_Server/*/Config/Model/*.schema` |
| `.fileref` | 76 | `Runtime/1_Server/*/Config/*FileRef.fileref` |
| `.app` | 2 | `Runtime/1_Server/Config/Apps.app` |
| **总计** | **7,401** | - |

## 3. 元数据类型分析

### 3.1 `.model` / `.schema` / `.dic` 三件套

**当前用途：**
- `.schema` - 定义数据表结构（列名、类型、主键、继承关系）
- `.model` - 定义实体类型元数据（逻辑类、图标、表名、策略）
- `.dic` - 定义多语言标签（表头、标题、字段显示名）

**示例：**
```xml
<!-- COMMANDINFO.schema -->
<Table Name="COMMANDINFO" Extend="ENTITY">
  <Column Name="EID" DataType="STRING100" Required="true" PrimaryKeyOrder="1" />
  <Column Name="ENAME" DataType="STRING200" />
</Table>

<!-- COMMANDINFO.model -->
<EntityType Name="COMMANDINFO" Extend="ENTITY" LogicClass="EntityObject" 
            TableName="COMMANDINFO" IsBusinessModel="true" />

<!-- COMMANDINFO.dic -->
<Context Name="COMMANDINFOModel">
  <Label Name="COMMANDINFOHeader">
    <LocalizedLabel LanguageRef="zhs">CommandInfo</LocalizedLabel>
  </Label>
</Context>
```

**职责分析：**
- 承担了 ORM 映射、业务模型定义、多语言、UI 显示配置的混合职责
- 三者必须同步维护，否则运行时报错
- 包含大量继承和扩展机制（`Extend="ENTITY"`）

**新平台目标形式：**
- **数据模型** → 使用 EF Core Code First 或 Fluent API
- **业务元数据** → 通过 C# Attribute 或独立配置类
- **多语言** → 使用标准 i18n 资源文件（`.resx` 或 JSON）
- **UI 显示配置** → 前端组件 props 或独立 UI Schema

**处置决策：** `Import`（部分）+ `Rewrite`（部分）

**转换策略：**
1. 提取 `.schema` 中的表结构，生成 EF Core Entity 类
2. 提取 `.model` 中的业务属性，转为 C# Attribute 或配置类
3. 提取 `.dic` 中的多语言标签，转为标准 i18n 资源
4. 不保留旧的 XML 加载机制

**风险：**
- 继承链复杂（如 `Extend="ENTITY"`），需要先梳理基类
- 数据类型映射（`STRING100` → `string` + `MaxLength(100)`）
- 旧系统可能依赖运行时动态修改元数据

**优先级：** 中（V1 可选择 1-2 个简单模型作为 POC）

---

### 3.2 `.view` 视图定义

**当前用途：**
- 定义查询视图的列配置（宽度、排序、冻结、背景色）
- 用于生成列表查询界面

**示例：**
```xml
<ObjectView Name="COMMANDSERVICEVIEW" ObjectTypeName="ENTITY">
  <ViewColumn Name="EID" Width="0" Resizable="true" Sortable="true" />
  <ViewColumn Name="COMMANDDISPLAYNAME" Width="150" Sortable="true" />
</ObjectView>
```

**职责分析：**
- 纯 UI 配置，与后端数据模型松耦合
- 包含列宽、排序、过滤等前端表格配置

**新平台目标形式：**
- 前端组件的 Column Schema（JSON 或 TypeScript 类型）
- 可选：后端提供 View Metadata API

**处置决策：** `Postpone`（V1）

**转换策略（后续阶段）：**
1. 解析 `.view` XML，提取列配置
2. 生成前端 Table Column 配置 JSON
3. 可选：提供运行时 View Metadata 服务

**风险：**
- 与旧前端控件（FilterGrid/PageGrid）强耦合
- 新前端可能不需要所有旧配置项

**优先级：** 低（V1 不处理，新模块直接用前端组件配置）

---

### 3.3 `.page` 页面定义

**当前用途：**
- 定义页面布局（容器、控件树）
- 定义页面事件处理脚本（OnCreate、OnClose）
- 定义控件属性（padding、border、type）

**示例：**
```xml
<root Name="CommandServiceSearchCommand">
  <Settings Key="title" Value="CommandServiceSearchCommand.Title"/>
  <PageHandlers>
    <PageHandler HandlerName="OnCreate" Script="InforCenter_Platform_CommandServiceSearchCommand_OnCreate(pageEvent);"/>
  </PageHandlers>
  <PageControls>
    <PageControl ControlID="TopContainer" TypeName="LayoutContainer">
      <Settings Key="type" Value="vertical"/>
    </PageControl>
  </PageControls>
</root>
```

**职责分析：**
- 类似低代码页面配置
- 与旧前端控件体系（LayoutContainer、TextBox、Button）强绑定
- 包含大量 JS 脚本引用

**新平台目标形式：**
- React 组件（TSX）
- 不保留 XML 页面定义机制

**处置决策：** `Postpone`（V1）

**转换策略（后续阶段）：**
1. 手工或半自动转换为 React 组件
2. 提取页面逻辑到独立 Hook 或 Service
3. 不保留运行时 XML 解析器

**风险：**
- 数量大（1,046 个）
- 与旧控件体系深度耦合
- 脚本逻辑难以自动迁移

**优先级：** 低（V1 不处理，新模块直接写 React 组件）

---

### 3.4 `.generator` 生成器模板

**当前用途：**
- 用于动态生成页面脚本片段
- 模板化生成 CRUD 表单、查询脚本

**示例：**
```javascript
// PAGE-ConversionAddressValues-CREATESCRIPT-EDIT.generator
{
var o = pageEvent.o.GetControl('[INFONAME]_Value');
if(o.id!=''){
  o.SetSelectedByValue(data.[INFONAME]);
}
}
```

**职责分析：**
- 代码生成模板，包含占位符（`[INFONAME]`）
- 与旧页面体系和控件 API 强绑定
- 数量最大（2,588 个）

**新平台目标形式：**
- 不保留旧生成器机制
- 新平台使用标准代码生成工具（如 T4、Roslyn Source Generator）

**处置决策：** `Retire`（V1）

**转换策略：**
- 不转换，新平台不采用此机制

**风险：**
- 旧系统可能依赖大量生成的代码

**优先级：** 无（V1 不处理）

---

### 3.5 `.dic` 多语言字典

**当前用途：**
- 定义多语言标签（中文、英文）
- 用于页面标题、字段显示名、提示信息

**示例：**
```xml
<Context Name="CommandServiceSearchCommand" IsWeb="true">
  <Label Name="Title">
    <LocalizedLabel LanguageRef="zhs">查看命令</LocalizedLabel>
    <LocalizedLabel LanguageRef="en">SearchCommand</LocalizedLabel>
  </Label>
</Context>
```

**职责分析：**
- 纯多语言资源
- 按 Context 和 Label 两级组织
- 支持多语言（zhs、en）

**新平台目标形式：**
- 标准 i18n 资源文件（JSON 或 `.resx`）
- 前端使用 `react-i18next` 或类似库
- 后端使用 ASP.NET Core Localization

**处置决策：** `Import`

**转换策略：**
1. 解析所有 `.dic` 文件
2. 按 Context 分组，生成 i18n JSON 文件
3. 提供转换脚本（Python 或 C#）

**转换脚本示例：**
```python
# dic_to_i18n.py
import xml.etree.ElementTree as ET
import json

def convert_dic_to_i18n(dic_file_path):
    tree = ET.parse(dic_file_path)
    root = tree.getroot()
    
    i18n_data = {"zhs": {}, "en": {}}
    
    for context in root.findall('.//Context'):
        context_name = context.get('Name')
        for label in context.findall('.//Label'):
            label_name = label.get('Name')
            key = f"{context_name}.{label_name}"
            for localized in label.findall('.//LocalizedLabel'):
                lang = localized.get('LanguageRef')
                text = localized.text
                if lang in i18n_data:
                    i18n_data[lang][key] = text
    
    return i18n_data
```

**风险：**
- 数量大（2,501 个）
- 可能存在重复或冲突的 Key
- 部分 `.dic` 可能包含非标准结构

**优先级：** 高（V1 可作为 Import POC）

---

### 3.6 `.fileref` 文件引用

**当前用途：**
- 定义前端资源文件引用（JS、CSS）
- 按 group 和 type 组织
- 支持主题占位符（`[THEME]`）

**示例：**
```xml
<JSFile FileName="~/Base/Library/jquery/jquery.3.4.1.js" group="jquery" type="PC,H5"/>
<CssFile FileName="~/Base/Css/[THEME]/images/baseCtrl.css" IsTheme="true" group="ctrl"/>
```

**职责分析：**
- 前端资源加载配置
- 与旧前端构建体系绑定

**新平台目标形式：**
- 使用 Vite 或 Webpack 等现代构建工具
- 通过 `import` 语句管理依赖

**处置决策：** `Retire`

**转换策略：**
- 不转换，新前端使用 npm + Vite

**风险：**
- 无

**优先级：** 无（V1 不处理）

---

### 3.7 `.app` 应用配置

**当前用途：**
- 定义应用模块层级和注册信息
- 类似模块清单

**示例：**
- `Runtime/1_Server/Config/Apps.app`（二进制文件，无法直接读取）

**职责分析：**
- 模块注册和组合
- 可能包含模块路径、版本、依赖关系

**新平台目标形式：**
- 模块契约（C# Interface + Attribute）
- 模块清单（JSON 或 YAML）

**处置决策：** `Rewrite`

**转换策略：**
1. 反序列化 `.app` 文件（需要旧平台代码）
2. 提取模块注册信息
3. 生成新平台模块清单

**风险：**
- 二进制格式，需要旧平台反序列化代码
- 可能包含大量已废弃模块

**优先级：** 中（V1 需要模块注册机制，但不一定要迁移旧 `.app`）

---

## 4. 处置决策汇总

| 资产类型 | 数量 | 决策 | 目标形式 | V1 优先级 | 备注 |
|---------|------|------|---------|----------|------|
| `.model` / `.schema` | 800 | Import + Rewrite | EF Core Entity + Attribute | 中 | 选 1-2 个简单模型作 POC |
| `.dic` | 2,501 | Import | i18n JSON / .resx | 高 | 适合作为首个 Import POC |
| `.view` | 388 | Postpone | 前端 Column Schema | 低 | V1 不处理 |
| `.page` | 1,046 | Postpone | React 组件 | 低 | V1 不处理 |
| `.generator` | 2,588 | Retire | - | 无 | 不迁移 |
| `.fileref` | 76 | Retire | Vite/Webpack | 无 | 不迁移 |
| `.app` | 2 | Rewrite | 模块契约 + 清单 | 中 | 不直接迁移旧 `.app` |

---

## 5. V1 推荐 POC 顺序

基于风险、收益和依赖关系，建议 V1 按以下顺序验证：

### 5.1 第一优先：`.dic` 多语言字典

**理由：**
- 结构简单，风险低
- 与业务逻辑解耦
- 转换脚本易实现
- 可立即验证新平台 i18n 机制

**实施步骤：**
1. 编写 `.dic` → i18n JSON 转换脚本
2. 选择 `Base/Config/Dictionary/Common.dic` 作为样本
3. 生成 `frontend/packages/core/locales/zh-CN/common.json`
4. 在前端 Shell 中集成 `react-i18next`
5. 验证多语言切换

**交付物：**
- 转换脚本：`scripts/migration/dic-to-i18n.py`
- 样本输出：`frontend/packages/core/locales/zh-CN/common.json`
- 文档：`docs/migration/dic-migration-guide.md`

---

### 5.2 第二优先：`.model` / `.schema` 简单模型

**理由：**
- 验证数据模型迁移可行性
- 为后续模块提供参考
- 可选择无继承、无复杂关系的简单模型

**推荐样本：**
- `COMMANDSERVICESETTING`（配置项，结构简单）
- 或 `Enumeration` 相关模型（枚举定义）

**实施步骤：**
1. 手工提取 `.schema` 中的列定义
2. 生成 EF Core Entity 类
3. 编写 FluentAPI 配置
4. 提取 `.dic` 中的显示名，生成 Attribute
5. 验证 CRUD 操作

**交付物：**
- Entity 类：`server/modules/ConfigCenter/Entities/CommandServiceSetting.cs`
- 配置类：`server/modules/ConfigCenter/EntityConfigurations/CommandServiceSettingConfiguration.cs`
- 文档：`docs/migration/model-migration-guide.md`

---

### 5.3 第三优先（可选）：Setting 配置项导入

**理由：**
- 验证运行时配置数据导入
- 为 ConfigCenter 模块提供初始数据

**实施步骤：**
1. 解析 `Runtime/1_Server/Config/Setting/*.setting`（如果是 XML）
2. 转换为新平台配置项格式
3. 通过 EF Core Migration 或 Seed Data 导入
4. 验证配置读取 API

**交付物：**
- 转换脚本：`scripts/migration/setting-import.py`
- Seed Data：`server/modules/ConfigCenter/Data/Seeds/SettingSeed.cs`
- 文档：`docs/migration/setting-import-guide.md`

---

## 6. 不建议在 V1 采用的策略

### 6.1 不要原样复制旧元数据加载机制

**反例：**
- 把 `Hoteam.InforCenter.Service.Interface.Config.Dictionarys` 类复制到新平台
- 保留 XML 运行时解析逻辑

**正确做法：**
- 一次性转换为标准格式
- 使用新平台标准机制（i18n、EF Core）

---

### 6.2 不要把 `.generator` 作为迁移目标

**理由：**
- 数量最大（2,588 个）
- 与旧控件体系深度耦合
- 新平台不应依赖动态代码生成

**正确做法：**
- 新模块直接写标准代码
- 必要时使用 Roslyn Source Generator

---

### 6.3 不要在 V1 大规模迁移 `.page` / `.view`

**理由：**
- 数量大（1,046 + 388）
- 与旧前端体系强耦合
- 新前端已切换到 React

**正确做法：**
- V1 只做新模块，直接写 React 组件
- 后续阶段可选择性转换高价值页面

---

## 7. 转换工具架构

建议在 `scripts/migration/` 下建立转换工具集：

```
scripts/
  migration/
    dic-to-i18n.py          # .dic → i18n JSON
    model-to-entity.py      # .model/.schema → EF Core Entity
    setting-import.py       # Setting 数据导入
    view-to-schema.py       # .view → Column Schema（后续）
    common/
      xml_parser.py         # 通用 XML 解析
      naming_converter.py   # 命名转换（PascalCase/camelCase）
    README.md               # 工具使用说明
```

---

## 8. 临时兼容层策略

### 8.1 不建立运行时兼容层

**理由：**
- 会成为长期技术债
- 阻碍新平台架构演进

**例外：**
- 如果必须短期兼容旧 API，必须：
  - 在 `server/compatibility/` 下隔离
  - 明确标记为 `[Obsolete]`
  - 设定退出时间表

---

### 8.2 数据迁移优于运行时兼容

**原则：**
- 优先一次性转换数据
- 不在新平台中保留旧格式解析器

**示例：**
- `.dic` → 转换为 i18n JSON，不保留 `.dic` 加载器
- `.model` → 转换为 EF Core Entity，不保留 `.model` 解析器

---

## 9. 风险与缓解

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| 元数据数量大（7,401 个） | 转换工作量大 | V1 只处理高优先级类型，其余 Postpone |
| 继承关系复杂（`Extend="ENTITY"`） | 转换逻辑复杂 | 先梳理基类，选择无继承的简单模型作 POC |
| 旧系统依赖运行时动态修改元数据 | 新平台可能无法完全兼容 | 明确新平台不支持运行时修改，改为配置管理 |
| `.app` 为二进制格式 | 无法直接解析 | 使用旧平台代码反序列化，或手工提取关键信息 |
| 转换脚本可能有 Bug | 生成错误数据 | 小样本验证 → 人工审查 → 批量转换 |

---

## 10. 成功标准

V1 元数据迁移 POC 成功标准：

1. **`.dic` 转换**：
   - 转换脚本可运行
   - 生成的 i18n JSON 格式正确
   - 前端可正常加载并切换语言

2. **`.model` / `.schema` 转换**：
   - 至少 1 个简单模型转换成功
   - 生成的 Entity 类可编译
   - EF Core 可正常 CRUD

3. **文档完整**：
   - 转换步骤清晰
   - 已知限制明确
   - 后续扩展路径清晰

---

## 11. 下一步行动

建议按以下顺序推进：

1. **评审本策略**：与团队确认处置决策和优先级
2. **建立转换工具骨架**：创建 `scripts/migration/` 目录和基础工具
3. **实施 `.dic` POC**：完成第一个转换脚本和样本验证
4. **实施 `.model` POC**：完成第一个模型转换和 CRUD 验证
5. **更新文档**：记录转换过程和已知问题
6. **评估扩展**：根据 POC 结果决定是否扩大转换范围

---

## 12. 参考文档

- [oldp-asset-inventory-v1.md](./oldp-asset-inventory-v1.md) - 资产盘点
- [oldp-asset-disposition-v1.md](./oldp-asset-disposition-v1.md) - 资产处置矩阵
- [OldP ApiDoc - Config.md](../OldP/ApiDoc/02_Business/ServiceInterface/Config.md) - 旧平台配置 API
- [AGENTS.md](../AGENTS.md) - 项目架构规则
- [scope-v1.md](./scope-v1.md) - V1 范围定义

---

**版本**: v1  
**创建日期**: 2026-04-04  
**状态**: Draft - 待评审
