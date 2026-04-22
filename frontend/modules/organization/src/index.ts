import { FrontendModule } from '@icp-platform/sdk';
import PersonCreatePage from './pages/PersonCreatePage';
import PersonDetailPage from './pages/PersonDetailPage';
import PersonListPage from './pages/PersonListPage';
import OrganizationTreePage from './pages/OrganizationTreePage';
import PositionManagementPage from './pages/PositionManagementPage';
import PersonListStandardListEntry from './standard-list/PersonListStandardListEntry';

export const organizationModule: FrontendModule = {
  metadata: {
    moduleId: 'organization',
    displayName: '组织结构',
    version: '0.1.0',
    description: '组织结构管理模块',
  },
  standardListViews: [
    {
      viewKey: 'organization.person.list',
      component: PersonListStandardListEntry,
    },
  ],
  customPages: [
    {
      pageKey: 'organization.tree.page',
      component: OrganizationTreePage,
    },
    {
      pageKey: 'organization.positions.page',
      component: PositionManagementPage,
    },
  ],
  // Task 4 先让 metadata + host registry 接管主入口；这里保留原 routes 作为 runtime metadata
  // 或外部 manifest 缺席时的兼容回退，同时继续承接详情/新建等非导航子路由。
  routes: [
    {
      path: '/organization/tree',
      component: OrganizationTreePage,
      requireAuth: true,
      permissions: ['Organization.Person.View'],
    },
    {
      path: '/organization/persons',
      component: PersonListPage,
      requireAuth: true,
      permissions: ['Organization.Person.View'],
    },
    {
      path: '/organization/persons/new',
      component: PersonCreatePage,
      requireAuth: true,
      permissions: ['Organization.Person.View'],
    },
    {
      path: '/organization/persons/:id',
      component: PersonDetailPage,
      requireAuth: true,
      permissions: ['Organization.Person.View'],
    },
    {
      path: '/organization/positions',
      component: PositionManagementPage,
      requireAuth: true,
      permissions: ['Organization.Person.View'],
    },
  ],
  // fallback 菜单也补齐组织树和岗位管理，避免 metadata 缺席时出现明显缺页。
  menus: [
    {
      id: 'organization',
      label: '组织结构',
      icon: '🏢',
      order: 150,
      permissions: ['Organization.Person.View'],
      children: [
        {
          id: 'organization-persons',
          label: '人员中心',
          path: '/organization/persons',
          order: 1,
          permissions: ['Organization.Person.View'],
        },
        {
          id: 'organization-tree',
          label: '组织树',
          path: '/organization/tree',
          order: 2,
          permissions: ['Organization.Person.View'],
        },
        {
          id: 'organization-positions',
          label: '岗位管理',
          path: '/organization/positions',
          order: 3,
          permissions: ['Organization.Person.View'],
        },
      ],
    },
  ],
  permissions: [
    {
      code: 'Organization.Person.View',
      name: '查看人员',
      description: '允许查看组织结构中的人员信息',
      category: '组织结构',
    },
  ],
};

export default organizationModule;
