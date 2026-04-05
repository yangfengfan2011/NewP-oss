# 前端架构说明

## 概述

新平台前端采用模块化架构，通过 SDK 实现模块动态注册和组合。

## 目录结构

```
frontend/
├── packages/
│   ├── shell/          # 平台壳（宿主）
│   ├── sdk/            # 前端 SDK
│   ├── ui/             # 共享 UI 组件（待实现）
│   └── core/           # 核心前端能力（待实现）
├── modules/
│   └── demo/           # Demo 模块示例
└── pnpm-workspace.yaml # Workspace 配置
```

## 核心组件

### Shell（平台壳）

职责：
- 提供基础布局（Header、Sidebar、主内容区）
- 管理认证状态和上下文
- 聚合模块路由和菜单
- 提供全局错误边界
- 提供权限守卫

核心文件：
- `src/App.tsx` - 应用入口，路由配置
- `src/components/Layout.tsx` - 主布局
- `src/components/Header.tsx` - 顶部导航
- `src/components/Sidebar.tsx` - 左侧菜单
- `src/contexts/AuthContext.tsx` - 认证上下文
- `src/contexts/PlatformContext.tsx` - 平台上下文
- `src/pages/Login.tsx` - 登录页面

### SDK（前端 SDK）

提供模块接入契约和注册机制。

核心接口：

#### ModuleMetadata
```typescript
interface ModuleMetadata {
  moduleId: string;
  displayName: string;
  version: string;
  description?: string;
}
```

#### RouteConfig
```typescript
interface RouteConfig {
  path: string;
  component: ComponentType;
  exact?: boolean;
  requireAuth?: boolean;
  permissions?: string[];
}
```

#### MenuConfig
```typescript
interface MenuConfig {
  id: string;
  label: string;
  path?: string;
  icon?: string;
  order?: number;
  permissions?: string[];
  children?: MenuConfig[];
}
```

#### PermissionDeclaration
```typescript
interface PermissionDeclaration {
  code: string;
  name: string;
  description?: string;
  category?: string;
}
```

#### I18nResource
```typescript
interface I18nResource {
  locale: string;
  messages: Record<string, string>;
}
```

#### FrontendModule
```typescript
interface FrontendModule {
  metadata: ModuleMetadata;
  routes?: RouteConfig[];
  menus?: MenuConfig[];
  permissions?: PermissionDeclaration[];
  i18n?: I18nResource[];
  initialize?: () => void | Promise<void>;
  cleanup?: () => void | Promise<void>;
}
```

#### ModuleRegistry
静态注册表，提供：
- `register(module)` - 注册模块
- `getModule(moduleId)` - 获取模块
- `getAllModules()` - 获取所有模块
- `getRoutes()` - 获取所有路由
- `getMenus()` - 获取所有菜单（按 order 排序）
- `getPermissions()` - 获取所有权限声明
- `getI18nResources(locale)` - 获取国际化资源
- `initializeAll()` - 初始化所有模块
- `cleanupAll()` - 清理所有模块

## 模块接入流程

### 1. 创建模块目录

```
frontend/modules/your-module/
├── src/
│   ├── pages/          # 页面组件
│   └── index.ts        # 模块入口
├── package.json
└── tsconfig.json
```

### 2. 定义模块

```typescript
// src/index.ts
import { FrontendModule } from '@icp-platform/sdk';
import YourPage from './pages/YourPage';

export const yourModule: FrontendModule = {
  metadata: {
    moduleId: 'your-module',
    displayName: '你的模块',
    version: '0.1.0'
  },
  
  routes: [
    {
      path: '/your-module',
      component: YourPage,
      permissions: ['your-module.view']
    }
  ],
  
  menus: [
    {
      id: 'your-module',
      label: '你的模块',
      path: '/your-module',
      order: 100,
      permissions: ['your-module.view']
    }
  ],
  
  permissions: [
    {
      code: 'your-module.view',
      name: '查看你的模块',
      category: 'YourModule'
    }
  ],
  
  i18n: [
    {
      locale: 'zh-CN',
      messages: {
        'your-module.title': '你的模块'
      }
    }
  ],
  
  initialize: async () => {
    console.log('Your module initialized');
  }
};

export default yourModule;
```

