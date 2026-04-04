import { FrontendModule } from '@icp-platform/sdk';
import OperationLogs from './pages/OperationLogs';
import AccessLogs from './pages/AccessLogs';

const auditModule: FrontendModule = {
  metadata: {
    moduleId: 'Audit',
    displayName: '审计日志',
    version: '1.0.0',
    description: '平台审计与日志管理模块'
  },
  routes: [
    {
      path: '/audit/operation-logs',
      component: OperationLogs,
      requireAuth: true,
      permissions: ['Audit.OperationLog.View']
    },
    {
      path: '/audit/access-logs',
      component: AccessLogs,
      requireAuth: true,
      permissions: ['Audit.AccessLog.View']
    }
  ],
  menus: [
    {
      id: 'audit',
      label: '审计日志',
      icon: 'document-text',
      order: 20,
      permissions: ['Audit.OperationLog.View'],
      children: [
        {
          id: 'audit-operation-logs',
          label: '操作日志',
          path: '/audit/operation-logs',
          icon: 'clipboard-list',
          order: 1,
          permissions: ['Audit.OperationLog.View']
        },
        {
          id: 'audit-access-logs',
          label: '访问日志',
          path: '/audit/access-logs',
          icon: 'globe-alt',
          order: 2,
          permissions: ['Audit.AccessLog.View']
        }
      ]
    }
  ],
  permissions: [
    { code: 'Audit.OperationLog.View', name: '查看操作日志', category: '操作日志' },
    { code: 'Audit.AccessLog.View', name: '查看访问日志', category: '访问日志' }
  ]
};

export default auditModule;
