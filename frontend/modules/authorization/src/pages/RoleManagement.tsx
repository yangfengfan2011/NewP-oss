import { usePermission } from '@icp-platform/core';

export default function RoleManagement() {
  const canCreate = usePermission('Authorization.Role.Create');
  const canEdit = usePermission('Authorization.Role.Edit');
  const canDelete = usePermission('Authorization.Role.Delete');

  const permissionItems = [
    { label: '创建角色', granted: canCreate, detail: '控制新增角色入口是否可用' },
    { label: '编辑角色', granted: canEdit, detail: '控制角色配置与权限调整能力' },
    { label: '删除角色', granted: canDelete, detail: '控制角色删除与清理操作' },
  ];

  const grantedCount = permissionItems.filter((item) => item.granted).length;

  return (
    <div className="sh-page">
      <section className="sh-page-header">
        <div className="sh-page-header-main">
          <span className="sh-page-eyebrow">权限管理</span>
          <h1 className="sh-page-title">角色管理</h1>
          <p className="sh-page-description">
            保留现有权限判断行为，使用共享页面样式呈现角色管理入口与操作状态，
            让后续角色页继续沿用同一套页面骨架。
          </p>
        </div>
        <div className="sh-page-actions">
          {canCreate && <button className="sh-btn sh-btn-primary">创建角色</button>}
        </div>
      </section>

      <div className="sh-section-grid">
        <div className="sh-summary-card">
          <span className="sh-summary-card-label">已授予能力</span>
          <span className="sh-summary-card-value">{grantedCount}</span>
          <span className="sh-summary-card-meta">共 {permissionItems.length} 项角色操作权限</span>
        </div>

        <div className="sh-helper-block">
          <h3 className="sh-helper-block-title">页面说明</h3>
          <p>
            第二阶段先统一页面结构和权限状态表达，不提前进入角色详情编辑器等第三阶段精修内容。
          </p>
        </div>
      </div>

      <section className="sh-page-panel">
        <div className="sh-page-panel-header">
          <div>
            <h2 className="sh-page-panel-title">当前权限状态</h2>
            <div className="sh-page-panel-meta">以下状态直接反映现有权限检查结果</div>
          </div>
        </div>
        <div className="sh-page-panel-body">
          <div className="sh-page-stack">
            {permissionItems.map((item) => (
              <div key={item.label} className="sh-page-panel">
                <div className="sh-page-panel-header-compact">
                  <div>
                    <div className="sh-page-panel-title">{item.label}</div>
                    <div className="sh-page-panel-meta">{item.detail}</div>
                  </div>
                  <span
                    className={`sh-status-badge ${
                      item.granted ? 'sh-status-badge-success' : 'sh-status-badge-neutral'
                    }`}
                  >
                    {item.granted ? '已授予' : '未授予'}
                  </span>
                </div>
              </div>
            ))}
          </div>
        </div>
      </section>
    </div>
  );
}
