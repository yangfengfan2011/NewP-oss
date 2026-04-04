import { usePermission } from '@icp-platform/core';

export default function RoleManagement() {
  const canCreate = usePermission('Authorization.Role.Create');
  const canEdit = usePermission('Authorization.Role.Edit');
  const canDelete = usePermission('Authorization.Role.Delete');

  return (
    <div>
      <h1>角色管理</h1>
      <p>权限检查示例：</p>
      <ul>
        <li>可以创建: {canCreate ? '是' : '否'}</li>
        <li>可以编辑: {canEdit ? '是' : '否'}</li>
        <li>可以删除: {canDelete ? '是' : '否'}</li>
      </ul>
      {canCreate && (
        <button>创建角色</button>
      )}
    </div>
  );
}
