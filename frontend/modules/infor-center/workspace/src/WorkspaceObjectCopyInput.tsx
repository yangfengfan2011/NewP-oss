import { useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react';
import { useLocalization } from '@icp-platform/core';
import type { StandardCrudActionInputRenderContext, StandardCrudFieldDefinition } from '@icp-platform/sdk';
import { StandardCrudForm } from '@icp-platform/ui/crud-form';
import Alert from 'antd/es/alert';
import Button from 'antd/es/button';
import { workspaceClipboard, type ObjectCopyDraft, type ObjectCopyPreparation } from './workspaceClipboard';
import { workspacePosition } from './workspacePosition';
const noSubscription = () => () => undefined;
const noSnapshot = () => undefined;

/** 来源和目标属于打开时的确认；重读明确更新预填，用户已改值始终由本地草稿保留。 */
export default function WorkspaceObjectCopyInput({ instance, queryScope, featurePosition, value, onChange, onDraftStateChange, submitting }: StandardCrudActionInputRenderContext) {
  const { t } = useLocalization();
  const callbacks = useRef({ onChange, onDraftStateChange }); callbacks.current = { onChange, onDraftStateChange };
  const active = instance?.active;
  const session = useMemo(() => active ? workspaceClipboard(instance) : undefined, [instance, active]);
  const state = useSyncExternalStore(session?.subscribe ?? noSubscription, session?.getSnapshot ?? noSnapshot);
  const [preparation, setPreparation] = useState<ObjectCopyPreparation>();
  const [draft, setDraft] = useState<ObjectCopyDraft>();
  const [failure, setFailure] = useState(false);
  const [reread, setReread] = useState(0);
  const [cancelState, setCancelState] = useState<string>();
  const latestValues = useRef<Record<string, unknown>>({});
  const baseline = useRef<Record<string, unknown>>();
  const baselineContext = useRef<string>();
  const current = value as { values?: Record<string, unknown> } | undefined;
  const values = current?.values ?? {};
  latestValues.current = values;
  const targetKey = JSON.stringify(workspacePosition(queryScope));
  const expired = failure || !!draft && (draft.closed || !instance?.active || state?.clipboard !== draft.source);
  useEffect(() => {
    const controller = new AbortController();
    if (!session) { setFailure(true); return () => controller.abort(); }
    let opened: ObjectCopyDraft;
    try { opened = session.prepareObjectCopy(JSON.parse(targetKey), featurePosition); }
    catch { setFailure(true); return () => controller.abort(); }
    setDraft(opened); setPreparation(undefined); setFailure(false);
    void session.readObjectCopyInput(opened, controller.signal).then(result => {
      const context = JSON.stringify([targetKey, featurePosition]);
      const previous = baselineContext.current === context ? baseline.current : undefined;
      const next = previous && reread > 0 ? Object.fromEntries(Object.entries(result.values).map(([key, initial]) =>
        [key, latestValues.current[key] !== previous[key] ? latestValues.current[key] : initial])) : result.values;
      baseline.current = result.values;
      baselineContext.current = context;
      setPreparation(result); callbacks.current.onChange({ draft: opened, values: next });
    }).catch(() => { if (!controller.signal.aborted) setFailure(true); });
    return () => { controller.abort(); opened.closed = true; };
  }, [session, targetKey, featurePosition, reread]);
  const dirty = !!preparation && Object.keys(values).some(key => values[key] !== preparation.values[key]);
  const ready = !!draft && !!preparation && !expired && state?.phase === 'idle' && !submitting
    && ['ECODE', 'ENAME'].every(key => typeof values[key] === 'string' && String(values[key]).trim().length > 0);
  useEffect(() => { callbacks.current.onDraftStateChange?.({ dirty, ready }); }, [dirty, ready]);
  const fields: StandardCrudFieldDefinition[] = [
    { key: 'ECODE', modelField: 'ECODE', labelKey: t('Part.Code'), kind: 'text', required: true },
    { key: 'ENAME', modelField: 'ENAME', labelKey: t('Part.Name'), kind: 'text', required: true },
    { key: 'Description', modelField: 'Description', labelKey: t('Part.Field_Description'), kind: 'textarea' },
  ];
  return <div style={{ display: 'grid', gap: 16 }}>
    <Alert showIcon type={expired || state?.phase === 'unknown' ? 'warning' : 'info'} message={t(state?.phase === 'unknown' ? 'Workspace.CopyOutcomeUnknown'
      : submitting ? 'Workspace.CopySubmitting' : expired ? 'Workspace.CopyInputExpired' : 'Workspace.ObjectCopyDescription')} />
    {preparation && <dl style={{ display: 'grid', gridTemplateColumns: 'auto minmax(0, 1fr)', gap: '8px 16px', margin: 0 }}>
      <dt>{t('Workspace.FolderCopySource')}</dt><dd style={{ margin: 0, overflowWrap: 'anywhere' }}>{preparation.sourcePath.join(' / ')}</dd>
      <dt>{t('Workspace.FolderCopyTarget')}</dt><dd style={{ margin: 0, overflowWrap: 'anywhere' }}>{preparation.targetPath.join(' / ')}</dd>
    </dl>}
    {preparation && <StandardCrudForm formId="workspace-object-copy" mode="create" fields={fields} sections={[]} values={values}
      onChange={(key, next) => callbacks.current.onChange({ draft, values: { ...values, [key]: next } })}
      onSubmit={() => undefined} saving={expired || submitting || state?.phase === 'unknown'} showSubmitAction={false} titleForSection={() => t('Workspace.Basic')} />}
    {!submitting && !expired && state?.phase === 'idle' && <Button onClick={() => setReread(count => count + 1)}>{t('Workspace.CopyReconfirm')}</Button>}
    {submitting && <Button disabled={!!cancelState} onClick={() => {
      setCancelState('requested');
      void session?.resolveObjectCopy(true).then(result => setCancelState(result.state)).catch(() => setCancelState('unknown'));
    }}>{t('Workspace.CopyCancel')}</Button>}
    {cancelState && <Alert showIcon message={t(`Workspace.CopyCancel_${cancelState}`)} />}
  </div>;
}
