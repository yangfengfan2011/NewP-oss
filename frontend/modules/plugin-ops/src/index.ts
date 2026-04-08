import { FrontendModule } from '@icp-platform/sdk';
import PluginOperationsCenter from './pages/PluginOperationsCenter';

const pluginOpsModule: FrontendModule = {
  metadata: {
    moduleId: 'plugin-ops',
    displayName: '插件运维中心',
    version: '1.0.0',
    description: '平台插件部署、状态监控与运维管理模块',
  },
  routes: [
    {
      path: '/plugin-ops',
      component: PluginOperationsCenter,
      requireAuth: true,
    },
  ],
  menus: [
    {
      id: 'plugin-ops',
      label: '插件运维',
      icon: 'server',
      order: 30,
      children: [
        {
          id: 'plugin-ops-center',
          label: '运维中心',
          path: '/plugin-ops',
          icon: 'view-grid',
          order: 1,
        },
      ],
    },
  ],
};

export default pluginOpsModule;
