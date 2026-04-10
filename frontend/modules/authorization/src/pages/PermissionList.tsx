import { PageHeader, SummaryCard, Panel, Badge, TableWrap } from '@icp-platform/ui';
import type { TableColumn } from '@icp-platform/ui';

interface Permission {
  code: string;
  name: string;
}

export default function PermissionList() {
  const permissionsByCategory = [
    {
      category: '角色管理',
      permissions: [
        { code: 'Authorization.Role.View', name: '查看角色' },
        { code: 'Authorization.Role.Create', name: '创建角色' },
        { code: 'Authorization.Role.Edit', name: '编辑角色' },
        { code: 'Authorization.Role.Delete', name: '删除角色' },
      ],
    },
    {
      category: '权限管理',
      permissions: [
        { code: 'Authorization.Permission.View', name: '查看权限' },
        { code: 'Authorization.Permission.Assign', name: '分配权限' },
      ],
    },
    {
      category: '用户角色',
      permissions: [
        { code: 'Authorization.User.AssignRole', name: '分配用户角色' },
      ],
    },
    {
      category: '操作日志',
      permissions: [{ code: 'Audit.OperationLog.View', name: '查看操作日志' }],
    },
    {
      category: '访问日志',
      permissions: [{ code: 'Audit.AccessLog.View', name: '查看访问日志' }],
    },
    {
      category: '配置管理',
      permissions: [
        { code: 'ConfigCenter.Config.View', name: '查看配置' },
        { code: 'ConfigCenter.Config.Create', name: '创建配置' },
        { code: 'ConfigCenter.Config.Edit', name: '编辑配置' },
        { code: 'ConfigCenter.Config.Delete', name: '删除配置' },
      ],
    },
  ];

  const totalPermissions = permissionsByCategory.reduce(
    (sum, category) => sum + category.permissions.length,
    0,
  );

  const makeColumns = (category: string): TableColumn<Permission>[] => [
    {
      key: 'code',
      title: '权限代码',
      render: (p) => <code className="sh-code">{p.code}</code>,
    },
    { key: 'name', title: '权限名称' },
    {
      key: 'category',
      title: '类别',
      render: () => <Badge variant="neutral">{category}</Badge>,
    },
  ];

  return (
    <div className="sh-page">
      <PageHeader
        eyebrow="权限管理"
        title="权限列表"
        description="展示系统中可用权限的代表性视图，按功能类别组织，作为平台侧列表页的统一参考样式。"
        actions={
          <SummaryCard
            label="总权限数"
            value={totalPermissions}
            meta={`${permissionsByCategory.length} 个类别`}
          />
        }
      />

      <div className="sh-helper-block">
        <h3 className="sh-helper-block-title">页面说明</h3>
        <p>第二阶段先统一权限目录的展示骨架，权限搜索、批量维护和高级筛选留到后续按真实需求继续补充。</p>
      </div>

      <section className="sh-page-stack">
        {permissionsByCategory.map(({ category, permissions }) => (
          <Panel
            key={category}
            title={category}
            meta={`${permissions.length} 项权限`}
          >
            <TableWrap
              columns={makeColumns(category)}
              data={permissions}
              rowKey={(p) => p.code}
            />
          </Panel>
        ))}
      </section>
    </div>
  );
}
