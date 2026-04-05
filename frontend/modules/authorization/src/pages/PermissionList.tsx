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

  return (
    <div className="sh-page">
      <section className="sh-page-header">
        <div className="sh-page-header-main">
          <span className="sh-page-eyebrow">权限管理</span>
          <h1 className="sh-page-title">权限列表</h1>
          <p className="sh-page-description">
            展示系统中可用权限的代表性视图，按功能类别组织，作为平台侧列表页的统一参考样式。
          </p>
        </div>
        <div className="sh-page-actions">
          <div className="sh-summary-card">
            <span className="sh-summary-card-label">总权限数</span>
            <span className="sh-summary-card-value">{totalPermissions}</span>
            <span className="sh-summary-card-meta">{permissionsByCategory.length} 个类别</span>
          </div>
        </div>
      </section>

      <div className="sh-helper-block">
        <h3 className="sh-helper-block-title">页面说明</h3>
        <p>第二阶段先统一权限目录的展示骨架，权限搜索、批量维护和高级筛选留到后续按真实需求继续补充。</p>
      </div>

      <section className="sh-page-stack">
        {permissionsByCategory.map(({ category, permissions }) => (
          <div key={category} className="sh-page-panel">
            <div className="sh-page-panel-header">
              <div>
                <h2 className="sh-page-panel-title">{category}</h2>
                <div className="sh-page-panel-meta">{permissions.length} 项权限</div>
              </div>
            </div>
            <div className="sh-page-panel-body">
              <div className="sh-table-wrap">
                <table className="sh-table">
                  <thead>
                    <tr>
                      <th>权限代码</th>
                      <th>权限名称</th>
                      <th>类别</th>
                    </tr>
                  </thead>
                  <tbody>
                    {permissions.map((permission) => (
                      <tr key={permission.code}>
                        <td>
                          <code className="sh-code">{permission.code}</code>
                        </td>
                        <td>{permission.name}</td>
                        <td>
                          <span className="sh-status-badge sh-status-badge-neutral">{category}</span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        ))}
      </section>
    </div>
  );
}
