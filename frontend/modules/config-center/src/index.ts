import { FrontendModule } from '@icp-platform/sdk';
import ConfigManagement from './pages/ConfigManagement';
import ValueListManagement from './pages/ValueListManagement';

const configCenterModule: FrontendModule = {
  metadata: {
    moduleId: 'ConfigCenter',
    displayName: '配置中心',
    version: '1.0.0',
    description: '平台统一配置管理模块'
  },
  customPages: [
    {
      pageKey: 'config-center.items.page',
      component: ConfigManagement,
    },
    {
      pageKey: 'config-center.value-lists.page',
      component: ValueListManagement,
    },
  ],
  // Task 4 先迁移主入口到 metadata + customPages，原 routes 继续保留为 metadata 缺席时的回退。
  routes: [
    {
      path: '/config/items',
      component: ConfigManagement,
      requireAuth: true,
      permissions: ['ConfigCenter.Config.View']
    },
    {
      path: '/config/value-lists',
      component: ValueListManagement,
      requireAuth: true,
      permissions: ['ConfigCenter.Config.View']
    }
  ],
  // menus 同样暂留，直到外部 metadata runtime manifest 生成链路纳入仓库后再统一收紧。
  menus: [
    {
      id: 'config-center',
      label: '配置中心',
      icon: 'settings',
      order: 20,
      permissions: ['ConfigCenter.Config.View'],
      children: [
        {
          id: 'config-items',
          label: '配置管理',
          path: '/config/items',
          icon: 'cog',
          order: 1,
          permissions: ['ConfigCenter.Config.View']
        },
        {
          id: 'value-lists',
          label: '值列表管理',
          path: '/config/value-lists',
          icon: 'list',
          order: 2,
          permissions: ['ConfigCenter.Config.View']
        }
      ]
    }
  ],
  permissions: [
    { code: 'ConfigCenter.Config.View', name: '查看配置', category: '配置管理' },
    { code: 'ConfigCenter.Config.Create', name: '创建配置', category: '配置管理' },
    { code: 'ConfigCenter.Config.Edit', name: '编辑配置', category: '配置管理' },
    { code: 'ConfigCenter.Config.Delete', name: '删除配置', category: '配置管理' }
  ]
};

export default configCenterModule;
