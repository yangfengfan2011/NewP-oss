# Shell 与标准 CRUD 工作台视觉收口设计

## 1. 背景

人员中心已经接入可复用的 Ant Design Pro 风格标准 CRUD 工作台，但当前页面仍有三类视觉噪音：

- Shell 顶部被左侧导航切开，平台栏不像统一宿主。
- 顶部品牌文字、工作台文案和视图切换在当前阶段不是必要操作，挤占注意力。
- 标准 CRUD 单列表页重复展示页面标题、描述、列表标题和顶部计数，层次分割过重。

本轮按最新视觉稿收口平台 Shell 与标准 CRUD 单列表页默认 chrome，使后续 CRUD 资源能复制同一套更轻的工作台外观。

## 2. 目标

- 顶部 Header 横跨整个视口，左侧只保留 `ICP` 图标，右侧保留用户信息和退出登录。
- 左侧导航从 Header 下方开始，底色保持安静，子级缩进明显收窄。
- 标准 CRUD 单列表页不显示主内容标题、描述、列表标题和表格顶部计数。
- 页面级动作保留在工作区顶部命令区右侧，最多常驻 5 个，超过后由后续动作聚合设计处理。
- 筛选区和表格区形成连续工作面，减少卡片割裂和顶部空白。

## 3. 非目标

- 不调整后端接口、权限模型、菜单元数据、标准 CRUD 数据契约。
- 不重新设计登录页、首页、非标准 CRUD 业务页面。
- 不实现动作超过 5 个后的更多下拉逻辑；本轮只保留布局约束和默认样式。
- 不删除平台视图能力本身，只从 Header 常驻 UI 中移除入口。

## 4. 范围

允许修改：

- `frontend/packages/shell/src/components/Layout.tsx`
- `frontend/packages/shell/src/components/Header.tsx`
- `frontend/packages/shell/src/components/Sidebar.tsx`
- `frontend/packages/shell/src/index.css`
- `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
- `frontend/packages/ui/src/pro-adapters/CrudWorkspacePage.tsx`
- `frontend/packages/ui/src/pro-adapters/CrudTableSection.tsx`
- 相关前端测试文件

不修改：

- Organization 模块业务资源注册和服务实现。
- 后端 Host、Controller、标准 CRUD 内核。
- 菜单 metadata 的含义和层级结构。

## 5. 设计约束

- Host/Shell 只负责布局、全局 Provider、菜单聚合、路由聚合和受控注册。
- 人员中心仍必须通过标准 CRUD Host 渲染，不回退到模块私有页面。
- 样式默认服务平台可复用工作台，不写死人员中心业务特例。
- 代码注释只补关键边界；样式命名继续沿用现有 `np-*` 约定。

## 6. 总体方案

采用“Shell chrome 收口 + 标准 CRUD adapter 默认轻量化”的方案：

- `Layout` 改为 Header 在最外层，下面再放 Sidebar + Content，使 Header 横跨全宽。
- `Header` 移除品牌文字、工作台说明和视图切换，只保留 `ICP` 图标与登录用户区。
- `Sidebar` 保留折叠控制和菜单投影，缩窄子项缩进、降低视觉权重。
- `CrudWorkspacePage` 允许无标题工作台：有标题时渲染左侧标题，无标题时只渲染右侧命令区。
- `CrudTableSection` 不再生成标题和顶部计数，只提供轻量工具区；总数继续由分页底部展示。
- `StandardCrudListPageHost` 对标准单列表页不再传入 title/description/list title。

不选择在人员模块私有 CSS 中覆盖，是因为这会破坏标准 CRUD 样板的可复制性。

## 7. 关键模型

本轮不新增后端模型或 DTO。

前端仅调整组件展示契约：

- `CrudWorkspacePage.title` 从必填改为可选。
- `CrudTableSection.title/total` 从外部语义展示字段中移除或转为非必需。
- 标准 CRUD Host 继续使用现有 `pageTitle` 作为抽屉标题和动作文案来源，不作为列表页主标题展示。

## 8. 核心流程

页面打开流程：

1. Shell 渲染全宽 Header。
2. Header 下方渲染 Sidebar 与 Content。
3. PageContainer 仍渲染 breadcrumb。
4. StandardCrudListPageHost 渲染命令区、筛选区和表格连续工作面。
5. 表格底部分页展示 `共 N 条`，顶部不重复展示计数。

异常流程：

- 标准 CRUD 注册缺失或列表加载失败时，现有错误状态继续展示。
- 抽屉新建、查看、编辑继续使用 `pageTitle`，不受列表页标题移除影响。

## 9. 接口与集成点

- 后端接口：无变化。
- 前端接入点：Shell `Layout/Header/Sidebar` 与 UI `CrudWorkspacePage/CrudTableSection`。
- 模块集成：Organization 仍只声明标准 CRUD resource，不感知 Shell 样式调整。

## 10. 数据与状态

不新增持久化数据和运行时状态。

`shellView` 仍保留在 `PlatformContext`，只是本轮不在 Header 常驻展示切换控件。后续若需要恢复入口，应另行设计入口位置。

## 11. 错误处理与风险

风险：

- 现有测试可能依赖重复标题或顶部计数，需要改为断言新 chrome。
- Header 移除视图切换后，平台视图切换能力缺少可见入口。
- CSS 调整可能影响其他标准 CRUD 页面。

控制方式：

- 用标准 CRUD Host 测试确认主内容不再重复标题/描述/顶部计数。
- 用 Shell layout 测试确认 Header 精简和 Sidebar 仍可渲染菜单。
- 浏览器 smoke 检查人员中心真实页面。

回滚方式：

- 反向回退本设计对应的 Shell、UI adapter、StandardCrudListPageHost 和测试改动。
- 如果只需恢复视图切换入口，可单独恢复 Header 中 `Segmented` 展示，不影响 CRUD 工作台。

## 12. 验证方案

最小验证：

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- antd-shell-layout standard-crud-list-host organization-person-center-list-layout person-center-crud-pro-sample
pnpm --dir frontend --filter @icp-platform/ui build
pnpm --dir frontend --filter @icp-platform/shell build
```

