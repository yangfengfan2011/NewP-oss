# 标准列表页模板宿主设计

## 1. 背景

当前已经有两份前置设计确定了两层边界：

1. `nav metadata` 统一页面入口  
见 `docs/designs/platform/nav-metadata-page-entry-design.md`

2. `StandardListPage` 只是标准列表页的统一渲染内核  
见 `docs/designs/platform/standard-list-page-template-design.md`

但当前仍缺一层关键宿主能力：

- 谁来承接 `standard-list` 入口
- 谁来读取 `viewKey`
- 谁来加载 `view metadata`
- 谁来把 `viewKey` 解析到业务 provider / action handler / renderer
- 谁来决定在成功、缺失注册、metadata 缺失时显示什么

如果缺少这一层，就会出现两个问题：

- 仍然容易退回到“业务页直接引用 `StandardListPage`”的过渡态
- `StandardListPage` 会被迫吸收 metadata 装配和注册表解析逻辑，边界再次变脏

因此，本设计要补齐“标准列表页模板宿主”这一层，明确它与导航入口、标准列表页模板、模块注册表之间的关系。

## 2. 目标

- 定义统一宿主页 `StandardListPageHost` 的职责边界
- 明确宿主页与 `nav metadata / view metadata / StandardListPage / list view registry` 的分工
- 定义宿主页最小输入与最小依赖
- 定义缺失注册、metadata 缺失、类型不匹配时的失败信号
- 为后续标准列表页从过渡态切换到目标入口模型提供稳定中间层

## 3. 非目标

- 不在本设计中处理 `custom` 页面渲染
- 不在本设计中设计 `detail` 页面宿主
- 不在本设计中实现完整 SDK 重构
- 不在本设计中替代 `StandardListPage`
- 不在本设计中进入具体业务页面实现

## 4. 范围

本设计涉及：

- `StandardListPageHost` 的职责与落点
- `list view registry` 的最小契约方向
- 宿主页与 Shell 路由分流的配合关系
- 宿主页的失败态与回滚边界

本设计不涉及：

- `custom page registry` 的详细实现
- `nav metadata` 文件结构再扩展
- `view metadata` 结构大改
- 页面级视觉设计细节

## 5. 设计约束

- 宿主页负责装配与分流，不负责业务数据获取实现细节。
- 宿主页不得吸收 `StandardListPage` 的纯渲染职责。
- 宿主页不得让 `StandardListPage` 反向知道 `viewKey` 解析逻辑。
- 宿主页必须可验证、可报错、可回退，不能静默吞掉缺失注册问题。
- 当前阶段应优先建立清晰边界，而不是把宿主页做成新的大而全页面引擎。

## 6. 总体方案

### 6.1 宿主页定位

`StandardListPageHost` 是标准列表页入口的运行时宿主。

它位于以下链路中：

```text
nav metadata
  -> entryType = standard-list
  -> targetKey = viewKey
  -> StandardListPageHost
  -> list view registry
  -> StandardListPage
```

也就是说：

- `nav metadata` 决定“应该进入标准列表页体系”
- 宿主页决定“如何把入口解析成真实可渲染页面”
- `StandardListPage` 只负责“把标准列表页渲染出来”

### 6.2 宿主页职责

宿主页负责：

1. 接收 `viewKey`
2. 加载并校验对应 `view metadata`
3. 从 `list view registry` 中解析出：
   - data provider
   - action handlers
   - optional renderers
4. 组装标准列表页所需的输入
5. 将最终结构交给 `StandardListPage`

宿主页不负责：

- 具体业务数据怎么查
- 某列如何具体渲染
- 某个动作调用哪个后端接口
- 列表页具体的视觉分区样式

### 6.3 `StandardListPage` 与宿主页的边界

`StandardListPage` 应保持为纯渲染内核。

它知道：

- 标题
- 筛选区内容
- 表格内容
- 底部区内容
- 高级筛选抽屉内容

它不知道：

- `viewKey`
- metadata 加载逻辑
- registry 匹配逻辑
- provider 的存在

这样才能确保：

- 宿主页负责“把页面准备好”
- 模板负责“把页面画出来”

### 6.4 宿主页与业务 provider 的边界

`list view registry` 中注册的业务 provider 应负责：

- 列表数据加载
- 筛选状态管理策略
- 行级动作处理
- 标准列表页所需的业务渲染片段

