import { FrontendModule } from '@icp-platform/sdk';
import DemoHome from './pages/DemoHome';
import DemoList from './pages/DemoList';

export const demoModule: FrontendModule = {
  metadata: {
    moduleId: 'demo',
    displayName: '演示模块',
    version: '0.1.0',
    description: '展示模块接入和功能演示'
  },
  customPages: [
    {
      pageKey: 'demo.home.page',
      component: DemoHome,
    },
    {
      pageKey: 'demo.list.page',
      component: DemoList,
    },
  ],
  // Task 4 先迁移主入口到 metadata + customPages，原 routes 继续保留为 metadata 缺席时的回退。
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
  // menus 同样暂留，直到外部 metadata runtime manifest 生成链路纳入仓库后再统一收紧。
  menus: [
    {
      id: 'demo',
      label: '演示模块',
      icon: '📦',
      order: 100,
      children: [
        {
          id: 'demo-home',
          label: '概览',
          path: '/demo',
          order: 1
        },
        {
          id: 'demo-list',
          label: '示例列表',
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
      name: '查看演示列表',
      description: '允许查看演示模块的列表页面',
      category: '演示模块'
    }
  ],

  i18n: [
    {
      locale: 'zh-CN',
      messages: {
        'demo.title': '演示模块',
        'demo.home': '概览',
        'demo.list': '示例列表'
      }
    },
    {
      locale: 'en-US',
      messages: {
        'demo.title': 'Demo Module',
        'demo.home': 'Overview',
        'demo.list': 'Examples'
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
