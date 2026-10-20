import { useCallback, useEffect, useRef, useState, useSyncExternalStore } from 'react';
import { Alert, Button, Checkbox, Input, Radio, Space, Spin, Table, Typography } from 'antd';
import { formatRequestError, platformFetch, useLocalization, usePlatformQuery } from '@icp-platform/core';
import { DataScopeSubjectTable } from '@icp-platform/ui';
import { readStandardCrudRequestError, StandardCrudRequestError, type StandardCrudActionInputRenderContext } from '@icp-platform/sdk';
import { batchInitialSources, batchItemKey, batchSubmission, prepareBatch, readBatchCurrent, batchAuthorizationHeaders,
  type BatchCurrentConfiguration, type BatchCatalog, type BatchDraft, type BatchFilters, type BatchItem, type BatchSource } from './workspaceBatchAuthorization';
import { workspaceClipboard } from './workspaceClipboard';

const noSubscribe = () => () => {};
const notFrozen = () => false;
const titleKeys: Record<string, string> = { 'part.part': 'Part.Title', 'document.document': 'Document.Title', 'workspace.folder': 'Workspace.Folder' };

/** 当前实例拥有完整候选快照；标准Review仍拥有提交、取消、错误和弹层生命周期。 */
export default function WorkspaceBatchAuthorizationInput(context: StandardCrudActionInputRenderContext) {
  const { t } = useLocalization();
  const { onChange, onDraftStateChange } = context;
  const submission = batchSubmission(context.instance);
  const frozen = useSyncExternalStore(submission?.subscribe ?? noSubscribe, submission?.getSnapshot ?? notFrozen);
  // 每个资源拥有独立主体草稿；一个编辑器结束不能清除其他资源仍在编辑的事实。
  const [editingResources, setEditingResources] = useState<ReadonlySet<string>>(() => new Set());
  const editing = editingResources.size > 0;
  const setResourceEditing = useCallback((resourceKey: string, active: boolean) => {
    setEditingResources(current => {
      if (current.has(resourceKey) === active) return current;
      const next = new Set(current);
      if (active) next.add(resourceKey); else next.delete(resourceKey);
      return next;
    });
  }, []);
  const [collecting, setCollecting] = useState(false);
  const [collectionFailure, setCollectionFailure] = useState<unknown>();
  const [filterDraft, setFilterDraft] = useState<BatchFilters>({});
  const pool = useRef<BatchSource[]>();
  const expanded = useRef(false);
  const appliedFilters = useRef<BatchFilters>();
  const removed = useRef(new Set<string>());
  const collectionRequest = useRef<AbortController>();
  useEffect(() => () => collectionRequest.current?.abort(), []);
  const received = useRef<unknown>();
  const opening = useRef(crypto.randomUUID());
  const sources = batchInitialSources(context);
  const initialKey = JSON.stringify(sources);
  const denied = context.submissionFailure instanceof StandardCrudRequestError && [401, 403, 404].includes(context.submissionFailure.status);
  const query = usePlatformQuery({ key: ['workspace-batch-authorization', initialKey, context.featurePosition ?? '', context.dataAccess?.id ?? '', opening.current],
    enabled: Boolean(context.featurePosition && !frozen && !denied),
    load: ({ signal }) => prepareBatch(JSON.parse(initialKey), context.featurePosition!, signal, false, undefined, context.dataAccess?.id) });
  const draft = (context.value as BatchDraft | undefined) ?? (frozen ? submission?.lastDraft : undefined);
  useEffect(() => {
    if (query.status !== 'success' || !query.data || received.current === query.data || frozen) return;
    received.current = query.data;
    onChange({ catalogs: query.data.catalogs, mode: 'U', confirmClear: false, items: query.data.items,
      resources: query.data.catalogs.map(catalog => ({ resourceKey: catalog.resourceKey, subjects: [] })) } satisfies BatchDraft);
  }, [query.status, query.data, onChange, frozen]);
  useEffect(() => {
    if (query.status === 'success' && query.data && !pool.current) {
      pool.current = JSON.parse(initialKey) as BatchSource[];
    }
  }, [query.status, query.data, initialKey]);
  const refine = async (nextSources: BatchSource[], descendants: boolean, filters?: BatchFilters) => {
    if (!draft || context.submitting || frozen || collecting) return;
    collectionRequest.current?.abort();
    const controller = new AbortController(); collectionRequest.current = controller;
    setCollecting(true); setCollectionFailure(undefined);
    try {
      const result = await prepareBatch(nextSources, context.featurePosition!, controller.signal, descendants, filters, context.dataAccess?.id);
      if (controller.signal.aborted) return;
      pool.current = nextSources; expanded.current = descendants; appliedFilters.current = filters;
      const items = result.items.filter(item => !removed.current.has(batchItemKey(item)));
      onChange({ ...draft, catalogs: result.catalogs, items, resources: result.catalogs.filter(catalog => items.some(item => item.resourceKey === catalog.resourceKey))
        .map(catalog => draft.resources.find(resource => resource.resourceKey === catalog.resourceKey) ?? { resourceKey: catalog.resourceKey, subjects: [] }) });
    } catch (failure) { if (!controller.signal.aborted) setCollectionFailure(failure); }
    finally { if (!controller.signal.aborted) setCollecting(false); }
  };
  const clipboard = context.instance?.active ? workspaceClipboard(context.instance) : undefined;
  const clipboardState = useSyncExternalStore(clipboard?.subscribe ?? noSubscribe, clipboard?.getSnapshot ?? (() => undefined));
  const collectionDenied = collectionFailure instanceof StandardCrudRequestError && [401, 403, 404].includes(collectionFailure.status);
  const changedTargets = Boolean(draft && query.data && JSON.stringify(draft.items.map(item => [batchItemKey(item), item.source]))
    !== JSON.stringify(query.data.items.map(item => [batchItemKey(item), item.source])));
  const clearNeedsConfirmation = draft?.mode === 'R' && draft.resources.some(resource => !resource.subjects.length) && !draft.confirmClear;
  useEffect(() => onDraftStateChange?.({ dirty: editing || changedTargets || Boolean(draft && (draft.mode !== 'U'
    || draft.resources.some(resource => resource.subjects.length))), ready: !context.submitting && !frozen && !denied && !collectionDenied && !collecting && !collectionFailure && !editing
    && query.status === 'success' && Boolean(draft?.items.length) && !draft?.items.some(item => !item.manageable) && !clearNeedsConfirmation }),
  [onDraftStateChange, context.submitting, draft, editing, changedTargets, frozen, denied, collectionDenied, collecting, collectionFailure, query.status, clearNeedsConfirmation]);
  if (denied || collectionDenied) return <Alert type="error" message={t('Workspace.BatchAccessChanged')} />;
  if (query.status === 'error') return <Alert type="error" message={formatRequestError(query.error, t)} />;
  if (!draft) return <Spin />;
  const disabled = Boolean(context.submitting || frozen || editing || collecting);
  return <div style={{ maxHeight: '65vh', overflowY: 'auto' }}><Space direction="vertical" style={{ width: '100%' }}>
    {frozen && <BatchReconciliation draft={draft} featurePosition={context.featurePosition!} declarationId={context.dataAccess?.id}
      onDenied={setCollectionFailure} onRestart={() => {
        pool.current = draft.items.map(item => item.source); expanded.current = false; appliedFilters.current = undefined;
        removed.current.clear(); setFilterDraft({});
        onChange({ ...draft, mode: 'U', confirmClear: false, resources: draft.catalogs.filter(catalog => draft.items.some(item => item.resourceKey === catalog.resourceKey))
          .map(catalog => ({ resourceKey: catalog.resourceKey, subjects: [] })) });
        submission?.restart();
      }} />}
    {!!collectionFailure && <Alert type="error" message={formatRequestError(collectionFailure, t)} />}
    <Space wrap>
      <Button disabled={disabled || !clipboardState?.clipboard || clipboardState.phase !== 'idle'} onClick={() => {
        const value = clipboardState?.clipboard;
        if (value) void refine([...(pool.current ?? sources), { position: { ...value.sourcePosition, linkIds: [...value.sourcePosition.linkIds] },
          linkIds: [...value.linkIds] }], expanded.current, appliedFilters.current);
      }}>{t('Workspace.BatchAppendClipboard')}</Button>
      <Button disabled={disabled || expanded.current} onClick={() => void refine(pool.current ?? sources, true, appliedFilters.current)}>{t('Workspace.BatchAddDescendants')}</Button>
    </Space>
    <Space wrap>
      <Checkbox.Group aria-label={t('Workspace.BatchTypeFilter')} value={filterDraft.resourceKeys ?? []}
        disabled={disabled} options={Object.entries(titleKeys).map(([value, key]) => ({ value, label: t(key) }))}
        onChange={values => setFilterDraft({ ...filterDraft, resourceKeys: values.length ? values.map(String) : undefined })} />
      <Input aria-label={t('Workspace.BatchCreatorFilter')} placeholder={t('Workspace.BatchCreatorFilter')} disabled={disabled}
        value={filterDraft.createdBy ?? ''} onChange={event => setFilterDraft({ ...filterDraft, createdBy: event.target.value || undefined })} />
      <Input aria-label={t('Workspace.BatchOwnerFilter')} placeholder={t('Workspace.BatchOwnerFilter')} disabled={disabled}
        value={filterDraft.ownerId ?? ''} onChange={event => setFilterDraft({ ...filterDraft, ownerId: event.target.value || undefined })} />
      <Button loading={collecting} disabled={disabled} onClick={() => void refine(pool.current ?? sources, expanded.current, filterDraft)}>{t('Workspace.BatchApplyFilters')}</Button>
    </Space>
    <Typography.Text>{t('Workspace.BatchTargets', undefined, { count: draft.items.length })}</Typography.Text>
    <Table<BatchItem> size="small" rowKey={batchItemKey} dataSource={draft.items} pagination={{ pageSize: 20 }} columns={[
      { key: 'type', title: t('Workspace.ObjectType'), render: (_, item) => t(titleKeys[item.resourceKey] ?? 'Workspace.Object') },
      { key: 'name', title: t('Workspace.Name'), dataIndex: 'name' },
      { key: 'code', title: t('Workspace.Code'), dataIndex: 'code' },
      { key: 'remove', render: (_, item) => <Button disabled={disabled} aria-label={t('Workspace.BatchRemoveNamed', undefined, { name: item.name })} onClick={() => {
        removed.current.add(batchItemKey(item));
        const items = draft.items.filter(value => batchItemKey(value) !== batchItemKey(item));
        onChange({ ...draft, items, resources: draft.resources.filter(resource => items.some(value => value.resourceKey === resource.resourceKey)) });
      }}>{t('Workspace.BatchRemove')}</Button> },
      { key: 'management', title: t('Workspace.Authorize'), render: (_, item) => t(item.manageable ? 'Workspace.BatchManageable' : 'Workspace.BatchNotManageable') },
    ]} />
    <Radio.Group disabled={disabled} value={draft.mode} onChange={event => onChange({ ...draft, mode: event.target.value, confirmClear: false })}>
      <Radio value="U">{t('Workspace.BatchUpdateMode')}</Radio><Radio value="R">{t('Workspace.BatchReplaceMode')}</Radio>
    </Radio.Group>
    <Alert type={draft.mode === 'R' ? 'warning' : 'info'} message={t(draft.mode === 'R' ? 'Workspace.BatchReplaceDescription' : 'Workspace.BatchUpdateDescription')} />
    {draft.mode === 'R' && <Checkbox disabled={disabled} checked={draft.confirmClear}
      onChange={event => onChange({ ...draft, confirmClear: event.target.checked })}>{t('Workspace.BatchClearConfirm')}</Checkbox>}
    {draft.catalogs.filter(catalog => draft.items.some(item => item.resourceKey === catalog.resourceKey)).map(catalog => <ResourceSubjects key={catalog.resourceKey} catalog={catalog}
      item={draft.items.find(item => item.resourceKey === catalog.resourceKey)!} featurePosition={context.featurePosition!} declarationId={context.dataAccess?.id}
      subjects={draft.resources.find(resource => resource.resourceKey === catalog.resourceKey)?.subjects ?? []}
      disabled={Boolean(context.submitting || frozen || collecting || collectionFailure || draft.items.some(item => !item.manageable))} onEditingChange={active => setResourceEditing(catalog.resourceKey, active)}
      onChange={subjects => onChange({ ...draft, resources: draft.resources.map(resource => resource.resourceKey === catalog.resourceKey
        ? { ...resource, subjects } : resource) })} />)}
  </Space></div>;
}

