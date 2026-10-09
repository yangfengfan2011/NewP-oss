import { useEffect } from 'react';
import { useLocalization } from '@icp-platform/core';
import type { StandardCrudActionInputRenderContext, StandardCrudFieldDefinition } from '@icp-platform/sdk';
import { StandardCrudForm } from '@icp-platform/ui/crud-form';

/** 只持有业务字段；Drawer、草稿保护、提交错误与成功刷新由共享动作宿主处理。 */
export default function WorkspaceObjectCreateInput({ action, value, onChange, onDraftStateChange, submitting }: StandardCrudActionInputRenderContext) {
  const { t } = useLocalization();
  const dictionary = action.key === 'createPart' ? 'Part' : 'Document';
  const values = typeof value === 'object' && value !== null ? value as Record<string, unknown> : {};
  const dirty = Object.values(values).some(item => typeof item === 'string' && item.length > 0);
  const ready = ['ECODE', 'ENAME'].every(key => typeof values[key] === 'string' && String(values[key]).trim().length > 0);
  useEffect(() => {
    onDraftStateChange?.({ dirty, ready: ready && !submitting });
  }, [dirty, ready, submitting, onDraftStateChange]);
  const fields: StandardCrudFieldDefinition[] = [
    { key: 'ECODE', modelField: 'ECODE', labelKey: t(`${dictionary}.Code`, '编码'), kind: 'text', required: true },
    { key: 'ENAME', modelField: 'ENAME', labelKey: t(`${dictionary}.Name`, '名称'), kind: 'text', required: true },
    { key: 'Description', modelField: 'Description', labelKey: t(`${dictionary}.Field_Description`, '说明'), kind: 'textarea' },
  ];
  return <StandardCrudForm formId={`workspace-${action.key}`} mode="create" fields={fields} sections={[]} values={values}
    onChange={(key, next) => onChange({ ...values, [key]: next })} onSubmit={() => undefined}
    saving={submitting ?? false} showSubmitAction={false} titleForSection={() => t('Workspace.Basic', '基本信息')} />;
}
