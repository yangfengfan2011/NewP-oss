# Frontend SDK

前端 SDK，为前端模块开发提供便利。

## 核心功能

- 模块注册
- 路由管理
- 菜单管理
- 类型定义

## 使用方式

```typescript
import { ModuleRegistry } from '@icp-platform/sdk';

ModuleRegistry.register({
  metadata: {
    moduleId: 'my-module',
    displayName: '我的模块',
    version: '1.0.0'
  },
  routes: [
    { path: '/my-module', component: MyModuleComponent }
  ],
  menus: [
    { id: 'my-module', label: '我的模块', path: '/my-module' }
  ]
});
```
