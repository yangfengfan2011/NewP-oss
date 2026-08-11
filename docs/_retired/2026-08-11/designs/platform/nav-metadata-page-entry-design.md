# nav metadata 统一页面入口设计

## 1. 背景

当前前端接入链路存在两个问题：

- 导航入口与路由入口的真实生效来源仍主要散落在模块代码中
- 标准列表页与自定义页面尚未被放到同一套页面入口模型下描述

这会直接导致以下问题：

- 导航 authoring 真源不稳定，后续模块越来越多时难以统一校验
- 标准列表页无法真正做到“导航进入模板页，再由 `view metadata` 决定页面结构”
- 自定义页面虽然不需要 `view metadata`，但入口仍然没有纳入统一治理

前面我们已经确认两条关键边界：

1. 不是所有页面都应该元数据化  
只有标准列表页应由模板页 + `view metadata` 承接。

2. 但所有页面的导航入口都应该统一进入 `nav metadata`  
`custom` 页面不使用 `view metadata`，不代表它不进入 `nav metadata`。

因此，本设计需要回答的问题不再是“所有页面都要不要统一模板化”，而是：

**如何让 `nav metadata` 成为统一页面入口真源，同时允许标准列表页和自定义页面走不同的运行时渲染路径。**

## 2. 目标

- 让 `nav metadata` 成为前端页面入口的 authoring 真源
- 让导航入口显式区分：
  - `standard-list`
  - `custom`
- 让标准列表页通过统一模板页入口 + `viewKey` 运行
- 让自定义页面通过稳定 `pageKey` + 注册表运行
- 保持元数据声明性，不直接耦合 React 组件名或文件路径
- 为后续运行时校验、回滚与 AI 辅助开发提供稳定入口

## 3. 非目标

- 不在本设计中让所有页面都使用 `view metadata`
- 不在本设计中引入 `detail` 页面类型
- 不在本设计中引入完整 `route metadata`
- 不在本设计中实现页面生命周期脚本、表达式 DSL 或低代码解释器
- 不在本设计中完成全模块迁移实施

## 4. 范围

本设计涉及：

- `modules/<ModuleId>/metadata/navigation/*.nav.json` 的结构升级
- Shell / 运行时如何根据 `nav metadata` 装配页面入口
- 标准列表页与自定义页面的双通道运行时解析模型
- 前端模块需要提供的注册信息边界

本设计不涉及：

- `view metadata` 结构本身的大改
- 标准列表页模板内部布局细节
- 自定义页面内部 UI 结构重写
- 权限中心、规则中心、对象授权

## 5. 设计约束

- `nav metadata` 只声明入口、归属、类型与稳定目标标识，不直接写组件名或文件路径。
- `view metadata` 只服务于标准列表页，不强迫 `custom` 页面进入 `view metadata`。
- 运行时解析、注册表匹配、冲突处理与失败降级都必须收口在代码内核。
- Shell 只负责受控装配，不吸收模块私有业务逻辑。
- 当前阶段应优先建立稳定入口模型，而不是一次性把所有页面抽象成统一页面解释器。

## 6. 总体方案

### 6.1 `nav metadata` 统一页面入口

后续页面入口统一从 `nav metadata` authoring 真源声明。

每个可点击导航节点都必须显式声明：

- `entryType`
- `targetKey`

其中：

- `entryType = standard-list`
  - 表示该入口进入统一列表页模板宿主
  - `targetKey` 解释为 `viewKey`
- `entryType = custom`
  - 表示该入口进入自定义页面
  - `targetKey` 解释为 `pageKey`

这样可以同时满足：

- 标准列表页通过元数据模板化
- 自定义页面不强迫页面元数据化
- 但二者都纳入统一导航真源

### 6.2 `targetKey` 只做稳定标识，不做实现细节

`nav metadata` 中不应直接写：

- React 组件名
- TSX 文件路径
- import 路径

而应只写稳定标识：

- 标准列表页写 `viewKey`
- 自定义页面写 `pageKey`

原因：

- 元数据应保持声明性，不能耦合具体实现文件
- 组件重命名、拆分、懒加载调整时，不应反向要求改导航元数据
- `targetKey` 更适合作为校验、装配和迁移时的稳定契约

### 6.3 双注册表模型

运行时需要维护两套注册表：

1. `list view registry`
   - 负责把 `viewKey` 解析到统一模板页宿主需要的数据提供者与动作处理器

2. `custom page registry`
   - 负责把 `pageKey` 解析到具体自定义页面组件

其中：

- `standard-list` 不直接映射到业务页组件
- `custom` 不进入 `view metadata`

这保证了两类页面各走各的运行时路径，但都通过统一入口装配。

### 6.4 模板页宿主只服务标准列表页

统一模板页宿主当前只服务标准列表页入口。

宿主页本身的详细设计以后续 `standard-list-page-host-design.md` 为准。

也就是说：

- 导航点击 `standard-list`
  - 进入统一模板页宿主
  - 宿主根据 `viewKey` 读取 `view metadata`
  - 再通过 `list view registry` 找到对应 provider / action handler / renderer

- 导航点击 `custom`
  - 直接通过 `custom page registry` 解析出页面组件
  - 不读取 `view metadata`

因此，“统一导航入口”和“统一页面模板”是两层能力，不应混为一谈。

