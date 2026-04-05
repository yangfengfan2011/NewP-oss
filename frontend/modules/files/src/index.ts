import { FrontendModule } from '@icp-platform/sdk';
import FilesHome from './pages/FilesHome';

export const filesModule: FrontendModule = {
  metadata: {
    moduleId: 'files',
    displayName: '文件管理',
    version: '0.1.0',
    description: '文件上传、下载和存储管理'
  },

  routes: [
    {
      path: '/files',
      component: FilesHome,
      permissions: ['Files.View']
    }
  ],

  menus: [
    {
      id: 'files',
      label: '文件管理',
      icon: '📁',
      path: '/files',
      order: 50,
      permissions: ['Files.View']
    }
  ],

  permissions: [
    {
      code: 'Files.Upload',
      name: '上传文件',
      description: '允许上传文件',
      category: '文件管理'
    },
    {
      code: 'Files.Download',
      name: '下载文件',
      description: '允许下载文件',
      category: '文件管理'
    },
    {
      code: 'Files.Delete',
      name: '删除文件',
      description: '允许删除文件',
      category: '文件管理'
    },
    {
      code: 'Files.View',
      name: '查看文件',
      description: '允许查看文件列表和元数据',
      category: '文件管理'
    }
  ],

  i18n: [
    {
      locale: 'zh-CN',
      messages: {
        'files.title': '文件管理',
        'files.upload': '上传文件',
        'files.download': '下载文件',
        'files.delete': '删除文件',
        'files.list': '文件列表'
      }
    },
    {
      locale: 'en-US',
      messages: {
        'files.title': 'File Management',
        'files.upload': 'Upload File',
        'files.download': 'Download File',
        'files.delete': 'Delete File',
        'files.list': 'File List'
      }
    }
  ],

  initialize: async () => {
    console.log('Files module initialized');
  },

  cleanup: async () => {
    console.log('Files module cleanup');
  }
};

export default filesModule;
