# 人员中心前端 Pro 风格布局重构详细设计

## 1. 背景

- 当前「人员中心」已经接入 Ant Design 和标准 CRUD 宿主，但页面结构仍处于过渡态。
- 现状问题不是单点样式偏差，而是 Shell 页面 chrome、标准列表页模板、标准 CRUD 列表宿主和人员资源定义分别输出了一部分 UI，导致标题、工具栏和分页重复，视觉层级也不稳定。
- 当前页面第一眼容易先看到深色导航和大块筛选区，而不是人员表格本身，不符合企业后台“列表优先、操作克制、信息密度适中”的预期。
- 这次改造要在不改业务链路的前提下，把人员中心收敛成更接近 Ant Design Pro 的企业后台页面，并形成后续列表页可复用的最小样板。

## 2. 目标

- 将 Shell 顶栏、左侧栏和内容区调整为浅色企业后台风格。
- 保证页面只出现一套主标题、一套“新建人员”入口和一套分页。
- 将筛选区压缩为轻量工具条，把表格卡片提升为页面主视觉块。
- 将人员中心表格工具栏收敛为“刷新 / 密度 / 列设置”，不再重复放“新建人员”。
- 将行操作调整为文字链接，并为“停用”保留二次确认。
- 尽量通过 Ant Design `ConfigProvider` token 和少量受控样式统一视觉，不改动现有查询、新增、刷新、查看、编辑、停用链路。

## 3. 非目标

- 不改后端接口、DTO、权限 code 或 standard CRUD 契约语义。
- 不重写人员中心的服务层、详情页或新建页业务逻辑。
- 不在本轮顺手重做组织树、岗位管理、首页或其他模块页面。
- 不引入新的全局状态管理方案。
- 不把菜单、路由、权限或 metadata 真源迁移到 ProLayout 私有模型。

## 4. 范围

### 4.1 涉及目录

