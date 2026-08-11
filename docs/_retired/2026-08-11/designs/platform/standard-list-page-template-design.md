# 标准列表页模板能力设计

## 1. 背景

当前仓库已经具备以下基础：

- `view metadata` 已能声明列表页的查询项、列、动作与导出模式
- `frontend/packages/ui` 已有 `PageHeader / Panel / TableWrap / Drawer` 等结构块
- 组织模块“人员中心”已经实现出一条真实的列表页试点链路

但当前仍缺一层真正的平台级“列表页模板代码内核”：

- 页面分层虽然趋同，但仍主要写在具体业务页 TSX 中
- 查询区、表格区、底部分页/导出和高级筛选抽屉仍由业务页自己拼装
- `view metadata` 已经能描述“页面应该长什么样”，但还没有一个稳定模板把这些声明消费成统一页面

这会带来几个直接问题：

- 样式优化仍然会落到单个业务页，而不是平台模板层
- 后续模块容易继续复制人员中心页，而不是复用受控模板
- 元数据与代码边界会继续模糊，导致页面结构散落在 TSX 中各自实现

因此，本设计需要先补齐“标准列表页模板能力”本身，但它的定位应被明确为：

- 它是标准列表页的统一渲染内核
- 它不是最终的页面入口真源

后续真正的目标入口模型，应由 `nav metadata` + `standard-list/custom` 分流设计承接。

## 2. 目标

- 定义平台级标准列表页模板的职责边界
- 明确模板与 `view metadata / model metadata / 业务模块代码` 的分工
- 固化列表页默认布局与可复用插槽
- 让后续模块在不改 Shell 内部结构的前提下复用统一列表页模板
- 用组织模块人员中心列表页验证该模板能力可落地、可验证、可回退

## 3. 非目标

- 不在本设计中引入完整低代码页面解释器
- 不在本设计中把详情页模板化作为首轮主目标
- 不在本设计中同时完成 `page / drawer / dialog` 三种编辑承载抽象
- 不在本设计中重写 `route metadata`、`tree view`、规则中心或权限体系
- 不在本设计中追求“一份 metadata 完全零代码生成整个页面”

## 4. 范围

本设计涉及：

- `frontend/packages/ui` 内新增或沉淀的列表页模板组件
- `frontend/modules/organization/src/pages/PersonListPage.tsx` 到模板页的迁移方式
- `modules/Organization/metadata/views/PersonList.view.json` 的消费方式
- 列表页的页面头部、轻筛选条、表格区、底部分页/导出、高级筛选抽屉的统一布局

本设计不涉及：

- 人员详情页整体模板化
- 组织树页、岗位页改造
- 绑定账号复杂交互与授权复杂交互重写
- Shell 内部 Provider、菜单聚合、路由聚合逻辑改造

## 5. 设计约束

- 元数据只声明页面结构、字段归属、静态入口与显示语义，不承载执行流程与切换逻辑。
- 列表页模板必须作为代码内核存在，不能退化成“把所有行为塞进 metadata”的解释器。
- 模板能力应放在共享层，业务模块通过受控入口使用，不应直接把组织模块页面代码抬进 Shell。
- 首轮目标是“统一列表页骨架”，不是“一次性抽象所有后台页面”。
- 模板必须让 AI 能在小上下文内理解入口、改动位置、验证方式与回滚路径。

## 6. 总体方案

### 6.1 首轮只做标准列表页模板

首轮平台能力只收敛“标准列表页模板”，详情页保持当前 `detail view` 路线，不单独升格为平台模板工程。

原因：

- 当前痛点首先集中在列表页样式和骨架复用
- 人员中心列表页已经具备真实试点价值，最适合承接模板化改造
- 若同时拉入详情页模板，会把本轮范围从“模板试点”膨胀成“后台页面体系重做”

### 6.2 模板与页面入口的关系

标准列表页模板不是页面入口真源，而是标准列表页的统一渲染内核。

理想目标链路应为：

- 导航入口通过 `nav metadata` 声明
- 当入口类型为 `standard-list` 时，运行时进入模板页宿主
- 模板页宿主根据 `viewKey` 读取 `view metadata`
- 再由标准列表页模板负责统一渲染页面骨架

因此，模板与业务页的直接关系只应被理解为“当前过渡实现”，不应继续作为目标架构扩散。

