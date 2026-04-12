import { FrontendModule } from '@icp-platform/sdk';
import PersonDetailPage from './pages/PersonDetailPage';
import PersonListPage from './pages/PersonListPage';
import OrganizationTreePage from './pages/OrganizationTreePage';
import PositionManagementPage from './pages/PositionManagementPage';

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
    },
    {
      path: '/organization/persons',
      component: PersonListPage,
      requireAuth: true,
      permissions: ['Organization.Person.View']
    },
    {
      path: '/organization/persons/:id',
      component: PersonDetailPage,
      requireAuth: true,
      permissions: ['Organization.Person.View']
    },
    {
      path: '/organization/positions',
      component: PositionManagementPage,
      requireAuth: true,
      permissions: ['Organization.Person.View']
    }
  ],
  menus: [
    {
      id: 'organization',
      label: '人员中心',
      path: '/organization/persons',
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
