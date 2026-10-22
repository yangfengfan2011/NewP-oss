import { useEffect, useState } from 'react';
import { formatRequestError, platformFetch, useLocalization } from '@icp-platform/core';
import { readStandardCrudRequestError, standardCrudRequestHeaders, StandardCrudRequestError, type StandardCrudActionInputRenderContext, type StandardCrudFieldDefinition } from '@icp-platform/sdk';
import { StandardCrudForm } from '@icp-platform/ui/crud-form';

/** 编辑从当前 LID 重新读取完整业务字段；表单不会把列表投影当成可保存的完整对象。 */
export default function WorkspaceObjectEditInput({ row, queryScope, featurePosition, buttonId, action, value, onChange, onDraftStateChange, submitting }: StandardCrudActionInputRenderContext) {
  const { t } = useLocalization();
  const target = row as Record<string, unknown>;
  const folder = target.ObjectModelKey === 'WorkspaceFolder';
  const dictionary = folder ? 'Workspace' : target.ObjectModelKey === 'Part' ? 'Part' : 'Document';
  const position = JSON.stringify({ spaceId: queryScope?.spaceId, rootFolderId: queryScope?.rootFolderId,
    linkIds: JSON.parse(String(queryScope?.linkIds ?? '[]')) as unknown });
  const currentButton = buttonId && buttonId === action.buttonId ? buttonId : undefined;
  const readKey = JSON.stringify([target.LID, position, featurePosition, currentButton]);
  const [loaded, setLoaded] = useState<{ key: string; fields: Record<string, unknown> } | null>(null);
  const initial = loaded?.key === readKey ? loaded.fields : null;
  const [failure, setFailure] = useState<{ key: string; error: unknown } | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    setLoaded(null); setFailure(null);
    void (async () => {
      try {
        if (!currentButton) throw new StandardCrudRequestError('缺少当前编辑声明。', 400);
        const response = await platformFetch(`/api/workspace/content/${encodeURIComponent(String(target.LID))}/edit-values`, {
          method: 'POST', signal: controller.signal, headers: { 'Content-Type': 'application/json', ...standardCrudRequestHeaders({ featurePosition, buttonId: currentButton }) },
          body: JSON.stringify({ position: JSON.parse(position) as unknown }),
        });
        if (!response.ok) throw await readStandardCrudRequestError(response);
        const payload = await response.json() as { fields: Record<string, unknown> };
        if (!controller.signal.aborted) {
          setLoaded({ key: readKey, fields: payload.fields });
          onChange(Object.fromEntries(Object.entries(payload.fields).filter(([field]) => ['ECODE', 'ENAME', 'Description'].includes(field))));
        }
      } catch (error) { if (!controller.signal.aborted) setFailure({ key: readKey, error }); }
    })();
    return () => controller.abort();
  }, [target.LID, position, featurePosition, currentButton, readKey, onChange]);
  const values = typeof value === 'object' && value !== null ? value as Record<string, unknown> : {};
  const baseline = initial && Object.fromEntries(Object.entries(initial).filter(([field]) => ['ECODE', 'ENAME', 'Description'].includes(field)));
  const dirty = baseline !== null && JSON.stringify(values) !== JSON.stringify(baseline);
  const ready = initial !== null && (folder ? ['ENAME'] : ['ECODE', 'ENAME'])
    .every(key => typeof values[key] === 'string' && String(values[key]).trim().length > 0);
  useEffect(() => { onDraftStateChange?.({ dirty, ready: ready && !submitting }); }, [dirty, ready, submitting, onDraftStateChange]);
  if (failure?.key === readKey) return <div role="alert">{formatRequestError(failure.error, t)}</div>;
  if (!initial) return <div role="status">{t('Platform.Loading', '加载中…')}</div>;
  const fields: StandardCrudFieldDefinition[] = [
    ...(!folder ? [{ key: 'ECODE', modelField: 'ECODE', labelKey: t(`${dictionary}.Code`, '编码'), kind: 'text' as const, required: true }] : []),
    { key: 'ENAME', modelField: 'ENAME', labelKey: t(`${dictionary}.Name`, '名称'), kind: 'text', required: true },
    { key: 'Description', modelField: 'Description', labelKey: t(`${dictionary}.Field_Description`, '说明'), kind: 'textarea', maxLength: 500 },
    { key: 'OwnerId', modelField: 'OwnerId', labelKey: t(`${dictionary}.Field_OwnerId`, '所属人'), kind: 'text', readOnly: true },
    { key: 'OwnerDepartmentId', modelField: 'OwnerDepartmentId', labelKey: t(`${dictionary}.Field_OwnerDepartmentId`, '所属部门'), kind: 'text', readOnly: true },
  ];
  return <StandardCrudForm formId="workspace-edit-object" mode="edit" fields={fields} sections={[]}
    values={{ ...values, OwnerId: initial.OwnerId_DisplayValue ?? null, OwnerDepartmentId: initial.OwnerDepartmentId_DisplayValue ?? null }}
    onChange={(key, next) => onChange({ ...values, [key]: next })} onSubmit={() => undefined}
    saving={submitting ?? false} showSubmitAction={false} titleForSection={() => t('Workspace.Basic', '基本信息')} />;
}