### 6.3 当前过渡实现中的业务容器页

在当前尚未完成 `nav metadata` 页面入口重构前，业务页仍可作为过渡容器：

迁移后，业务页应主要负责：

- 拉取列表数据
- 提供筛选值状态与查询动作
- 提供列渲染中的少量业务特例
- 提供行级动作处理函数
- 提供新建/编辑打开方式的业务实现

模板层负责：

- 页面头部
- 轻筛选条布局
- 表格容器与标准工具区
- 底部分页与导出区
- 高级筛选抽屉骨架
- 标准空态、错误态、加载态的承接位置

但这只是过渡态，而不是最终态。

### 6.4 模板默认布局

标准列表页模板默认分成五个区块：

1. 页面头部  
承接标题、说明和少量主动作，如 `create / refresh`

2. 轻筛选条  
承接最常用的查询项，按 `view.query.fields` 顺序呈现

3. 表格主体区  
承接表格工具位、表格本体、空态/错误态/加载态

4. 表格底部  
左侧承接导出动作，右侧承接总数与分页

5. 高级筛选抽屉  
仅当 `advancedFilter.enabled = true` 时显示入口和抽屉

### 6.5 首轮统一插槽

为避免模板变成新的大而全页面，首轮只保留以下受控插槽：

- `headerActions`
- `filterFields`
- `tableActions`
- `table`
- `footerLeft`
- `footerRight`
- `advancedFilterContent`

其中：

- 布局和分区顺序由模板固定
- 具体字段渲染与表格列渲染可以先由业务页提供
- 首轮不强求“所有字段都自动渲染”，但要把渲染入口收口到模板约定接口中

### 6.6 metadata 与模板的分工

`view metadata` 首轮继续负责：

- 标题与说明文案 key
- 查询字段顺序
- 高级筛选 section 与字段顺序
- 列顺序
- 页面动作与行动作声明
- 导出模式声明

模板代码内核负责：

- 如何把这些声明组织成统一布局
- 查询区与高级筛选区的分层
- 空态/错误态/加载态的稳定承接
- 导出区与分页区的位置
- 动作按钮、表格区、抽屉区的标准视觉层级

当前过渡实现中的业务容器页负责：

- 把具体字段映射为输入组件
- 把具体列映射为渲染函数
- 处理查询、刷新、停用、新建、编辑等业务动作

## 7. 目标文件结构

首轮建议在 `frontend/packages/ui` 增加列表页模板层，而不是直接把它放进 Shell。

建议新增：

- `frontend/packages/ui/src/patterns/StandardListPage.tsx`
  - 统一列表页骨架
- `frontend/packages/ui/src/patterns/ListPageSection.tsx`
  - 若需要拆小文件，可承接单一布局块

组织模块建议调整为：

- `frontend/modules/organization/src/pages/PersonListPage.tsx`
  - 保留为人员中心列表页容器
  - 负责数据查询、字段与列映射、动作处理
  - 不再直接手写整页骨架样式

## 8. 关键接口建议

首轮不追求复杂泛型体系，但应明确模板最小输入边界。可按如下思路收口：

```ts
interface StandardListPageProps {
  title: string;
  description?: string;
  headerActions?: ReactNode;
  filterBar?: ReactNode;
  content: ReactNode;
  footerLeft?: ReactNode;
  footerRight?: ReactNode;
  advancedFilterTrigger?: ReactNode;
  advancedFilterOpen?: boolean;
  advancedFilterContent?: ReactNode;
  onAdvancedFilterClose?: () => void;
}
```

这一层先解决“统一骨架与样式复用”。

后续若需要更强模板能力，再逐步增加：

- `query field renderer registry`
- `column renderer registry`
- `action renderer registry`

但这些增强项不作为首轮必达目标。

## 9. 与页面入口设计的关系

本设计只负责标准列表页进入模板宿主之后的统一渲染问题。

对于页面如何进入模板宿主，应以后续：

- `nav-metadata-page-entry-design.md`
- `standard-list-page-host-design.md`

两份设计为准。

也就是说，目标链路应为：

```text
nav metadata
  -> entryType = standard-list
  -> targetKey = viewKey
  -> 模板页宿主
  -> StandardListPage
```

而不是：

