import { useEffect, useState } from 'react';
import { usePermission } from '@icp-platform/core';

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
    return <div className="sh-page-empty">暂无查看配置的权限。</div>;
  }

  if (loading) {
    return <div className="sh-page-loading">配置加载中...</div>;
  }

  if (error) {
    return <div className="sh-page-error">配置加载失败：{error}</div>;
  }

  return (
    <div className="sh-page">
      <section className="sh-page-header">
        <div className="sh-page-header-main">
          <span className="sh-page-eyebrow">配置中心</span>
          <h1 className="sh-page-title">配置管理</h1>
          <p className="sh-page-description">
            按模块与分组查看系统配置项，保留原有加载和编辑行为，并统一到第二阶段的共享页面样式中。
          </p>
        </div>
      </section>

      <div className="sh-section-grid">
        <div className="sh-meta-block">
          <div className="sh-meta-row">
            <span className="sh-meta-label">新增配置</span>
            <span
              className={`sh-status-badge ${
                canCreate ? 'sh-status-badge-success' : 'sh-status-badge-neutral'
              }`}
            >
              {canCreate ? '允许' : '禁止'}
            </span>
          </div>
          <div className="sh-meta-row">
            <span className="sh-meta-label">编辑配置</span>
            <span
              className={`sh-status-badge ${
                canEdit ? 'sh-status-badge-success' : 'sh-status-badge-neutral'
              }`}
            >
              {canEdit ? '允许' : '禁止'}
            </span>
          </div>
          <div className="sh-meta-row">
            <span className="sh-meta-label">删除配置</span>
            <span
              className={`sh-status-badge ${
                canDelete ? 'sh-status-badge-warning' : 'sh-status-badge-neutral'
              }`}
            >
              {canDelete ? '允许' : '禁止'}
            </span>
          </div>
        </div>

        <div className="sh-summary-card">
          <span className="sh-summary-card-label">配置分组</span>
          <span className="sh-summary-card-value">{groups.length}</span>
          <span className="sh-summary-card-meta">
            {groups.reduce((sum, group) => sum + group.items.length, 0)} 个配置项
          </span>
        </div>
      </div>

      <div className="sh-helper-block">
        <h3 className="sh-helper-block-title">页面说明</h3>
        <p>当前阶段优先统一配置页的结构与操作密度，复杂配置编辑器和批量变更能力留到后续再补充。</p>
      </div>

      {groups.map((group) => (
        <section key={`${group.moduleId}-${group.group || 'default'}`} className="sh-page-panel">
          <div className="sh-page-panel-header">
            <div>
              <h2 className="sh-page-panel-title">
                {group.moduleId}
                {group.group ? ` / ${group.group}` : ''}
              </h2>
              <div className="sh-page-panel-meta">共 {group.items.length} 个配置项</div>
            </div>
          </div>
          <div className="sh-page-panel-body">
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
                            <button onClick={handleSave} className="sh-btn sh-btn-primary">
                              保存
                            </button>
                            <button onClick={handleCancel} className="sh-btn">
                              取消
                            </button>
                          </div>
                        ) : (
                          canEdit && (
                            <button onClick={() => handleEdit(item)} className="sh-btn">
                              编辑
                            </button>
                          )
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </section>
      ))}

      {groups.length === 0 && <div className="sh-page-empty">未找到配置项。</div>}
    </div>
  );
}