function ResourceSubjects({ catalog, item, featurePosition, declarationId, subjects, disabled, onChange, onEditingChange }: {
  catalog: BatchCatalog; item: BatchItem; featurePosition: string; declarationId?: string; subjects: BatchDraft['resources'][number]['subjects']; disabled: boolean;
  onChange: (subjects: BatchDraft['resources'][number]['subjects']) => void; onEditingChange: (editing: boolean) => void;
}) {
  const { t } = useLocalization();
  const sourceKey = JSON.stringify(item.source);
  const read = useCallback(async (suffix: string, signal?: AbortSignal) => {
    const source = JSON.parse(sourceKey) as BatchItem['source'];
    const child = new URL(suffix, 'http://batch-authorization.local');
    const url = new URL(`/api/workspace/content/${encodeURIComponent(source.linkIds[0])}/permissions${child.pathname}`, 'http://batch-authorization.local');
    url.search = child.search; url.searchParams.set('queryScope', JSON.stringify(source.position));
    const response = await platformFetch(url.pathname + url.search, { signal, headers: batchAuthorizationHeaders(featurePosition, declarationId) });
    if (!response.ok) throw await readStandardCrudRequestError(response);
    return response.json() as Promise<unknown>;
  }, [sourceKey, featurePosition, declarationId]);
  return <section aria-label={t(titleKeys[catalog.resourceKey] ?? 'Workspace.Object')}>
    <Typography.Title level={5}>{t(titleKeys[catalog.resourceKey] ?? 'Workspace.Object')}</Typography.Title>
    <DataScopeSubjectTable subjects={subjects} types={catalog.subjectTypes} actions={catalog.actions} read={read}
      disabled={disabled} objectMode onChange={onChange} onEditingChange={onEditingChange} />
  </section>;
}

