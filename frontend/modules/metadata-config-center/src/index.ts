import { FrontendModule } from '@icp-platform/sdk';
import SettingDefinitionManagement from './pages/SettingDefinitionManagement';

const metadataConfigCenterModule: FrontendModule = {
  metadata: {
    moduleId: 'MetadataConfigCenter',
    displayName: '元数据配置中心',
    version: '1.0.0',
    description: '模块元数据定义层的独立可视化编辑入口'
  },
  routes: [
    {
      path: '/metadata-config/modules',
      component: SettingDefinitionManagement,
      requireAuth: true,
      permissions: ['MetadataConfigCenter.View']
    }
  ],
  menus: [
    {
      id: 'metadata-config-center',
      label: '元数据配置中心',
      icon: 'database',
      order: 21,
      permissions: ['MetadataConfigCenter.View'],
      children: [
        {
          id: 'metadata-modules',
          label: '模块概览',
          path: '/metadata-config/modules',
          icon: 'grid',
          order: 1,
          permissions: ['MetadataConfigCenter.View']
        }
      ]
    }
  ],
  permissions: [
    { code: 'MetadataConfigCenter.View', name: '查看元数据配置中心', category: '元数据配置中心' },
    { code: 'MetadataConfigCenter.Edit', name: '编辑元数据定义', category: '元数据配置中心' },
    { code: 'MetadataConfigCenter.Apply', name: '应用元数据变更', category: '元数据配置中心' },
    { code: 'MetadataConfigCenter.GitSync', name: '同步元数据 Git 变更', category: '元数据配置中心' }
  ]
};

export default metadataConfigCenterModule;
