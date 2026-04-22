import { FrontendModule } from '@icp-platform/sdk';
import ModuleTemplateSamplePage from './pages/ModuleTemplateSamplePage';

export const moduleTemplateSampleModule: FrontendModule = {
  metadata: {
    moduleId: 'module-template-sample',
    displayName: '模块模板示例',
    version: '0.1.0',
    description: '展示如何创建和集成新的前端模块'
  },
  customPages: [
    {
      pageKey: 'module-template-sample.home.page',
      component: ModuleTemplateSamplePage,
    },
  ],
  // Task 4 先迁移主入口到 metadata + customPages，原 routes 继续保留为 metadata 缺席时的回退。
  routes: [
    {
      path: '/module-template-sample',
      component: ModuleTemplateSamplePage,
      permissions: ['ModuleTemplateSample.View']
    }
  ],
  // menus 同样暂留，直到外部 metadata runtime manifest 生成链路纳入仓库后再统一收紧。
  menus: [
    {
      id: 'module-template-sample',
      label: '模块模板示例',
      icon: '📋',
      path: '/module-template-sample',
      order: 200,
      permissions: ['ModuleTemplateSample.View']
    }
  ],

  permissions: [
    {
      code: 'ModuleTemplateSample.View',
      name: '查看模块模板示例',
      description: '允许查看模块模板示例页面',
      category: '模块模板示例'
    }
  ],

  i18n: [
    {
      locale: 'zh-CN',
      messages: {
        'module-template-sample.title': '模块模板示例',
        'module-template-sample.description': '展示如何创建和集成新的前端模块'
      }
    },
    {
      locale: 'en-US',
      messages: {
        'module-template-sample.title': 'Module Template Sample',
        'module-template-sample.description': 'Demonstrates how to create and integrate new frontend modules'
      }
    }
  ],

  initialize: async () => {
    console.log('Module Template Sample initialized');
  },

  cleanup: async () => {
    console.log('Module Template Sample cleanup');
  }
};

export default moduleTemplateSampleModule;
