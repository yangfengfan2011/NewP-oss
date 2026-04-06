import { FrontendModule } from '@icp-platform/sdk';
import TemplatePage from './pages/TemplatePage';

/**
 * 前端模块模板
 * 
 * 使用说明：
 * 1. 复制此模板到 frontend/modules/your-module-name
 * 2. 修改 moduleId、displayName 等配置
 * 3. 实现你的页面组件
 * 4. 在主应用中注册模块
 */
export const templateModule: FrontendModule = {
  metadata: {
    moduleId: 'TemplateModule',
    displayName: '模板模块',
    version: '0.1.0',
    description: '前端模块模板，用于快速创建新的业务模块'
  },

  // 路由配置
  routes: [
    {
      path: '/template-module',
      component: TemplatePage,
      permissions: ['TemplateModule.View']
    }
  ],

  // 菜单配置
  menus: [
    {
      id: 'template-module',
      label: '模板模块',
      icon: '📋',
      path: '/template-module',
      order: 999,
      permissions: ['TemplateModule.View']
    }
  ],

  // 权限定义
  permissions: [
    {
      code: 'TemplateModule.View',
      name: '查看模板模块',
      description: '允许访问模板模块页面',
      category: '模板模块'
    }
  ],

  // 国际化资源
  i18n: [
    {
      locale: 'zh-CN',
      messages: {
        'template.title': '模板模块',
        'template.description': '这是一个模板页面'
      }
    },
    {
      locale: 'en-US',
      messages: {
        'template.title': 'Template Module',
        'template.description': 'This is a template page'
      }
    }
  ],

  // 模块初始化（可选）
  initialize: async () => {
    console.log('Template module initialized');
  },

  // 模块清理（可选）
  cleanup: async () => {
    console.log('Template module cleanup');
  }
};

export default templateModule;
