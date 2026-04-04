# 前端模块接入指南

## 快速开始

### 1. 创建模块目录结构

```bash
frontend/modules/your-module/
├── src/
│   ├── pages/
│   │   └── YourPage.tsx
│   └── index.ts
├── package.json
└── tsconfig.json
```

### 2. 配置 package.json

```json
{
  "name": "@icp-modules/your-module",
  "version": "0.1.0",
  "type": "module",
  "main": "./dist/index.js",
  "types": "./dist/index.d.ts",
  "scripts": {
    "build": "tsc",
    "dev": "tsc --watch"
  },
  "dependencies": {
    "@icp-platform/sdk": "workspace:*"
  },
  "devDependencies": {
    "@types/react": "^18.3.1",
    "react": "^18.3.1",
    "typescript": "^5.4.2"
  }
}
```

### 3. 配置 tsconfig.json

```json
{
  "compilerOptions": {
    "target": "ES2020",
    "module": "ESNext",
    "lib": ["ES2020", "DOM", "DOM.Iterable"],
    "jsx": "react-jsx",
    "declaration": true,
    "outDir": "./dist",
    "rootDir": "./src",
    "strict": true,
    "moduleResolution": "bundler",
    "esModuleInterop": true,
    "skipLibCheck": true,
    "forceConsistentCasingInFileNames": true
  },
  "include": ["src"],
  "exclude": ["node_modules", "dist"]
}
```

### 4. 创建页面组件

```typescript
// src/pages/YourPage.tsx
export default function YourPage() {
  return (
    <div>
      <h2>你的模块</h2>
      <p>模块内容</p>
    </div>
  );
}
```

### 5. 定义模块入口

```typescript
// src/index.ts
import { FrontendModule } from '@icp-platform/sdk';
import YourPage from './pages/YourPage';

export const yourModule: FrontendModule = {
  metadata: {
    moduleId: 'your-module',
    displayName: '你的模块',
    version: '0.1.0',
    description: '模块描述'
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
      icon: '📦',
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

### 6. 在 Shell 中注册模块

```typescript
// packages/shell/src/main.tsx
import yourModule from '../../../modules/your-module/src/index';

ModuleRegistry.register(yourModule);
```

### 7. 构建和运行

```bash
# 安装依赖
pnpm install

# 构建模块
pnpm --filter @icp-modules/your-module build

# 开发模式
pnpm dev
```

## 完整示例

参考 `frontend/modules/demo` 查看完整的模块实现示例。

## 注意事项

- 模块 ID 必须唯一
- 路由路径不要与其他模块冲突
- 权限代码建议使用 `模块名.资源.操作` 格式
- 菜单 order 越小越靠前
- 所有异步初始化逻辑放在 `initialize` 函数中