浏览器验证：

- 打开 `http://localhost:3000/organization/persons`。
- 确认 Header 横跨全宽且左侧只显示 `ICP` 图标。
- 确认 Sidebar 从 Header 下方开始，子项缩进较浅。
- 确认主内容无 `人员中心` 标题、无描述、无 `人员列表`、无顶部 `共 1 条`。
- 确认底部分页仍显示总数。

## 13. 未决问题

- `shellView` 切换入口未来放在哪里仍未定。本轮只移除 Header 常驻入口，不删除能力。
- 超过 5 个页面动作后的 `更多` 聚合交互暂未实现。

## 14. 实施拆分建议

1. 先更新测试，锁定 Header 精简、标准 CRUD 单列表页去重标题和顶部计数。
2. 调整 Shell 布局和 Header/Sidebar 样式。
3. 调整 CRUD adapter 和 Host 参数。
4. 运行定向测试、构建和浏览器 smoke。

## 15. AI-First 最小检查清单

- 入口在哪里：`frontend/packages/shell/src/components/Layout.tsx`、`Header.tsx`、`Sidebar.tsx`、`StandardCrudListPageHost.tsx` 和 `frontend/packages/ui/src/pro-adapters/`。
- 边界在哪里：只改 Shell chrome、标准 CRUD 前端工作台 adapter、样式和测试；不改后端、菜单 metadata、Organization 业务服务。
- 如何验证：执行第 12 节的前端测试、UI/Shell 构建和浏览器 smoke。
- 失败信号是什么：Header 仍被 Sidebar 切开、Header 出现被移除文案、主内容仍重复标题/描述/顶部计数、导航缩进仍过大、测试或构建失败。
- 如何回滚：反向回退本轮涉及文件；如只恢复视图切换，单独恢复 Header 中视图切换控件。
