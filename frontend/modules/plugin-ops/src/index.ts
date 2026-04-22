import { FrontendModule } from '@icp-platform/sdk';
import PluginOperationsCenter from './pages/PluginOperationsCenter';

const pluginOpsModule: FrontendModule = {
  metadata: {
    moduleId: 'plugin-ops',
    displayName: '插件运维中心',
    version: '1.0.0',
    description: '平台插件部署、状态监控与运维管理模块',
  },
  customPages: [
    {
      pageKey: 'plugin-ops.home.page',
      component: PluginOperationsCenter,
    },
  ],
  // Task 4 先迁移主入口到 metadata + customPages，原 routes 继续保留为 metadata 缺席时的回退。
  routes: [
    {
      path: '/plugin-ops',
      component: PluginOperationsCenter,
      requireAuth: true,
    },
  ],
  // menus 同样暂留，直到外部 metadata runtime manifest 生成链路纳入仓库后再统一收紧。
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
