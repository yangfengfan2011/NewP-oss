# 前端 UI 控件库实现文档

## 1. 概述

`@icp-platform/ui`（位于 `frontend/packages/ui`）是平台共享前端控件库，为所有前端模块提供统一的基础控件、结构型页面块、复合控件和复杂控件。

**当前状态**：28 个控件已实现，8 个模块页面已完成消费迁移。

**设计依据**：[frontend-ui-component-strategy-design.md](../designs/frontend-ui-component-strategy-design.md)

## 2. 目录结构

```
frontend/packages/ui/
├── package.json           # @icp-platform/ui, peerDep react ^18.2.0
├── README.md              # 控件清单、使用说明、约束
├── tsconfig.json
└── src/
    ├── index.ts           # Barrel 导出，按层分段
    ├── components/        # 基础控件 + 复合控件 + 复杂控件
    │   ├── Button.tsx
    │   ├── Input.tsx
    │   ├── Textarea.tsx
    │   ├── Select.tsx
    │   ├── Checkbox.tsx
    │   ├── Switch.tsx
    │   ├── Radio.tsx
    │   ├── Badge.tsx
    │   ├── Tabs.tsx
    │   ├── EmptyState.tsx
    │   ├── LoadingState.tsx
    │   ├── ErrorState.tsx
    │   ├── Pagination.tsx
    │   ├── Modal.tsx
    │   ├── Drawer.tsx
    │   ├── ConfirmDialog.tsx
    │   ├── FilterBar.tsx
    │   ├── KeyValueEditor.tsx
    │   ├── Tree.tsx
    │   ├── TreeSelect.tsx
    │   ├── DataTable.tsx
    │   └── Uploader.tsx
    ├── patterns/          # 结构型页面块
    │   ├── PageHeader.tsx
    │   ├── Panel.tsx
    │   ├── SummaryCard.tsx
    │   ├── TableWrap.tsx
    │   ├── StatusBadge.tsx
    │   └── FormInline.tsx
    └── styles/
        ├── tokens.css     # 设计令牌（颜色、间距、圆角、阴影）
        └── components.css # 所有控件 CSS（sh- 前缀）
```

## 3. 控件分层

### 3.1 基础控件（13 个）

位于 `components/`，对应设计策略 Phase A。

| 控件 | 文件 | 说明 | 核心 Props |
|------|------|------|------------|
| Button | Button.tsx | 按钮 | `variant?: 'primary' \| 'secondary' \| 'danger'`, `size?: 'sm' \| 'md' \| 'lg'` |
| Input | Input.tsx | 输入框 | `label?`, `error?`，继承 `InputHTMLAttributes` |
| Textarea | Textarea.tsx | 多行文本框 | `label?`，继承 `TextareaHTMLAttributes` |
| Select | Select.tsx | 下拉选择 | `options: SelectOption[]`, `label?`, `placeholder?` |
| Checkbox | Checkbox.tsx | 复选框 | `checked`, `onChange(checked)`, `label?` |
| Switch | Switch.tsx | 开关 | `checked`, `onChange(checked)`, `label?`, `disabled?` |
| Radio | Radio.tsx | 单选组 | `name`, `options: RadioOption[]`, `value?`, `onChange(value)`, `direction?` |
| Badge | Badge.tsx | 内联徽标 | `variant?: 'success' \| 'danger' \| 'warning' \| 'neutral'`, `children` |
| Tabs | Tabs.tsx | 选项卡 | `tabs: Tab[]`, `activeKey`, `onChange(key)` |
| EmptyState | EmptyState.tsx | 空数据占位 | `message`, `children?` |
| LoadingState | LoadingState.tsx | 加载中占位 | `message?` |
| ErrorState | ErrorState.tsx | 错误占位 | `message`, `children?` |
| Pagination | Pagination.tsx | 分页器 | `current`, `total`, `pageSize`, `onPageChange(page)` |

### 3.2 结构型页面块（6 个）

位于 `patterns/`，对应设计策略第二层。

| 控件 | 文件 | 说明 | 核心 Props |
|------|------|------|------------|
| PageHeader | PageHeader.tsx | 页面标题区 | `title`, `eyebrow?`, `description?`, `actions?: ReactNode` |
| Panel | Panel.tsx | 面板容器 | `title`, `meta?`, `actions?`, `compact?`, `children?` |
| SummaryCard | SummaryCard.tsx | 摘要卡片 | `label`, `value`, `meta?: ReactNode` |
| TableWrap | TableWrap.tsx | 简单表格 | `columns: TableColumn[]`, `data: T[]`, `rowKey` |
| StatusBadge | StatusBadge.tsx | 实体状态徽标 | `variant?`, `label`, `icon?: ReactNode` |
| FormInline | FormInline.tsx | 行内表单容器 | `children` |

### 3.3 复合控件（5 个）

位于 `components/`，对应设计策略 Phase B。

| 控件 | 文件 | 说明 | 核心 Props |
|------|------|------|------------|
| Modal | Modal.tsx | 模态对话框，Portal 渲染 | `open`, `onClose`, `title`, `size?: 'sm' \| 'default' \| 'lg'`, `footer?` |
| Drawer | Drawer.tsx | 右侧滑入抽屉，Portal 渲染 | `open`, `onClose`, `title`, `size?`, `footer?` |
| ConfirmDialog | ConfirmDialog.tsx | 确认弹窗，封装 Modal | `open`, `onConfirm`, `onCancel`, `title`, `message`, `variant?: 'danger'` |
| FilterBar | FilterBar.tsx | 筛选工具栏 | `fields: FilterField[]`, `values`, `onChange`, `onSearch`, `onReset` |
| KeyValueEditor | KeyValueEditor.tsx | 键值对编辑器 | `items: KeyValuePair[]`, `onChange` |

