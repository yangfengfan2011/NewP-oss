# NewP Shell 视觉基础实施计划

> **给代理执行者：** 必需子技能：使用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans`，按任务逐项实现本计划。步骤使用复选框语法（`- [ ]`）进行跟踪。

**目标：** 为 `NewP` 建立第一阶段的视觉基础，使产品在更多模块页面继续扩展前，先拥有稳定的登录页、Shell、首页入口以及模式驱动的导航结构。

**架构：** 在现有 React/Vite 前端中引入轻量级的 Shell 层设计令牌与共享布局优化。实现尽量局限在 `frontend/packages/shell`，并仅在需要表达体验模式时，对共享 SDK 做最小的类型或配置调整。

**技术栈：** React、TypeScript、Vite、现有 `@icp-platform/sdk` 以及 shell 包结构

---

### 任务 1：增加 Shell 体验模式与静态 UI 配置

**文件：**
- 修改：`frontend/packages/sdk/src/index.ts`
- 修改：`frontend/packages/shell/src/contexts/PlatformContext.tsx`
- 新建：`frontend/packages/shell/src/config/uiConfig.ts`
- 测试：`frontend/packages/shell/src/App.tsx`

- [ ] **步骤 1：定义新的配置结构**

在 `frontend/packages/sdk/src/index.ts` 中增加 Shell 体验模式类型和最小 UI 配置契约。

包含：

```ts
export type ShellExperienceMode = 'hybrid' | 'product' | 'platform';

export interface UiConfig {
  shellExperienceMode: ShellExperienceMode;
}
```

- [ ] **步骤 2：为 Shell 创建静态 UI 配置**

创建 `frontend/packages/shell/src/config/uiConfig.ts`：

```ts
import type { UiConfig } from '@icp-platform/sdk';

