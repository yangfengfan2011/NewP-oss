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
        body: JSON.stringify({ value: newValue })
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
    return <div>No permission to view config.</div>;
  }

  if (loading) {
    return <div>Loading...</div>;
  }

  if (error) {
    return <div style={{ color: 'red' }}>Error: {error}</div>;
  }

  return (
    <div style={{ padding: '20px' }}>
      <h1>Config Management</h1>

      <div style={{ marginBottom: '16px', color: '#666' }}>
        {canCreate ? 'Create permission granted' : 'No create permission'}
        {' · '}
        {canDelete ? 'Delete permission granted' : 'No delete permission'}
      </div>

      {groups.map(group => (
        <div key={`${group.moduleId}-${group.group || 'default'}`} style={{ marginBottom: '30px' }}>
          <h2>
            {group.moduleId}
            {group.group && ` - ${group.group}`}
          </h2>

          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr style={{ borderBottom: '2px solid #ddd' }}>
                <th style={{ padding: '10px', textAlign: 'left' }}>Key</th>
                <th style={{ padding: '10px', textAlign: 'left' }}>Display Name</th>
                <th style={{ padding: '10px', textAlign: 'left' }}>Value</th>
                <th style={{ padding: '10px', textAlign: 'left' }}>Type</th>
                <th style={{ padding: '10px', textAlign: 'left' }}>Environment</th>
                <th style={{ padding: '10px', textAlign: 'left' }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {group.items.map(item => (
                <tr key={item.id} style={{ borderBottom: '1px solid #eee' }}>
                  <td style={{ padding: '10px' }}>{item.key}</td>
                  <td style={{ padding: '10px' }}>{item.displayName}</td>
                  <td style={{ padding: '10px' }}>
                    {editingItem?.id === item.id ? (
                      <input
                        type="text"
                        value={newValue}
                        onChange={(e) => setNewValue(e.target.value)}
                        style={{ width: '100%', padding: '5px' }}
                      />
                    ) : (
                      <span>{item.value || '-'}</span>
                    )}
                  </td>
                  <td style={{ padding: '10px' }}>{item.type}</td>
                  <td style={{ padding: '10px' }}>{item.environment || 'Global'}</td>
                  <td style={{ padding: '10px' }}>
                    {editingItem?.id === item.id ? (
                      <>
                        <button onClick={handleSave} style={{ marginRight: '5px' }}>
                          Save
                        </button>
                        <button onClick={handleCancel}>Cancel</button>
                      </>
                    ) : (
                      canEdit && <button onClick={() => handleEdit(item)}>Edit</button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ))}

      {groups.length === 0 && <p>No config items found.</p>}
    </div>
  );
}
