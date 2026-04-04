import { FrontendModule } from '@icp-platform/sdk';
import ConfigManagement from './pages/ConfigManagement';

const configCenterModule: FrontendModule = {
  metadata: {
    moduleId: 'ConfigCenter',
    displayName: '配置中心',
    version: '1.0.0',
    description: '平台统一配置管理模块'
  },
  routes: [
    {
      path: '/config/items',
      component: ConfigManagement,
      requireAuth: true,
      permissions: ['ConfigCenter.Config.View']
    }
  ],
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
