import { useEffect, useState } from 'react';
import { formatRequestError, platformFetch, useLocalization, usePlatformQuery } from '@icp-platform/core';
import { loadStandardCrudLookupPage, readStandardCrudRequestError, StandardCrudRequestError, type StandardCrudActionInputRenderContext, type StandardCrudLookupProvider, type StandardCrudOption } from '@icp-platform/sdk';
import { StandardCrudForm } from '@icp-platform/ui/crud-form';

function candidateProvider(modelKey: string): StandardCrudLookupProvider { return {
  key: 'workspace.object.candidate',
  load: async (request, context) => {
    if (modelKey !== 'Workspace/WorkspaceFolder' && !context?.featurePosition)
      throw new StandardCrudRequestError('Workspace candidate read permission is missing.', 403, {}, {
        code: 'Workspace.CandidateReadDenied', reasonKey: 'Workspace.CandidateReadDenied',
      });
    const query = new URLSearchParams({ keyword: request.keyword, pageIndex: String(request.page), pageSize: String(request.pageSize) });
    const route = modelKey === 'Part/Part' ? '/api/part/parts' : modelKey === 'Document/Document' ? '/api/document/documents' : '/api/workspace/folders/candidates';
    if (modelKey !== 'Workspace/WorkspaceFolder') query.set('fields', 'EID,ECODE,ENAME');
    const response = await platformFetch(`${route}?${query}`, { signal: context?.signal,
      headers: context?.featurePosition ? { 'X-NewP-Feature': context.featurePosition } : {} });
    if (!response.ok) throw await readStandardCrudRequestError(response);
    const result = await response.json() as { items: { id?: string; name?: string; EID?: string; ENAME?: string; ECODE?: string }[]; total: number; page: number; pageSize: number };
    return { options: result.items.map(item => ({ value: item.id ?? item.EID!, label: item.name ?? `${item.ECODE} - ${item.ENAME}` })), currentOptions: [], page: result.page, pageSize: result.pageSize,
      hasMore: result.page * result.pageSize < result.total };
  },
}; }

/** 复用正式 Lookup 分页协议与标准表单控件；弹层、草稿保护与写入状态由共享宿主持有。 */
function CandidatePicker({ modelKey, value, onChange, onDraftStateChange, featurePosition, submitting }: StandardCrudActionInputRenderContext & { modelKey: string }) {
  const { t } = useLocalization();
  const provider = candidateProvider(modelKey);
  const [request, setRequest] = useState({ keyword: '', page: 1 });
  const [options, setOptions] = useState<StandardCrudOption[]>([]);
  const field = { key: 'folder', modelField: 'EID', labelKey: t('Workspace.Object', '对象'), kind: 'lookup' as const, required: true,
    lookup: { providerKey: provider.key } };
  const query = usePlatformQuery({ key: ['workspace-object-candidate', modelKey, featurePosition ?? '', request.keyword, request.page],
    load: async ({ signal }) => {
      const result = await loadStandardCrudLookupPage({ field, providers: [provider], values: {}, keyword: request.keyword, page: request.page, pageSize: 20 }, { signal, featurePosition });
      if (result.error) throw result.failure ?? new Error(result.error);
      return result;
    } });
  useEffect(() => {
    if (query.status !== 'success' || !query.data) return;
    setOptions(previous => {
      const kept = request.page === 1 ? previous.filter(option => option.value === value) : previous;
      return [...kept, ...query.data!.options.filter(option => !kept.some(existing => existing.value === option.value))];
    });
  }, [query.status, query.data, request.page, value]);
  useEffect(() => {
    onDraftStateChange?.({ dirty: typeof value === 'string' && value.length > 0,
      ready: typeof value === 'string' && options.some(option => option.value === value) && query.status === 'success' && !submitting });
  }, [value, options, query.status, submitting, onDraftStateChange]);
  return <StandardCrudForm formId="workspace-folder-candidate" mode="create" fields={[field]} sections={[]} values={{ folder: value }}
    lookupOptions={{ folder: options }} lookupLoading={{ folder: query.isFetching }} lookupErrors={{ folder: query.error ? formatRequestError(query.error, t) : null }}
    lookupHasMore={{ folder: query.data?.hasMore ?? false }} onLookupSearch={(_key, keyword) => setRequest({ keyword, page: 1 })}
    onLookupLoadMore={() => { if (!query.isFetching && query.data?.hasMore) setRequest(current => ({ ...current, page: current.page + 1 })); }}
    onLookupRetry={() => query.retry()} onChange={(_key, next) => onChange(next)} onSubmit={() => undefined}
    saving={submitting ?? false} showSubmitAction={false} titleForSection={() => t('Workspace.Object', '对象')} />;
}

export default function FolderCandidateInput(context: StandardCrudActionInputRenderContext) {
  const { t } = useLocalization();
  const [modelKey, setModelKey] = useState('Workspace/WorkspaceFolder');
  const target = typeof context.value === 'object' && context.value !== null && 'id' in context.value ? context.value.id : undefined;
  const featurePosition = modelKey === 'Workspace/WorkspaceFolder' ? context.featurePosition
    : context.navigationFeaturePosition?.(modelKey === 'Part/Part' ? 'part.parts' : 'document.documents');
  const choices = [
    { value: 'Workspace/WorkspaceFolder', label: t('Workspace.Folder', '文件夹') },
    { value: 'Part/Part', label: t('Part.Title', '零组件') },
    { value: 'Document/Document', label: t('Document.Title', '图文档') },
  ];
  return <>
    <StandardCrudForm formId="workspace-candidate-type" mode="create" fields={[{ key: 'type', modelField: 'ObjectType', labelKey: t('Workspace.ObjectType', '对象类型'), kind: 'enum', options: choices }]}
      sections={[]} values={{ type: modelKey }} onChange={(_key, next) => { setModelKey(String(next)); context.onChange(undefined); }}
      onSubmit={() => undefined} saving={context.submitting ?? false} showSubmitAction={false} titleForSection={() => ''} />
    <CandidatePicker key={modelKey} {...context} modelKey={modelKey} featurePosition={featurePosition} value={target}
      onChange={id => context.onChange(typeof id === 'string' && id ? { modelKey, id } : undefined)} />
  </>;
}