- `frontend/packages/shell/src/components/Layout.tsx`
- `frontend/packages/shell/src/components/Header.tsx`
- `frontend/packages/shell/src/components/Sidebar.tsx`
- `frontend/packages/shell/src/components/PageContainer.tsx`
- `frontend/packages/shell/src/config/antdTheme.ts`
- `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
- `frontend/packages/shell/src/standard-crud/standardCrudActionRuntime.tsx`
- `frontend/packages/shell/src/index.css`
- `frontend/packages/ui/src/pro-adapters/SearchTablePage.tsx`
- `frontend/packages/ui/src/pro-adapters/StatusTag.tsx`
- `frontend/packages/ui/src/styles/components.css`
- `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`
- `frontend/packages/shell/src/__tests__/antd-shell-layout.test.tsx`
- `frontend/packages/shell/src/__tests__/organization-person-center-list-layout.test.tsx`

### 4.2 不触碰范围

- `server/**`
- `apps/**`
- runtime metadata 文档结构
- `frontend/packages/sdk/src/**`
- 人员中心详情/新建接口与保存逻辑
- 登录、认证、权限判断和路由接管逻辑

## 5. 设计约束

- Shell 仍只负责全局布局、面包屑、Provider 和受控路由承载，不承接人员业务逻辑。
- 人员中心仍通过标准 CRUD 资源注册接入，不把业务判断写回 Shell。
- 视觉重构优先收口重复 chrome，不允许通过增加更多卡片和更多装饰来“掩盖”结构问题。
- 新增关键注释默认使用简体中文，重点解释边界、失败信号和为什么这样收口。
- 页面主标题、筛选条、表格卡片、分页和行操作都必须有唯一职责层，避免再次分散到多层组件中。

## 6. 总体方案

本次采用“Shell 变浅色、标题职责回收到列表页、列表宿主收口分页与工具栏、人员资源定义只保留模块侧字段与动作声明”的方案。

### 6.1 布局收口

- `Layout.tsx` 继续提供 `Header + Sidebar + Content + PageContainer`。
- `PageContainer` 在人员中心场景只承载面包屑和外边距，不再由 Shell 输出页面标题，避免和列表页模板重复。
- 顶栏品牌信息统一放到 `Header`，左侧栏只保留导航和折叠操作，不再在侧边栏重复品牌头。

### 6.2 标题与操作区收口

- 页面主标题“人员中心”和“新建人员”按钮由 `StandardCrudListPageHost.tsx` 输出，作为列表页唯一标题区。
- `PageContainer` 不再向人员中心注入标题。
- ProTable 工具栏只保留刷新按钮，自带 `density` 与 `setting` 选项继续启用，不再注入“新建人员”。

### 6.3 分页收口

- 底部分页只保留 `SearchTablePage.tsx` 中的 ProTable 分页。
- `StandardCrudListPageHost.tsx` 删除额外 `footerRight` 分页信息，避免上下或内外重复。

### 6.4 筛选区重构

- 人员中心筛选区改为单行或双行自适应的轻量工具条，字段顺序固定为关键字、公司、部门、在职状态、搜索、重置、更多筛选。
- 筛选字段继续由 `personStandardCrudResource.tsx` 管理，保留现有公司/部门联动与错误兜底。
- “更多筛选”本轮只提供轻量入口按钮，点击后不展开新的高级筛选流程。

### 6.5 表格区重构

- 表格区域由列表卡片承载，卡片头显示“人员列表”与“共 N 条”。
- 表头、行高、hover、分页、工具栏和状态标签通过 token 与少量类名统一。
- 至少保留 5 行示例数据的渲染能力，不额外制造大片留白。

### 6.6 行操作重构

- 查看、编辑、停用由模块侧 `renderRowActions` 统一输出为文字链接。
- 停用使用 `Popconfirm` 做二次确认，成功后仍走既有 `disablePerson` 服务和 `refresh()` 刷新逻辑。
- 列表宿主不再从 `listActions` 推导人员中心的行级停用按钮，避免按钮风格和确认方式分散。

## 7. 关键模型

### 7.1 页面 chrome 职责模型

```ts
interface PageChromeOwnership {
  shell: 'breadcrumb' | 'page-padding';
  listHost: 'title' | 'description' | 'page-primary-action';
  table: 'list-toolbar' | 'pagination';
}
```

约束：

- Shell 不拥有人员中心标题。
- 列表宿主不拥有底部分页。
- 表格工具栏不拥有主创建按钮。

### 7.2 人员中心列表动作模型

```ts
interface PersonListActionOwnership {
  pageAction: 'create';
  tableToolbar: 'refresh' | 'density' | 'setting';
  rowActions: 'view' | 'edit' | 'disable';
}
```

约束：

- `create` 只出现在标题区。
- `refresh` 只出现在表格工具栏。
- `disable` 只出现在行操作区，并必须二次确认。

## 8. 核心流程

### 8.1 页面渲染主流程

1. Shell 根据当前路径解析菜单和面包屑。
2. `Layout.tsx` 输出浅色顶栏、浅色侧边栏和灰色内容背景。
3. `PageContainer` 输出面包屑和内容外层容器。
4. `StandardCrudListPageHost.tsx` 输出页面标题区、筛选工具条和列表卡片。
5. `SearchTablePage.tsx` 使用 ProTable 加载数据并渲染底部分页。

### 8.2 查询流程

1. 用户在筛选工具条中输入条件。
2. 点击“搜索”后更新 `appliedFilters`。
3. `SearchTablePage` 根据筛选条件和分页参数请求列表接口。
4. 成功时刷新表格和条数；失败时维持错误提示和空表格兜底。

### 8.3 停用流程

1. 用户点击行内“停用”文字链接。
2. `Popconfirm` 二次确认。
3. 确认后调用 `disablePerson(id)`。
4. 成功后触发 `refresh()` 刷新列表；失败时显示错误提示。

## 9. 接口与集成点

- 后端接口继续使用 `/api/organization/persons` 列表接口与既有停用接口，不新增 API。
- Shell 侧接入点仍为 `StandardCrudListPageHost.tsx`。
- 模块侧接入点仍为 `personStandardCrudResource.tsx` 的 `renderFilters`、`renderRowActions`、`buildColumns`、`actionHandlers`。
- ProTable 适配层继续由 `SearchTablePage.tsx` 负责，避免模块直接依赖宿主内部状态。

## 10. 数据与状态

- 筛选状态继续分为 `filters` 与 `appliedFilters`，不改变查询触发语义。
- 列表数据、总数、当前页和分页大小继续由列表宿主管理。
- 公司和部门选项继续在模块侧异步加载，失败时保留错误提示和可用字段降级。
- 本轮不新增持久化设置，不缓存列设置或筛选条件。

## 11. 错误处理与风险

- 风险 1：去掉 Shell 标题后，其他页面可能暴露缺少自有标题的问题。
  - 控制方式：只让 `Layout.tsx` 停止给页面注入 `title`，保留 `PageContainer` 组件能力；若其他页面需要，可继续自行传入或使用 `PageHeader`。
- 风险 2：ProTable 工具栏与自定义卡片头同时存在时，可能出现间距或重复视觉层。
  - 控制方式：由宿主统一定义“页面标题区”和“列表卡片头”，并把 ProTable 限制为表格工具栏和分页承载。
- 风险 3：把停用从通用 `listActions` 挪到模块行操作后，若刷新上下文传递错误会影响停用后刷新。
  - 控制方式：继续使用 `renderRowActions(row, context)` 的标准上下文，不引入新的全局状态。
- 回滚或降级思路：若新布局产生意外回归，可按文件粒度回退 `Header/Sidebar/theme`、`StandardCrudListPageHost`、`personStandardCrudResource` 三组修改，不影响服务层和接口。

## 12. 验证方案

- `pnpm --dir frontend/packages/shell test -- --run src/__tests__/antd-shell-layout.test.tsx src/__tests__/organization-person-center-list-layout.test.tsx`
- `pnpm --dir frontend/packages/shell build`
- 如本地可启动页面，再执行一次人员中心人工 smoke：
  - 验证只出现一处“人员中心”主标题
  - 验证“新建人员”只在标题区出现
  - 验证分页只在表格底部出现
  - 验证左侧栏为浅色
  - 验证停用为文字链接且带二次确认

## 13. 未决问题

- 本轮“更多筛选”只做轻量入口，不展开高级筛选面板；后续若要接入，需要单独设计高级筛选状态和宿主抽屉行为。
- 侧边栏图标体系本轮不重建，只先把层级、颜色和选中态收口到浅色企业风格。

## 14. 实施拆分建议

- 第一步：调整 Shell 主题、顶栏、侧边栏和内容容器，让全局视觉先稳定。
- 第二步：调整列表宿主，去掉重复标题、重复新建按钮和重复分页。
- 第三步：重写人员中心筛选条、表格列状态样式和行操作风格。
- 第四步：补充和修正测试，最后跑构建与定向测试。

## 15. AI-First 最小检查清单

1. 入口在哪里
   - 入口在 `frontend/packages/shell/src/components/Layout.tsx`、`frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx` 和 `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`。
2. 边界在哪里
   - 允许修改 Shell 布局、主题、列表宿主、人员资源定义和相关样式/测试；不修改后端接口、SDK 契约、认证权限和 metadata 真源。
3. 如何验证
   - 通过 Shell 定向测试、Shell build 和人员中心页面 smoke 验证标题、分页、按钮、导航和停用确认是否符合预期。
4. 失败信号是什么
   - 出现双标题、双分页、双“新建人员”，侧边栏仍为深色，停用失去确认，或列表页测试/build 失败，都说明实现未按设计落地。
5. 如何回滚
   - 按 `Header/Sidebar/theme`、`StandardCrudListPageHost`、`personStandardCrudResource` 三组提交或 diff 粒度回退，业务接口与数据层无需联动回滚。