export const uiConfig: UiConfig = {
  shellExperienceMode: 'hybrid',
};
```

- [ ] **步骤 3：将配置接入平台上下文**

扩展 `PlatformContext.tsx`，使 Shell 可以读取 `uiConfig.shellExperienceMode`。

预期行为：
- 当前模式可被布局、首页和导航读取
- 不引入后端依赖

- [ ] **步骤 4：运行 Shell 类型检查 / 构建目标**

运行：

```powershell
pnpm -C frontend --filter @icp-platform/shell build
```

预期：
- shell 可以成功编译，或者失败范围仅限于本任务修改过的文件

### 任务 2：引入设计令牌与共享 Shell 表面样式

**文件：**
- 修改：`frontend/packages/shell/src/index.css`
- 修改：`frontend/packages/shell/src/components/Layout.tsx`
- 修改：`frontend/packages/shell/src/components/Header.tsx`
- 修改：`frontend/packages/shell/src/components/Sidebar.tsx`

- [ ] **步骤 1：在 `index.css` 中定义 CSS 变量**

增加如下变量：

```css
:root {
  --color-primary: #1769b5;
  --color-accent: #f29a1f;
  --color-bg: #f4f6f8;
  --color-surface: #ffffff;
  --color-border: #d7dee7;
  --color-text: #1e2a36;
  --color-text-muted: #617083;
  --radius-sm: 8px;
  --radius-md: 12px;
  --shadow-soft: 0 8px 24px rgba(19, 45, 76, 0.06);
}
```

- [ ] **步骤 2：应用 Shell 级页面框架样式**

更新布局相关样式，使 Shell 具备：
- 浅色中性背景
- 白色内容表面
- 清晰边框
- 中等密度间距

- [ ] **步骤 3：更新头部与侧边栏结构**

将新的令牌系统应用到：
- 头部背景与边框
- 侧边栏分区间距
- 当前选中导航状态
- 悬停状态

选中导航应使用主蓝色强调，但不要使用大面积高饱和填充。

- [ ] **步骤 4：重新运行 Shell 构建**

运行：

```powershell
pnpm -C frontend --filter @icp-platform/shell build
```

预期：
- 构建成功

### 任务 3：重新设计登录页

**文件：**
- 修改：`frontend/packages/shell/src/pages/Login.tsx`
- 修改：`frontend/packages/shell/src/index.css`

- [ ] **步骤 1：将登录布局改为双栏结构**

页面应呈现：
- 左侧品牌 / 信息面板
- 右侧登录卡片

左侧面板内容：
- logo 区域占位
- 一句产品说明
- 两条简洁要点，分别表达业务产品能力与平台能力

- [ ] **步骤 2：设计登录卡片与表单样式**

表单应具备：
- 清晰标签
- 中等间距
- 主蓝色提交按钮
- 行内错误提示区域

不要增加额外字段，也不要改动流程。

- [ ] **步骤 3：验证认证行为仍然正常**

运行：

```powershell
pnpm -C frontend --filter @icp-platform/shell build
```

预期：
- 构建成功
- 登录页在现有认证上下文下仍可正常编译

### 任务 4：构建双入口首页门户

**文件：**
- 修改：`frontend/packages/shell/src/pages/Home.tsx`
- 修改：`frontend/packages/shell/src/index.css`

- [ ] **步骤 1：替换当前首页占位内容**

实现一个包含以下结构的首页：
- 页面引导区
- 两张主卡片：`Business Portal`、`Platform Portal`
- 每张卡片 3 到 5 个快捷入口
- 下方一个最近 / 常用操作占位区域

- [ ] **步骤 2：让门户卡片响应体验模式**

行为：
- `hybrid`：显示两张卡片
- `product`：只显示业务优先内容
- `platform`：只显示平台优先内容

- [ ] **步骤 3：保持导航目标简单**

快捷入口可路由到现有页面：
- 业务侧可指向 demo/files 等占位页面
- 平台侧应指向 authorization / config / audit

- [ ] **步骤 4：重新运行 Shell 构建**

运行：

```powershell
pnpm -C frontend --filter @icp-platform/shell build
```

预期：
- 构建成功

### 任务 5：在 Shell 中增加业务 / 平台视图切换

**文件：**
- 修改：`frontend/packages/shell/src/App.tsx`
- 修改：`frontend/packages/shell/src/components/Header.tsx`
- 修改：`frontend/packages/shell/src/components/Sidebar.tsx`

- [ ] **步骤 1：增加当前视图状态**

增加本地 Shell 视图状态：

```ts
type ShellView = 'business' | 'platform';
```

默认行为：
- 在 `hybrid` 模式下，从首页进入并允许切换
- 在 `product` 模式下，强制为 `business`
- 在 `platform` 模式下，强制为 `platform`

- [ ] **步骤 2：增加头部切换 UI**

头部应渲染一个轻量级分段控件或标签页：
- `业务视图`
- `平台视图`

仅在当前模式允许时同时显示两个入口。

- [ ] **步骤 3：按当前视图过滤侧边栏菜单**

先采用一版简单规则：
- platform：authorization、config center、audit、开发者相关路由
- business：demo/files 或业务相关模块

保持过滤逻辑集中管理，便于后续调整。

- [ ] **步骤 4：运行 Shell 构建**

运行：

```powershell
pnpm -C frontend --filter @icp-platform/shell build
```

预期：
- 构建成功

### 任务 6：为模块页面统一共享页面基础元件

**文件：**
- 修改：`frontend/packages/shell/src/index.css`
- 修改：`frontend/modules/authorization/src/pages/RoleManagement.tsx`
- 修改：`frontend/modules/config-center/src/pages/ConfigManagement.tsx`
- 修改：`frontend/modules/audit/src/pages/AccessLogs.tsx`
- 修改：`frontend/modules/audit/src/pages/OperationLogs.tsx`

- [ ] **步骤 1：增加共享工具类**

在 `index.css` 中创建轻量级共享页面类：
- `.page-shell`
- `.page-header`
- `.page-actions`
- `.panel`
- `.data-table`
- `.status-badge`

- [ ] **步骤 2：将这些工具类应用到代表性模块页面**

使用新的基础元件重新整理：
- 一个权限页
- 配置中心页
- 两个审计列表页

目标：
- 样式一致性明显提升
- 不进行深度行为改造

- [ ] **步骤 3：重新运行 Shell 构建**

运行：

```powershell
pnpm -C frontend --filter @icp-platform/shell build
```

预期：
- 构建成功

### 任务 7：记录 Shell 视觉契约

**文件：**
- 修改：`docs/verification/quick-verification.md`
- 新建：`docs/shell-visual-guidelines.md`

- [ ] **步骤 1：编写 Shell 视觉指引**

创建一份简洁文档，覆盖：
- 模式含义
- 首页门户结构
- 色彩角色
- Shell 区域划分
- 共享页面基础元件

- [ ] **步骤 2：扩展快速验证清单**

增加视觉验证点：
- 登录页使用左右分栏布局
- 首页显示正确的门户区块
- product 模式隐藏平台入口
- Shell 视图切换行为正确

- [ ] **步骤 3：重新运行后端与 Shell 验证**

运行：

```powershell
dotnet build ICP-Platform.sln
pnpm -C frontend --filter @icp-platform/shell build
```

预期：
- 后端构建成功
- Shell 构建成功

## 自检

### 规格覆盖

本计划覆盖：
- 静态体验模式
- 登录页改版
- Shell 框架样式
- 首页双入口模型
- 业务 / 平台切换
- 共享基础元件
- 文档补充

### 占位项扫描

没有遗留 `TODO`、`TBD` 或跨任务含糊带过的内容。每个任务都给出了明确文件和验证命令。

### 类型一致性

本计划使用一致的命名集合：
- `ShellExperienceMode`
- `UiConfig`
- `ShellView`

如果实现时发现已有等价类型，应统一朝一个方向重命名，而不是再引入重复定义。
