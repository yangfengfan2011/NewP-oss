import { usePermission } from '@icp-platform/core';
import { PageHeader, Button, SummaryCard, Panel, Badge } from '@icp-platform/ui';

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
      <PageHeader
        eyebrow="权限管理"
        title="角色管理"
        description="保留现有权限判断行为，使用共享页面样式呈现角色管理入口与操作状态，让后续角色页继续沿用同一套页面骨架。"
        actions={canCreate ? <Button variant="primary">创建角色</Button> : undefined}
      />

      <div className="sh-section-grid">
        <SummaryCard
          label="已授予能力"
          value={grantedCount}
          meta={`共 ${permissionItems.length} 项角色操作权限`}
        />

        <div className="sh-helper-block">
          <h3 className="sh-helper-block-title">页面说明</h3>
          <p>
            第二阶段先统一页面结构和权限状态表达，不提前进入角色详情编辑器等第三阶段精修内容。
          </p>
        </div>
      </div>

      <Panel title="当前权限状态" meta="以下状态直接反映现有权限检查结果">
        <div className="sh-page-stack">
          {permissionItems.map((item) => (
            <Panel
              key={item.label}
              title={item.label}
              meta={item.detail}
              compact
              actions={
                <Badge variant={item.granted ? 'success' : 'neutral'}>
                  {item.granted ? '已授予' : '未授予'}
                </Badge>
              }
            />
          ))}
        </div>
      </Panel>
    </div>
  );
}