### 3.4 复杂控件（4 个）

位于 `components/`，对应设计策略 Phase C。

> **注意**：按设计文档，Phase C 控件应"按证据推进"。当前 4 个控件已预建，后续应根据真实功能需求验证和调整接口。

| 控件 | 文件 | 说明 | 核心 Props |
|------|------|------|------------|
| Tree | Tree.tsx | 递归树形控件 | `data: TreeNode[]`, `selectedKey?`, `onSelect?`, `renderLabel?` |
| TreeSelect | TreeSelect.tsx | 下拉树选择器 | `data: TreeNode[]`, `value?`, `onChange`, `placeholder?` |
| DataTable | DataTable.tsx | 可排序可选择表格 | `columns`, `data`, `rowKey`, `selectable?`, `sort?`, `onSortChange?`, `toolbar?` |
| Uploader | Uploader.tsx | 拖拽上传 | `accept?`, `multiple?`, `items: UploadItem[]`, `onAdd`, `onRemove?`, `onRetry?` |

## 4. 样式体系

### 4.1 设计令牌（tokens.css）

```css
/* 颜色 */
--sh-primary          /* 主色 */
--sh-success          /* 成功 */
--sh-error            /* 错误 */
--sh-warning          /* 警告 */

/* 文本 */
--sh-text-primary     /* 主文本色 */
--sh-text-secondary   /* 次文本色 */
--sh-text-muted       /* 弱文本色 */

/* 背景和边框 */
--sh-bg-subtle        /* 辅助背景 */
--sh-border           /* 标准边框色 */

/* 几何 */
--sh-radius-sm / md / lg
--sh-space-1 ~ --sh-space-8
--sh-shadow-sm / md / lg
```

### 4.2 CSS 命名规则

- 所有类名使用 `sh-` 前缀
- 变体使用后缀：`sh-btn-primary`、`sh-status-badge-success`
- 状态使用后缀：`sh-tab-active`、`sh-switch-track-on`

### 4.3 使用方式

```tsx
import '@icp-platform/ui/styles';  // 引入所有 CSS（tokens + components）
```

## 5. 消费方式

```tsx
// 所有导出均通过 barrel import
import { Button, Input, Panel, SummaryCard, Modal } from '@icp-platform/ui';
```

消费端 `package.json`：

```json
{
  "dependencies": {
    "@icp-platform/ui": "workspace:*"
  }
}
```

## 6. 已完成迁移的模块页面

| 模块 | 页面 | 使用的控件 |
|------|------|------------|
| authorization | PermissionList | PageHeader, SummaryCard, Panel, Badge, TableWrap |
| authorization | RoleManagement | PageHeader, Button, SummaryCard, Panel, Badge |
| config-center | ConfigManagement | PageHeader, SummaryCard, Panel, Badge, Button, LoadingState, ErrorState, EmptyState |
| config-center | ValueListManagement | PageHeader, SummaryCard, Panel, Button, Input, Textarea, EmptyState, LoadingState, ErrorState, ConfirmDialog |
| audit | OperationLogs | PageHeader, SummaryCard, Panel, Badge, Pagination, LoadingState |
| audit | AccessLogs | PageHeader, SummaryCard, Panel, Badge, Pagination, LoadingState |
| files | FileList | Panel, SummaryCard, Button, LoadingState, ErrorState, EmptyState, ConfirmDialog |
| files | FilesHome | PageHeader, SummaryCard |

## 7. 设计对齐状态

| 设计要求 | 当前状态 |
|----------|----------|
| 方案 C：先基础后复杂 | ✅ 按 A → B → C 推进 |
| `components/` 放基础控件 | ✅ |
| `patterns/` 放结构块 | ✅ |
| Phase A 基础控件 | ✅ 13 个（含设计列出的 Switch、Radio） |
| Phase A 结构块 | ✅ 6 个（含设计列出的 StatusBadge、FormInline） |
| Phase B 复合控件 | ✅ 5 个 |
| Phase C 按证据推进 | ⚠️ 已预建 4 个，待真实功能验证 |
| 控件文档与示例 | ✅ README + 本文档 |
| 第二次复用升级 | ✅ 规则已纳入 README |

## 8. 已知差异与后续方向

- **PermissionSelector**：设计列为 Phase B 按需控件，尚未实现。待权限管理页面有真实交互需求时补充。
- **Phase C 控件**：Tree、TreeSelect、DataTable、Uploader 已预建但未被真实功能驱动，接口可能在实际集成时调整。
- **`pnpm build` 验证**：由于终端环境限制，尚未执行全量前端构建。

## 9. 验证命令

```bash
cd frontend
pnpm --filter @icp-platform/ui build    # UI 包独立构建
pnpm build                                # 全工作区构建
```

## 10. 相关文档

- 设计策略：[frontend-ui-component-strategy-design.md](../designs/frontend-ui-component-strategy-design.md)
- 包内说明：[frontend/packages/ui/README.md](../../frontend/packages/ui/README.md)
- 模块集成指南：[frontend/docs/module-integration-guide.md](../../frontend/docs/module-integration-guide.md)
- 组件化防护栏：[componentization-guardrails.md](../architecture/componentization-guardrails.md)
