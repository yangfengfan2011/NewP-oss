import { useEffect } from 'react';
import { useLocalization } from '@icp-platform/core';
import type { StandardCrudActionInputRenderContext, StandardCrudFieldDefinition } from '@icp-platform/sdk';
import { StandardCrudForm } from '@icp-platform/ui/crud-form';
import WorkspaceParentAuthorizationOption from './WorkspaceParentAuthorizationOption';
import { workspacePosition } from './workspacePosition';

/** Folder命令选项与模型字段分开；草稿、账号边界和提交生命周期仍交共享动作宿主。 */
export default function WorkspaceFolderCreateInput({ value, queryScope, onChange, onDraftStateChange, submitting }: StandardCrudActionInputRenderContext) {
  const { t } = useLocalization();
  const input = typeof value === 'object' && value !== null ? value as Record<string, unknown> : {};
  const root = workspacePosition(queryScope).linkIds.length === 0;
  const copyParentAuthorization = !root && input.copyParentAuthorization !== false;
  const values = Object.fromEntries(['ENAME', 'Description'].filter(key => input[key] !== undefined).map(key => [key, input[key]]));
  const dirty = Object.values(values).some(item => typeof item === 'string' && item.length > 0) || !root && !copyParentAuthorization;
  const ready = typeof values.ENAME === 'string' && values.ENAME.trim().length > 0 && values.ENAME.length <= 256;
  useEffect(() => { onDraftStateChange?.({ dirty, ready: ready && !submitting }); }, [dirty, ready, submitting, onDraftStateChange]);
  const fields: StandardCrudFieldDefinition[] = [
    { key: 'ENAME', modelField: 'ENAME', labelKey: t('Workspace.Name'), kind: 'text', required: true },
    { key: 'Description', modelField: 'Description', labelKey: t('Workspace.Field_Description'), kind: 'textarea' },
  ];
  return <div style={{ display: 'grid', gap: 16 }}>
    <StandardCrudForm formId="workspace-create-folder" mode="create" fields={fields} sections={[]} values={values}
      onChange={(key, next) => onChange({ ...values, [key]: next, copyParentAuthorization })} onSubmit={() => undefined}
      saving={submitting ?? false} showSubmitAction={false} titleForSection={() => t('Workspace.Basic')} />
    <WorkspaceParentAuthorizationOption root={root} checked={copyParentAuthorization} disabled={submitting}
      onChange={checked => onChange({ ...values, copyParentAuthorization: checked })} />
  </div>;
}