```text
业务页组件
  -> StandardListPage
```

## 10. 组织模块试点接入方式

人员中心列表页当前可以继续作为首个过渡试点，按“容器页 + 模板”的方式迁移：

1. 保留 `PersonListPage.tsx` 作为业务容器页
2. 将页面级骨架迁移到 `StandardListPage`
3. 让 `PersonListPage` 改为：
   - 读取 runtime schema
   - 提供筛选字段渲染结果
   - 提供表格列与数据
   - 提供底部导出与分页内容
   - 提供高级筛选抽屉内容
4. 保留人员中心现有 `drawer` 编辑承载，不在本轮扩展统一编辑模板

这样做的意义是：

- 列表页视觉与骨架可以统一演进
- 人员中心仍保留必要业务特性
- 为后续切换到“模板宿主入口”保留渲染内核

但后续其他模块不应简单复制这种“业务页直接引用模板”的过渡方式，而应优先等待统一页面入口模型落地。

## 11. 验证方案

首轮验证至少包括：

1. 前端构建  
确认模板组件引入后，`frontend/packages/ui` 与组织模块都能正常构建

2. 人员中心列表页手动 smoke  
确认以下区块仍存在且布局稳定：
- 页面头部
- 轻筛选条
- 表格区
- 底部分页/导出
- 高级筛选抽屉

3. metadata 消费对齐  
确认 `PersonList.view.json` 中的：
- `query.fields`
- `advancedFilter.sections`
- `columns`
- `pageActions`
- `rowActions`
- `export.modes`

仍能驱动人员中心列表页呈现

## 12. 失败信号

以下现象说明首轮设计没有成功落地：

- 模板组件只是把原页面整段 JSX 搬过去，没有形成稳定公共边界
- 业务页仍需要重写页面级骨架样式才能接入模板
- 为支持模板而把查询逻辑、动作逻辑重新塞回 metadata
- 模板被放进 Shell 内部，导致模块无法通过共享层复用
- 人员中心迁移后，列表页结构反而比迁移前更难理解或更难回滚
- 后续模块继续把“业务页直接引用模板”误当成目标架构复制扩散

## 13. 回滚思路

- 若模板能力试点失败，可先回退组织模块对 `StandardListPage` 的接入
- 保留 `PersonListPage.tsx` 原有实现作为最近回滚点
- 若模板组件边界明显不合理，可仅删除新增模板文件，不影响现有 metadata 真源
- 本轮不改 Shell 内部结构，因此回滚不会影响其他模块接入链路

## 14. 与既有文档的关系

本设计是以下文档的补充与收敛：

- `docs/designs/platform/metadata-driven-backoffice-ui-design.md`
- `docs/designs/platform/list-management-layout-demo-design.md`
- `docs/designs/platform/nav-metadata-page-entry-design.md`
- `docs/designs/modules/organization-person-center-pilot-design.md`

其中：

- 显示层三层边界仍以 `metadata-driven-backoffice-ui-design.md` 为准
- 列表页默认布局仍以 `list-management-layout-demo-design.md` 为准
- 页面入口模型以后续 `nav-metadata-page-entry-design.md` 为准
- 人员中心仍是首个真实试点，但其列表页当前只是在过渡阶段优先落到本设计定义的模板能力上

## 15. AI-First 最小检查清单

### 入口在哪里

- 平台模板设计入口：`docs/designs/platform/standard-list-page-template-design.md`
- 目标页面入口设计：`docs/designs/platform/nav-metadata-page-entry-design.md`
- 模板实现入口：`frontend/packages/ui/src/patterns/StandardListPage.tsx`

### 边界在哪里

- `view metadata` 只声明标准列表页结构
- 模板代码负责统一骨架与布局
- 当前业务页容器只是过渡实现，不是目标入口模型

### 如何验证

- 前端构建通过
- 模板进入宿主前后的职责边界是否清楚
- 关键 metadata 声明仍能驱动页面呈现

### 失败信号是什么

- 模板没有收口页面级骨架
- 元数据被扩成行为解释器
- 业务页接入模板后反而更重
- 继续把业务页直接引用模板误当成目标架构

### 如何回滚

- 先回退组织模块接入
- 再删除模板组件
- 不影响既有 metadata 真源与 Shell 接入链路
