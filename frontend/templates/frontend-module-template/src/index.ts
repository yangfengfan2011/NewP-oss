import { FrontendModule } from '@icp-platform/sdk';
import TemplatePage from './pages/TemplatePage';

export const templateModule: FrontendModule = {
  metadata: {
    moduleId: 'TemplateModule',
    displayName: '模板模块',
    version: '0.1.0',
    description: '用于复制新前端模块的最小骨架'
  },
  routes: [
    {
      path: '/template-module',
      component: TemplatePage,
      requireAuth: true,
      permissions: ['TemplateModule.View']
    }
  ],
  menus: [
    {
      id: 'template-module',
      label: '模板模块',
      path: '/template-module',
      order: 100,
      permissions: ['TemplateModule.View']
    }
  ],
  permissions: [
    {
      code: 'TemplateModule.View',
      name: '查看模板模块',
      category: '模板模块'
    }
  ],
  initialize: async () => {
    console.log('模板模块初始化完成');
  },
  cleanup: async () => {
    console.log('模板模块清理完成');
  }
};

export default templateModule;