### 3. 在 Shell 中注册

```typescript
// shell/src/main.tsx
import { ModuleRegistry } from '@icp-platform/sdk';
import yourModule from '../../../modules/your-module/src/index';

ModuleRegistry.register(yourModule);
```

### 4. 模块自动生效

- 路由自动注册到 React Router
- 菜单自动出现在侧边栏
- 权限自动集成到权限系统

## 权限系统

### 权限检查

在组件中使用 `PermissionGuard`：

```typescript
<PermissionGuard permissions={['module.action']}>
  <YourComponent />
</PermissionGuard>
```

在代码中使用 `useAuth`：

```typescript
const auth = useAuth();
if (auth.hasPermission('module.action')) {
  // 执行操作
}
```

### 权限配置

- `requireAll: true` - 需要所有权限
- `requireAll: false` - 需要任一权限（默认）

## 认证流程

1. 用户访问需要认证的路由
2. 未认证则重定向到 `/login`
3. 登录成功后跳转回原路由
4. 认证状态保存在 `AuthContext` 中

## 平台上下文

`PlatformContext` 提供：
- `auth` - 认证上下文
- `navigate(path)` - 导航函数
- `showNotification(message, type)` - 显示通知
- `showDialog(content, options)` - 显示对话框

使用方式：

```typescript
import { usePlatform } from '@icp-platform/sdk';

function YourComponent() {
  const platform = usePlatform();
  
  const handleClick = () => {
    platform.showNotification('操作成功', 'success');
    platform.navigate('/other-page');
  };
}
```

## 错误处理

全局错误边界自动捕获组件错误，显示友好错误页面。

## 构建和开发

```bash
# 安装依赖
pnpm install

# 开发模式
pnpm dev

# 构建所有包
pnpm build

# 构建 SDK
pnpm --filter @icp-platform/sdk build

# 构建 Shell
pnpm --filter @icp-platform/shell build
```

## Demo 模块

参考 `frontend/modules/demo` 查看完整的模块接入示例。

Demo 模块展示了：
- 路由注册
- 菜单注册（包含子菜单）
- 权限声明和使用
- 国际化资源
- 模块初始化和清理

## 待实现功能

- [ ] 通知系统（当前使用 console.log）
- [ ] 对话框系统（当前使用 window.confirm）
- [ ] 国际化切换
- [ ] 主题切换
- [ ] 共享 UI 组件库
- [ ] 模块热重载
- [ ] 模块懒加载
- [ ] 实际的后端认证集成

## 兼容性说明

当前架构不包含旧 `.aspx` 页面兼容逻辑。如需兼容旧页面，应：

1. 创建独立的 `compatibility` 区域
2. 通过 iframe 或独立路由加载旧页面
3. 不混入 Shell 核心逻辑
4. 明确标记为临时方案

## 技术栈

- React 18.3
- TypeScript 5.4
- React Router 6.22
- Vite 5.1
- pnpm workspace

## Clean Check

为避免本地 `node_modules`、`dist` 或 Vite 缓存掩盖问题，建议在干净环境下按下面步骤验证前端可构建性：

```bash
# 清理工作区产物和依赖（跨平台）
pnpm clean

# 基于锁文件重新安装
pnpm install --frozen-lockfile

# 重新构建全部 workspace 包
pnpm build
```

说明：
- `pnpm clean` 会清理各 workspace 下的 `dist`、`node_modules` 和 `.vite` 缓存。
- 推荐在提交前或切换机器后至少执行一次这组命令，用来确认 clean 环境下的可构建性。
