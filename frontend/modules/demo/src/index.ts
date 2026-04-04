import { FrontendModule } from '@icp-platform/sdk';
import DemoHome from './pages/DemoHome';
import DemoList from './pages/DemoList';

export const demoModule: FrontendModule = {
  metadata: {
    moduleId: 'demo',
    displayName: 'Demo 模块',
    version: '0.1.0',
    description: '演示模块接入的示例模块'
  },

  routes: [
    {
      path: '/demo',
      component: DemoHome
    },
    {
      path: '/demo/list',
      component: DemoList,
      permissions: ['Demo.View']
    }
  ],

  menus: [
    {
      id: 'demo',
      label: 'Demo 模块',
      icon: '📦',
      order: 100,
      children: [
        {
          id: 'demo-home',
          label: '模块首页',
          path: '/demo',
          order: 1
        },
        {
          id: 'demo-list',
          label: '列表页面',
          path: '/demo/list',
          order: 2,
          permissions: ['Demo.View']
        }
      ]
    }
  ],

  permissions: [
    {
      code: 'Demo.View',
      name: '查看 Demo 列表',
      description: '允许查看 Demo 模块的列表页面',
      category: 'Demo'
    }
  ],

  i18n: [
    {
      locale: 'zh-CN',
      messages: {
        'demo.title': 'Demo 模块',
        'demo.home': '模块首页',
        'demo.list': '列表页面'
      }
    },
    {
      locale: 'en-US',
      messages: {
        'demo.title': 'Demo Module',
        'demo.home': 'Home',
        'demo.list': 'List'
      }
    }
  ],

  initialize: async () => {
    console.log('Demo module initialized');
  },

  cleanup: async () => {
    console.log('Demo module cleanup');
  }
};

export default demoModule;
