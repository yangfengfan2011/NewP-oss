# @icp-platform/ui

平台共享前端控件库，服务于 `frontend/modules/*` 和 `frontend/packages/shell`。

## 安装

作为 workspace 包自动可用：

```json
{
  "dependencies": {
    "@icp-platform/ui": "workspace:*"
  }
}
```

## 使用

```tsx
import { Button, Input, Panel, SummaryCard } from '@icp-platform/ui';
import '@icp-platform/ui/styles';
```

## 目录结构

```
src/
├── components/    # 基础控件与复合控件
├── patterns/      # 结构型页面块
├── styles/        # CSS token 与组件样式
│   ├── tokens.css
│   └── components.css
└── index.ts       # Barrel 导出
```

- `components/`：基础原子控件、表单控件与已稳定的复合控件
- `patterns/`：结构型页面块——PageHeader、Panel、SummaryCard、TableWrap、StatusBadge、FormInline

## 控件清单

### 基础控件（components/）

| 控件 | 说明 | 核心 Props |
|------|------|------------|
| `Button` | 按钮 | `variant?: 'default' \| 'primary' \| 'danger'`, `size?: 'default' \| 'sm'`, `disabled` |
| `Input` | 输入框 | `label?`, `error?`, 继承 `InputHTMLAttributes` |
| `Textarea` | 多行文本框 | `label?`, 继承 `TextareaHTMLAttributes` |
| `Select` | 下拉选择 | `label?`, `options: SelectOption[]`, `placeholder?` |
| `Checkbox` | 复选框 | `checked`, `onChange(checked)`, `label?` |
| `Switch` | 开关 | `checked`, `onChange(checked)`, `label?`, `disabled?` |
| `Radio` | 单选组 | `name`, `options: RadioOption[]`, `value?`, `onChange(value)`, `direction?` |
| `Badge` | 内联徽标 | `variant?: 'success' \| 'danger' \| 'warning' \| 'neutral'`, `children` |
| `Tabs` | 选项卡 | `tabs: Tab[]`, `activeKey`, `onChange(key)` |
| `EmptyState` | 空数据占位 | `message`, `children?` |
| `LoadingState` | 加载中占位 | `message?` |
| `ErrorState` | 错误占位 | `message`, `children?` |
| `Pagination` | 分页器 | `current`, `total`, `pageSize`, `onPageChange(page)` |

### 结构型页面块（patterns/）

| 控件 | 说明 | 核心 Props |
|------|------|------------|
| `PageHeader` | 页面标题区 | `title`, `eyebrow?`, `description?`, `actions?` |
| `Panel` | 面板容器 | `title`, `meta?`, `actions?`, `compact?`, `children?` |
| `SummaryCard` | 摘要卡片 | `label`, `value`, `meta?` |
| `TableWrap` | 简单表格 | `columns: TableColumn[]`, `data`, `rowKey` |
| `StatusBadge` | 实体状态徽标 | `variant?`, `label`, `icon?` |
| `FormInline` | 行内表单容器 | `children` |

### 复合控件（components/）

| 控件 | 说明 | 核心 Props |
|------|------|------------|
| `Modal` | 模态对话框 | `open`, `onClose`, `title`, `size?`, `footer?`, `children` |
| `Drawer` | 侧滑抽屉 | `open`, `onClose`, `title`, `size?`, `footer?`, `children` |
| `ConfirmDialog` | 确认弹窗 | `open`, `onConfirm`, `onCancel`, `title`, `message`, `variant?` |
| `FilterBar` | 筛选工具栏 | `fields: FilterField[]`, `values`, `onChange`, `onSearch`, `onReset` |
| `KeyValueEditor` | 键值对编辑器 | `items: KeyValuePair[]`, `onChange` |

### 后续候选控件

`Tree`、`TreeSelect`、`DataTable`、`Uploader` 仍保留在包内作为候选实现，但在出现真实页面复用并完成正式公共化前，不视为稳定公开 API。

## 样式

所有 CSS 类名统一使用 `sh-` 前缀，基于 `tokens.css` 定义的设计令牌：

```css
/* 颜色 */
--sh-primary, --sh-success, --sh-error, --sh-warning

/* 文本 */
--sh-text-primary, --sh-text-secondary, --sh-text-muted

/* 尺寸 */
--sh-radius-sm, --sh-radius-md, --sh-radius-lg
--sh-space-1 ~ --sh-space-8
```

## 升级规则

遵循 [frontend-ui-component-strategy-design.md](../../docs/designs/frontend-ui-component-strategy-design.md)：

1. **第一次使用**：允许在模块内先实现，命名和结构为后续抽取留空间
2. **第二次复用**：必须评估并优先迁入本包
3. **正式公共化**：补 README、示例、使用约束和最小验证

## 验证

```bash
cd frontend
pnpm --filter @icp-platform/ui build
pnpm build
```

## 约束

- 控件不应依赖 Shell 内部实现或具体模块逻辑
- `patterns/` 中的页面块只负责布局结构，不承载业务交互
- 新增控件必须同步更新本 README 和 `index.ts` 导出
- CSS 新增类名必须使用 `sh-` 前缀
