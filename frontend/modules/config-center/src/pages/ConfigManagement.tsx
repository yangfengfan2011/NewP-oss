import { useEffect, useState } from 'react';
import { usePermission } from '@icp-platform/core';
import { PageHeader, SummaryCard, Panel, Badge, Button, LoadingState, ErrorState, EmptyState } from '@icp-platform/ui';

interface ConfigItem {
  id: string;
  key: string;
  value?: string;
  type: string;
  moduleId: string;
  group?: string;
  environment?: string;
  displayName: string;
}

interface ConfigGroup {
  moduleId: string;
  group?: string;
  items: ConfigItem[];
}

export default function ConfigManagement() {
  const canView = usePermission('ConfigCenter.Config.View');
  const canCreate = usePermission('ConfigCenter.Config.Create');
  const canEdit = usePermission('ConfigCenter.Config.Edit');
  const canDelete = usePermission('ConfigCenter.Config.Delete');

  const [groups, setGroups] = useState<ConfigGroup[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editingItem, setEditingItem] = useState<ConfigItem | null>(null);
  const [newValue, setNewValue] = useState('');

  useEffect(() => {
    if (canView) {
      void loadConfigs();
    }
  }, [canView]);

  const loadConfigs = async () => {
    try {
      setLoading(true);
      setError(null);
      const response = await fetch('/api/config/grouped');
      if (!response.ok) throw new Error('Failed to load config');
      const data = await response.json();
      setGroups(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unknown error');
    } finally {
      setLoading(false);
    }
  };

  const handleEdit = (item: ConfigItem) => {
    setEditingItem(item);
    setNewValue(item.value || '');
  };

  const handleSave = async () => {
    if (!editingItem) return;

    try {
      const response = await fetch(`/api/config/${editingItem.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ value: newValue }),
      });

      if (!response.ok) throw new Error('Failed to update config');

      setEditingItem(null);
      setNewValue('');
      await loadConfigs();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unknown error');
    }
  };

  const handleCancel = () => {
    setEditingItem(null);
    setNewValue('');
  };

  if (!canView) {
    return <EmptyState message="暂无查看配置的权限。" />;
  }

  if (loading) {
    return <LoadingState message="配置加载中..." />;
  }

  if (error) {
    return <ErrorState message={`配置加载失败：${error}`} />;
  }

  return (
    <div className="sh-page">
      <PageHeader
        eyebrow="配置中心"
        title="配置管理"
        description="按模块与分组查看系统配置项，保留原有加载和编辑行为，并统一到第二阶段的共享页面样式中。"
      />

      <div className="sh-section-grid">
        <div className="sh-meta-block">
          <div className="sh-meta-row">
            <span className="sh-meta-label">新增配置</span>
            <Badge variant={canCreate ? 'success' : 'neutral'}>
              {canCreate ? '允许' : '禁止'}
            </Badge>
          </div>
          <div className="sh-meta-row">
            <span className="sh-meta-label">编辑配置</span>
            <Badge variant={canEdit ? 'success' : 'neutral'}>
              {canEdit ? '允许' : '禁止'}
            </Badge>
          </div>
          <div className="sh-meta-row">
            <span className="sh-meta-label">删除配置</span>
            <Badge variant={canDelete ? 'warning' : 'neutral'}>
              {canDelete ? '允许' : '禁止'}
            </Badge>
          </div>
        </div>

        <SummaryCard
          label="配置分组"
          value={groups.length}
          meta={`${groups.reduce((sum, group) => sum + group.items.length, 0)} 个配置项`}
        />
      </div>

      <div className="sh-helper-block">
        <h3 className="sh-helper-block-title">页面说明</h3>
        <p>当前阶段优先统一配置页的结构与操作密度，复杂配置编辑器和批量变更能力留到后续再补充。</p>
      </div>

      {groups.map((group) => (
        <Panel
          key={`${group.moduleId}-${group.group || 'default'}`}
          title={`${group.moduleId}${group.group ? ` / ${group.group}` : ''}`}
          meta={`共 ${group.items.length} 个配置项`}
        >
          <div className="sh-table-wrap">
            <table className="sh-table">
              <thead>
                <tr>
                  <th>配置键</th>
                  <th>显示名称</th>
                  <th>当前值</th>
                  <th>类型</th>
                  <th>环境</th>
                  <th>操作</th>
                </tr>
              </thead>
              <tbody>
                {group.items.map((item) => (
                  <tr key={item.id}>
                    <td className="sh-code">{item.key}</td>
                    <td>{item.displayName}</td>
                    <td>
                      {editingItem?.id === item.id ? (
                        <input
                          type="text"
                          value={newValue}
                          onChange={(e) => setNewValue(e.target.value)}
                          className="sh-form-input"
                        />
                      ) : (
                        <span>{item.value || '-'}</span>
                      )}
                    </td>
                    <td>{item.type}</td>
                    <td>{item.environment || '全局'}</td>
                    <td>
                      {editingItem?.id === item.id ? (
                        <div className="sh-form-inline">
                          <Button variant="primary" onClick={handleSave}>保存</Button>
                          <Button onClick={handleCancel}>取消</Button>
                        </div>
                      ) : (
                        canEdit && (
                          <Button onClick={() => handleEdit(item)}>编辑</Button>
                        )
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Panel>
      ))}

      {groups.length === 0 && <EmptyState message="未找到配置项。" />}
    </div>
  );
}
