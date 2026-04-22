import { FrontendModule } from '@icp-platform/sdk';
import RoleManagement from './pages/RoleManagement';
import PermissionList from './pages/PermissionList';

const authorizationModule: FrontendModule = {
  metadata: {
    moduleId: 'Authorization',
    displayName: '权限管理',
    version: '1.0.0',
    description: '平台权限与授权管理模块'
  },
  customPages: [
    {
      pageKey: 'authorization.roles.page',
      component: RoleManagement,
    },
    {
      pageKey: 'authorization.permissions.page',
      component: PermissionList,
    },
  ],
  // Task 4 先迁移主入口到 metadata + customPages，原 routes 继续保留为 metadata 缺席时的回退。
  routes: [
    {
      path: '/authorization/roles',
      component: RoleManagement,
      requireAuth: true,
      permissions: ['Authorization.Role.View']
    },
    {
      path: '/authorization/permissions',
      component: PermissionList,
      requireAuth: true,
      permissions: ['Authorization.Permission.View']
    }
  ],
  // menus 同样暂留，直到外部 metadata runtime manifest 生成链路纳入仓库后再统一收紧。
  menus: [
    {
      id: 'authorization',
      label: '权限管理',
      icon: 'shield',
      order: 10,
      permissions: ['Authorization.Role.View'],
      children: [
        {
          id: 'authorization-roles',
          label: '角色管理',
          path: '/authorization/roles',
          icon: 'user-group',
          order: 1,
          permissions: ['Authorization.Role.View']
        },
        {
          id: 'authorization-permissions',
          label: '权限列表',
          path: '/authorization/permissions',
          icon: 'key',
          order: 2,
          permissions: ['Authorization.Permission.View']
        }
      ]
    }
  ],
  permissions: [
    { code: 'Authorization.Role.View', name: '查看角色', category: '角色管理' },
    { code: 'Authorization.Role.Create', name: '创建角色', category: '角色管理' },
    { code: 'Authorization.Role.Edit', name: '编辑角色', category: '角色管理' },
    { code: 'Authorization.Role.Delete', name: '删除角色', category: '角色管理' },
    { code: 'Authorization.Permission.View', name: '查看权限', category: '权限管理' },
    { code: 'Authorization.Permission.Assign', name: '分配权限', category: '权限管理' },
    { code: 'Authorization.User.AssignRole', name: '分配用户角色', category: '用户角色' }
  ]
};

export default authorizationModule;
