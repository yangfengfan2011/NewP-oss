import { useEffect, useMemo, useState } from 'react';
import { usePermission } from '@icp-platform/core';
import {
  PageHeader, SummaryCard, Panel, Button, Input, Textarea,
  EmptyState, LoadingState, ErrorState, ConfirmDialog,
} from '@icp-platform/ui';

interface ValueListItem {
  id: string;
  code: string;
  name: string;
  description?: string;
  createdAt: string;
  updatedAt: string;
  updatedBy?: string;
}

interface ValueListFormState {
  code: string;
  name: string;
  description: string;
}

const EMPTY_FORM: ValueListFormState = {
  code: '',
  name: '',
  description: ''
};

export default function ValueListManagement() {
  const canView = usePermission('ConfigCenter.Config.View');
  const canCreate = usePermission('ConfigCenter.Config.Create');
  const canEdit = usePermission('ConfigCenter.Config.Edit');
  const canDelete = usePermission('ConfigCenter.Config.Delete');

  const [items, setItems] = useState<ValueListItem[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [isEditorOpen, setIsEditorOpen] = useState(false);
  const [mode, setMode] = useState<'create' | 'edit'>('create');
  const [form, setForm] = useState<ValueListFormState>(EMPTY_FORM);
  const [confirmOpen, setConfirmOpen] = useState(false);

  const selectedItem = useMemo(
    () => items.find((item) => item.id === selectedId) ?? null,
    [items, selectedId]
  );

  useEffect(() => {
    if (canView) {
      void loadItems();
    }
  }, [canView]);

  async function loadItems() {
    try {
      setLoading(true);
      setError(null);

      const response = await fetch('/api/value-lists');
      if (!response.ok) {
        throw new Error('值列表加载失败');
      }

      const data = (await response.json()) as ValueListItem[];
      setItems(data);

      if (selectedId && !data.some((item) => item.id === selectedId)) {
        setSelectedId(null);
      }
    } catch (fetchError) {
      setError(fetchError instanceof Error ? fetchError.message : '值列表加载失败');
    } finally {
      setLoading(false);
    }
  }

  function startCreate() {
    setMode('create');
    setForm(EMPTY_FORM);
    setFormError(null);
    setIsEditorOpen(true);
  }

  function startEdit() {
    if (!selectedItem) {
      return;
    }

    setMode('edit');
    setForm({
      code: selectedItem.code,
      name: selectedItem.name,
      description: selectedItem.description ?? ''
    });
    setFormError(null);
    setIsEditorOpen(true);
  }

  function closeEditor() {
    setIsEditorOpen(false);
    setForm(EMPTY_FORM);
    setFormError(null);
  }

  async function submitForm() {
    if (!form.code.trim() || !form.name.trim()) {
      setFormError('编码和名称不能为空。');
      return;
    }

    try {
      setSubmitting(true);
      setFormError(null);

      const payload = {
        code: form.code.trim(),
        name: form.name.trim(),
        description: form.description.trim() || undefined
      };

      const response = await fetch(
        mode === 'create' ? '/api/value-lists' : `/api/value-lists/${selectedItem?.id}`,
        {
          method: mode === 'create' ? 'POST' : 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload)
        }
      );

      if (!response.ok) {
        const problem = await tryReadMessage(response);
        throw new Error(problem ?? '值列表保存失败');
      }

      await loadItems();
      closeEditor();
    } catch (submitError) {
      setFormError(submitError instanceof Error ? submitError.message : '值列表保存失败');
    } finally {
      setSubmitting(false);
    }
  }

  async function removeSelected() {
    if (!selectedItem) {
      return;
    }

    try {
      setSubmitting(true);
      setError(null);
      setConfirmOpen(false);

      const response = await fetch(`/api/value-lists/${selectedItem.id}`, {
        method: 'DELETE'
      });

      if (!response.ok) {
        const problem = await tryReadMessage(response);
        throw new Error(problem ?? '值列表删除失败');
      }

      setSelectedId(null);
      await loadItems();
      closeEditor();
    } catch (deleteError) {
      setError(deleteError instanceof Error ? deleteError.message : '值列表删除失败');
    } finally {
      setSubmitting(false);
    }
  }

  if (!canView) {
    return <EmptyState message="暂无查看值列表的权限。" />;
  }

  if (loading) {
    return <LoadingState message="值列表加载中..." />;
  }

  return (
    <div className="sh-page">
      <ConfirmDialog
        open={confirmOpen}
        onConfirm={() => void removeSelected()}
        onCancel={() => setConfirmOpen(false)}
        title="确认删除"
        message={`确认删除值列表项"${selectedItem?.name}"吗？`}
        variant="danger"
        confirmText="删除"
      />

      <PageHeader
        eyebrow="配置中心"
        title="值列表管理"
        description="管理系统值列表基础数据，支持新建、编辑、删除和刷新，并在表格中展示当前数据库数据。"
        actions={
          <>
            {canCreate && (
              <Button variant="primary" onClick={startCreate}>新建</Button>
            )}
            {canEdit && (
              <Button onClick={startEdit} disabled={!selectedItem || submitting}>编辑</Button>
            )}
            {canDelete && (
              <Button
                onClick={() => setConfirmOpen(true)}
                disabled={!selectedItem || submitting}
              >
                删除
              </Button>
            )}
            <Button onClick={() => void loadItems()} disabled={submitting}>刷新</Button>
          </>
        }
      />

      <div className="sh-section-grid">
        <SummaryCard
          label="值列表数量"
          value={items.length}
          meta={selectedItem ? `当前选中：${selectedItem.name}` : '请选择一条记录进行编辑或删除'}
        />
        <div className="sh-helper-block">
          <h3 className="sh-helper-block-title">使用说明</h3>
          <p>先在表格中选择一条记录，再点击编辑或删除。新建和编辑都使用下方同一套录入表单。</p>
        </div>
      </div>

      {error && <ErrorState message={error} />}

      {isEditorOpen && (
        <Panel
          title={mode === 'create' ? '新建值列表项' : '编辑值列表项'}
          meta="维护编码、名称和备注说明"
        >
          {formError && <div className="sh-form-error">{formError}</div>}
          <div className="sh-page-stack">
            <Input
              label="编码"
              value={form.code}
              onChange={(e) => setForm((current) => ({ ...current, code: e.target.value }))}
            />
            <Input
              label="名称"
              value={form.name}
              onChange={(e) => setForm((current) => ({ ...current, name: e.target.value }))}
            />
            <Textarea
              label="备注"
              rows={4}
              value={form.description}
              onChange={(e) => setForm((current) => ({ ...current, description: e.target.value }))}
            />
            <div className="sh-form-inline">
              <Button variant="primary" onClick={() => void submitForm()} disabled={submitting}>
                保存
              </Button>
              <Button onClick={closeEditor} disabled={submitting}>取消</Button>
            </div>
          </div>
        </Panel>
      )}

      <Panel title="值列表数据" meta="表格展示数据库中的值列表记录">
        <div className="sh-table-wrap">
          <table className="sh-table">
            <thead>
              <tr>
                <th>选择</th>
                <th>编码</th>
                <th>名称</th>
                <th>备注</th>
                <th>更新时间</th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={item.id}>
                  <td>
                    <input
                      type="radio"
                      name="value-list-item"
                      checked={item.id === selectedId}
                      onChange={() => setSelectedId(item.id)}
                    />
                  </td>
                  <td className="sh-code">{item.code}</td>
                  <td>{item.name}</td>
                  <td>{item.description || '-'}</td>
                  <td>{formatDateTime(item.updatedAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {items.length === 0 && <EmptyState message="暂无值列表数据。" />}
      </Panel>
    </div>
  );
}

function formatDateTime(value: string) {
  if (!value) {
    return '-';
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return date.toLocaleString();
}

async function tryReadMessage(response: Response) {
  try {
    const payload = (await response.json()) as { message?: string };
    return payload.message;
  } catch {
    return null;
  }
}