/** 显式读取不会重放旧模板；只有用户另行选择新操作才清空未知草稿并恢复编辑。 */
function BatchReconciliation({ draft, featurePosition, declarationId, onDenied, onRestart }: {
  draft: BatchDraft; featurePosition: string; declarationId?: string; onDenied: (failure: unknown) => void; onRestart: () => void;
}) {
  const { t } = useLocalization();
  const [current, setCurrent] = useState<BatchCurrentConfiguration[]>();
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<unknown>();
  const controller = useRef<AbortController>();
  useEffect(() => () => controller.current?.abort(), []);
  const reconcile = async () => {
    if (busy) return;
    controller.current?.abort(); const request = new AbortController(); controller.current = request;
    setBusy(true); setCurrent(undefined); setFailure(undefined);
    try {
      const result = await readBatchCurrent(draft, featurePosition, request.signal, declarationId);
      if (!request.signal.aborted) setCurrent(result);
    } catch (error) {
      if (!request.signal.aborted) {
        setFailure(error);
        if (error instanceof StandardCrudRequestError && [401, 403, 404].includes(error.status)) onDenied(error);
      }
    } finally { if (!request.signal.aborted) setBusy(false); }
  };
  return <Space direction="vertical" style={{ width: '100%' }}>
    <Alert type="warning" message={t('Workspace.BatchUnknown')} />
    <Button loading={busy} disabled={busy} onClick={() => void reconcile()}>{t('Workspace.BatchReconcile', '核对当前整组配置')}</Button>
    {!!failure && <Alert type="error" message={formatRequestError(failure, t)} />}
    {current && <>
      <Alert type="info" message={t('Workspace.BatchCurrentIsNotReceipt', '当前配置不能证明原请求是否提交')} />
      {current.map(configuration => <ResourceSubjects key={batchItemKey(configuration)}
        catalog={draft.catalogs.find(catalog => catalog.resourceKey === configuration.resourceKey)!}
        item={draft.items.find(item => batchItemKey(item) === batchItemKey(configuration))!} featurePosition={featurePosition} declarationId={declarationId}
        subjects={configuration.subjects} disabled onChange={() => {}} onEditingChange={() => {}} />)}
      <Button onClick={onRestart}>{t('Workspace.BatchStartNew', '开始新的授权操作')}</Button>
    </>}
  </Space>;
}