## 7. `nav metadata` 建议结构

建议在现有 `nav metadata` 最小结构上增加页面入口类型字段：

```json
{
  "schemaVersion": "1.0",
  "type": "nav",
  "navigation": {
    "items": [
      {
        "key": "organization.personCenter",
        "titleKey": "Navigation.PersonCenter",
        "parentKey": "organization",
        "path": "/organization/persons",
        "order": 110,
        "entryType": "standard-list",
        "targetKey": "organization.person.list"
      },
      {
        "key": "organization.tree",
        "titleKey": "Navigation.OrganizationTree",
        "parentKey": "organization",
        "path": "/organization/tree",
        "order": 120,
        "entryType": "custom",
        "targetKey": "organization.tree.page"
      }
    ]
  }
}
```

### 7.1 字段说明

- `entryType`
  - `standard-list`
  - `custom`

- `targetKey`
- 当 `entryType = standard-list` 时，解释为 `viewKey`
  - 当 `entryType = custom` 时，解释为 `pageKey`

### 7.2 校验规则

- 可点击节点必须同时具备 `path / entryType / targetKey`
- 容器节点不得声明 `entryType / targetKey`
- `entryType = standard-list` 时，`targetKey` 必须能在 `view metadata` 中找到对应 `viewKey`
- `entryType = custom` 时，`targetKey` 必须能在 `custom page registry` 中解析到页面组件
- `targetKey` 必须在对应入口类型作用域内唯一

## 8. 运行时装配模型

### 8.1 导航阶段

Shell 读取 `nav metadata` 后：

- 生成左侧导航树
- 为每个可点击入口记录：
  - `path`
  - `entryType`
  - `targetKey`

### 8.2 路由阶段

路由命中后，运行时根据 `entryType` 分流：

- `standard-list`
  - 进入统一模板页宿主
  - 宿主持有 `viewKey = targetKey`

- `custom`
  - 进入 `custom page registry`
  - 解析 `pageKey = targetKey`
  - 渲染对应页面组件

### 8.3 失败信号与降级

若发生以下情况，应由运行时代码内核显式报错：

- `nav metadata` 引用了不存在的 `viewKey`
- `nav metadata` 引用了不存在的 `pageKey`
- 同一路径配置了冲突入口
- 同一 `targetKey` 被错误注册到不匹配的入口类型

当前阶段不建议静默回退为默认页面。

## 9. 模块契约建议

当前 `FrontendModule` 契约仍主要暴露：

- `routes`
- `menus`

在新方向下，后续应逐步演进为：

- 标准列表页提供：
  - `viewKey -> provider/handlers/renderers` 注册
- 自定义页面提供：
  - `pageKey -> React component` 注册

也就是说，模块代码未来更像“提供注册信息”，而不是“自己声明导航和页面入口”。

本设计不直接展开 SDK 详细字段，只冻结运行时责任边界：

- `nav metadata` 是入口真源
- 模块注册表负责把 `targetKey` 解析到代码实现

## 10. 与现有标准列表页模板设计的关系

本设计与 `standard-list-page-template-design.md` 的关系是：

- 本文负责“页面怎么进来”
- 标准列表页模板设计负责“标准列表页进来以后怎么渲染”

因此，后续正确链路应为：

```text
nav metadata
  -> entryType = standard-list
  -> targetKey = viewKey
  -> 模板页宿主
  -> 标准列表页模板
  -> list view registry
```

而不再是：

```text
业务页组件
  -> 引用 StandardListPage
```

后者可以作为当前阶段的过渡实现，但不应作为目标架构继续扩散。

## 11. 验证方案

设计层至少应验证：

1. `nav metadata` 能否同时描述 `standard-list` 与 `custom`
2. 运行时是否能根据 `entryType` 正确分流
3. `targetKey` 是否足够稳定，不需要耦合组件名
4. 标准列表页与自定义页是否都能纳入统一导航真源

## 12. 失败信号

以下现象说明设计方向仍然有问题：

- `nav metadata` 仍直接耦合组件名或文件路径
- `custom` 页面被强行拉进 `view metadata`
- 标准列表页仍要求业务页组件作为运行时入口
- Shell 为了解析 `targetKey` 而吸收模块私有业务逻辑

## 13. 回滚思路

- 若新入口模型验证失败，可继续保留当前模块代码路由入口
- 新增 `entryType / targetKey` 字段可先作为扩展设计存在，不立即替换旧入口
- 不影响现有 `view metadata` 和已有页面实现

## 14. AI-First 最小检查清单

### 入口在哪里

- `docs/designs/platform/nav-metadata-page-entry-design.md`

### 边界在哪里

- `nav metadata` 统一入口
- `view metadata` 只服务标准列表页
- `custom` 页面不进入 `view metadata`
- 运行时注册表负责把 `targetKey` 解析到实现

### 如何验证

- 检查是否能清楚区分 `standard-list` 与 `custom`
- 检查元数据是否仍保持声明性
- 检查模板入口是否不再依赖业务页组件

### 失败信号是什么

- 组件实现细节重新写回元数据
- `custom` 页重新被强行 metadata 化
- 页面入口仍分散在模块代码中

### 如何回滚

- 保留当前模块代码入口模型
- 暂不启用新入口分流
- 不影响现有标准列表页模板实验
