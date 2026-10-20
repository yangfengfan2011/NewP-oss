import { useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react';
import { useLocalization } from '@icp-platform/core';
import type { StandardCrudActionInputRenderContext, StandardCrudFieldDefinition } from '@icp-platform/sdk';
import { StandardCrudForm } from '@icp-platform/ui/crud-form';
import Alert from 'antd/es/alert';
import Button from 'antd/es/button';
import { workspaceClipboard, type ObjectCopyDraft, type ObjectCopyPreparation, type ObjectCopyValuesRow } from './workspaceClipboard';
import { workspacePosition } from './workspacePosition';
const noSubscription = () => () => undefined;
const noSnapshot = () => undefined;

/** 来源和目标属于打开时的确认；重读明确更新预填，用户已改值始终由本地草稿保留。 */
export default function WorkspaceObjectCopyInput({ instance, queryScope, featurePosition, dataAccess, value, onChange, onDraftStateChange, submitting }: StandardCrudActionInputRenderContext) {
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
  const currentDraft = useRef<ObjectCopyDraft>();
  const latestValues = useRef<Record<string, unknown>>({});
  const baseline = useRef<Record<string, unknown>>();
  const baselineContext = useRef<string>();
  const baselineSource = useRef<ObjectCopyDraft["source"]>();
  const current = value as { values?: Record<string, unknown> } | undefined;
  const values = current?.values ?? {};
  latestValues.current = values;
  const targetKey = JSON.stringify(workspacePosition(queryScope));
  const declarationId = dataAccess?.id;
  const expired = failure || !!draft && (draft.closed || !instance?.active || state?.clipboard !== draft.source);
  useEffect(() => {
    const controller = new AbortController();
    if (!session) { setFailure(true); return () => controller.abort(); }
    let opened: ObjectCopyDraft;
    try { opened = session.prepareObjectCopy(JSON.parse(targetKey), featurePosition, declarationId); }
    catch { setFailure(true); return () => controller.abort(); }
    currentDraft.current = opened;
    setDraft(opened); setPreparation(undefined); setFailure(false); setCancelState(undefined);
    void session.readObjectCopyInput(opened, controller.signal).then(result => {
      const context = JSON.stringify([targetKey, featurePosition, declarationId]);
      const previous = baselineContext.current === context && baselineSource.current === opened.source ? baseline.current : undefined;
      const merge = (initial: Record<string, unknown>, before?: Record<string, unknown>, current?: Record<string, unknown>) =>
        before && current ? Object.fromEntries(Object.entries(initial).map(([key, value]) => [key, current[key] !== before[key] ? current[key] : value])) : initial;
      const next = result.items ? { items: result.items.map(item => ({ sourceLinkId: item.sourceLinkId,
        values: merge(item.values, (previous?.items as ObjectCopyValuesRow[] | undefined)?.find(row => row.sourceLinkId === item.sourceLinkId)?.values,
          (latestValues.current.items as ObjectCopyValuesRow[] | undefined)?.find(row => row.sourceLinkId === item.sourceLinkId)?.values) })) }
        : previous && reread > 0 ? merge(result.values, previous, latestValues.current) : result.values;
      baseline.current = result.values;
      baselineContext.current = context; baselineSource.current = opened.source;
      setPreparation(result); callbacks.current.onChange({ draft: opened, values: next });
    }).catch(() => { if (!controller.signal.aborted) setFailure(true); });
    return () => { controller.abort(); opened.closed = true; if (currentDraft.current === opened) currentDraft.current = undefined; };
  }, [session, targetKey, featurePosition, declarationId, reread]);
  const rows = values.items as ObjectCopyValuesRow[] | undefined;
  const dirty = !!preparation && JSON.stringify(values) !== JSON.stringify(preparation.values);
  const ready = !!draft && !!preparation && !expired && !draft.requiresReconfirmation && cancelState !== 'requested' && state?.phase === 'idle' && !submitting
    && (preparation?.items ? rows?.length === preparation.items.length && rows.every((row, index) => row.sourceLinkId === preparation.items![index].sourceLinkId
      && ['ECODE', 'ENAME'].every(key => typeof row.values[key] === 'string' && String(row.values[key]).trim().length > 0))
      : ['ECODE', 'ENAME'].every(key => typeof values[key] === 'string' && String(values[key]).trim().length > 0));
  useEffect(() => { callbacks.current.onDraftStateChange?.({ dirty, ready }); }, [dirty, ready]);
  const model = preparation?.modelKey ?? 'Part';
  const fields: StandardCrudFieldDefinition[] = [
    { key: 'ECODE', modelField: 'ECODE', labelKey: t(`${model}.Code`), kind: 'text', required: true },
    { key: 'ENAME', modelField: 'ENAME', labelKey: t(`${model}.Name`), kind: 'text', required: true },
    { key: 'Description', modelField: 'Description', labelKey: t(`${model}.Field_Description`), kind: 'textarea' },
  ];
  return <div style={{ display: 'grid', gap: 16 }}>
    <Alert showIcon type={expired || draft?.requiresReconfirmation || state?.phase === 'unknown' ? 'warning' : 'info'} message={t(state?.phase === 'unknown' ? 'Workspace.CopyOutcomeUnknown'
      : submitting ? 'Workspace.CopySubmitting' : expired ? 'Workspace.CopyInputExpired' : draft?.requiresReconfirmation ? 'Workspace.CopySourceChanged' : 'Workspace.ObjectCopyDescription')} />
    {preparation && <dl style={{ display: 'grid', gridTemplateColumns: 'auto minmax(0, 1fr)', gap: '8px 16px', margin: 0 }}>
      <dt>{t('Workspace.FolderCopySource')}</dt><dd style={{ margin: 0, overflowWrap: 'anywhere' }}>{preparation.sourcePath.join(' / ')}</dd>
      <dt>{t('Workspace.FolderCopyTarget')}</dt><dd style={{ margin: 0, overflowWrap: 'anywhere' }}>{preparation.targetPath.join(' / ')}</dd>
    </dl>}
    {preparation?.modelKey === 'Document' && <Alert showIcon type="info" message={t('Workspace.DocumentCopyFiles')}
      description={<span>{t('Workspace.CopyFileCount')}: {preparation.fileCount ?? 0} · {t('Document.FileLength')}: {preparation.totalBytes ?? 0}</span>} />}
    {preparation?.items ? preparation.items.map((item, index) => {
      // 标准字段key也是控件id；每行投影独立key，业务载荷和服务端错误仍按canonical字段往返。
      const prefix = `object-copy-${index}-`;
      return <section key={item.sourceLinkId} aria-label={`${index + 1}. ${item.sourcePath[item.sourcePath.length - 1]}`}>
      <h3 style={{ margin: '0 0 8px', overflowWrap: 'anywhere' }}>{index + 1}. {item.sourcePath.join(' / ')}</h3>
      <p>{t('Workspace.CopyFileCount')}: {item.fileCount} · {t('Document.FileLength')}: {item.totalBytes}</p>
      <StandardCrudForm formId={`workspace-object-copy-${index}`} mode="create" fields={fields.map(field => ({ ...field, key: prefix + field.key }))} sections={[]}
        values={Object.fromEntries(Object.entries(rows?.[index]?.values ?? item.values).map(([key, entry]) => [prefix + key, entry]))}
        fieldErrors={Object.fromEntries(Object.entries(draft?.fieldErrors ?? {}).filter(([key]) => key.startsWith(`items.${item.sourceLinkId}.`)).map(([key, message]) => [prefix + key.slice(`items.${item.sourceLinkId}.`.length), message]))}
        onChange={(key, next) => callbacks.current.onChange({ draft, values: { items: (rows ?? []).map((row, rowIndex) => rowIndex === index ? { ...row, values: { ...row.values, [key.slice(prefix.length)]: next } } : row) } })}
        onSubmit={() => undefined} saving={expired || submitting || cancelState === 'requested' || state?.phase === 'unknown'} showSubmitAction={false} titleForSection={() => t('Workspace.Basic')} />
    </section>; }) : preparation && <StandardCrudForm formId="workspace-object-copy" mode="create" fields={fields} sections={[]} values={values}
      fieldErrors={draft?.fieldErrors}
      onChange={(key, next) => callbacks.current.onChange({ draft, values: { ...values, [key]: next } })}
      onSubmit={() => undefined} saving={expired || submitting || state?.phase === 'unknown'} showSubmitAction={false} titleForSection={() => t('Workspace.Basic')} />}
    {!submitting && !expired && state?.phase === 'idle' && <Button onClick={() => setReread(count => count + 1)}>{t('Workspace.CopyReconfirm')}</Button>}
    {preparation && !expired && <Button disabled={cancelState === 'requested' || cancelState === 'cancelled' || cancelState === 'committed'} onClick={() => {
      const cancelling = draft;
      setCancelState('requested');
      void session?.resolveObjectCopy(true).then(result => { if (currentDraft.current === cancelling) setCancelState(result.state); })
        .catch(() => { if (currentDraft.current === cancelling) setCancelState('unknown'); });
    }}>{t('Workspace.CopyCancel')}</Button>}
    {cancelState && <Alert showIcon message={t(`Workspace.CopyCancel_${cancelState}`)} />}
  </div>;
}