宿主页只负责调用 provider 并把结果转接给模板。

也就是说，宿主页与 provider 的关系更像：

- 宿主页是运行时装配层
- provider 是业务适配层

## 7. 宿主页建议落点

建议宿主页放在 Shell 侧或与 Shell 紧邻的运行时装配层，而不是放在 `ui` 包。

推荐落点方向：

- `frontend/packages/shell/src/pages/StandardListPageHost.tsx`

原因：

- 它属于运行时入口装配，而不是纯 UI 渲染组件
- 它需要接近 Shell 的 metadata 与注册表上下文
- 它不应污染 `@icp-platform/ui` 的纯展示职责

相对地：

- `StandardListPage` 继续放在 `frontend/packages/ui`

## 8. 宿主页最小输入

建议宿主页首轮只接收一个稳定输入：

```ts
interface StandardListPageHostProps {
  viewKey: string;
}
```

其他上下文通过运行时环境获得，例如：

- metadata loader
- localization
- registry

这样可以保持宿主页的入口语义非常清晰：

- 给我一个 `viewKey`
- 我负责把这个标准列表页装起来

## 9. `list view registry` 最小契约方向

本设计先冻结“职责方向”，不展开最终 SDK 字段。

首轮最小注册方向应支持：

- `viewKey`
- `provider`
- `handlers`
- `renderers`（可选）

可理解为：

```ts
type ListViewRegistryEntry = {
  viewKey: string;
  provider: unknown;
  handlers: unknown;
  renderers?: unknown;
};
```

这里故意不在本设计中展开具体类型签名，避免把宿主设计和 SDK 细节绑死。

## 10. 失败态设计

宿主页必须显式承接以下失败态：

1. `view metadata` 不存在
2. `view metadata` 存在，但不是 `list` 类型
3. `list view registry` 中不存在该 `viewKey`
4. registry 返回的注册项类型不完整

当前阶段建议：

- 使用显式错误页或错误状态块
- 日志输出具体缺失项
- 不静默降级为默认页面

这样更符合 AI-First 和可验证原则。

## 11. 运行时链路

完整链路建议收口为：

```text
Sidebar / Router
  -> 根据 nav metadata 命中 standard-list 入口
  -> 解析出 viewKey
  -> 渲染 StandardListPageHost(viewKey)
  -> host 读取 view metadata
  -> host 查询 list view registry
  -> host 组装页面输入
  -> StandardListPage 渲染
```

## 12. 与现有过渡实现的关系

当前已经存在的：

- `PersonListPage -> StandardListPage`

应视为过渡实现。

其价值是：

- 先把列表页骨架从业务页抽出

但目标状态不应停留在这里，而应升级为：

- `StandardListPageHost(viewKey) -> StandardListPage`

因此，后续实施计划应以“引入宿主页并迁移人员中心入口”为主线，而不是继续扩散业务页直接引用模板的做法。

## 13. 验证方案

设计层至少应验证：

1. 宿主页和模板的职责边界是否清楚
2. 宿主页是否不依赖具体业务模块页面组件
3. 宿主页是否只接收 `viewKey` 即可完成装配
4. 宿主页失败态是否可观测

## 14. 失败信号

以下现象说明宿主页设计失败：

- 宿主页开始直接承载大量业务逻辑
- `StandardListPage` 反向知道 `viewKey` 和 metadata 解析细节
- 宿主页被放进 `ui` 包
- 宿主页仍需要依赖具体业务页组件才能工作

## 15. 回滚思路

- 若宿主页设计无法稳定落地，可继续保留当前过渡实现
- 不删除 `StandardListPage`
- 不影响 `nav metadata` 入口设计本身

## 16. AI-First 最小检查清单

### 入口在哪里

- `docs/designs/platform/standard-list-page-host-design.md`

### 边界在哪里

- 宿主页负责装配
- 模板负责渲染
- provider 负责业务适配

### 如何验证

- 检查 `viewKey -> host -> registry -> template` 链路是否清楚
- 检查宿主页是否不承担多余职责

### 失败信号是什么

- 宿主页变成新的页面解释器
- 模板反向承担入口解析职责
- 业务页组件仍然是必需入口

### 如何回滚

- 保留当前过渡态
- 暂不启用宿主页入口
- 不影响现有模板能力与 nav 入口设计
