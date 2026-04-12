import { FrontendModule } from '@icp-platform/sdk';
import OrganizationTreePage from './pages/OrganizationTreePage';

export const organizationModule: FrontendModule = {
  metadata: {
    moduleId: 'organization',
    displayName: '组织结构',
    version: '0.1.0',
    description: '组织结构管理模块'
  },
  routes: [
    {
      path: '/organization/tree',
      component: OrganizationTreePage,
      requireAuth: true,
      permissions: ['Organization.Person.View']
    }
  ],
  menus: [
    {
      id: 'organization',
      label: '组织结构',
      path: '/organization/tree',
      icon: '🏢',
      order: 150,
      permissions: ['Organization.Person.View']
    }
  ],
  permissions: [
    {
      code: 'Organization.Person.View',
      name: '查看人员',
      description: '允许查看组织结构中的人员信息',
      category: '组织结构'
    }
  ]
};

export default organizationModule;
