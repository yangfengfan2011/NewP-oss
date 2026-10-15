import { platformFetch } from '@icp-platform/core';
import { createElement, lazy, Suspense } from 'react';
import { readStandardCrudRequestError, StandardCrudRequestError, type StandardCrudActionInstance, type StandardCrudActionInputRenderContext, type StandardCrudDetail, type StandardCrudResourceRegistration } from '@icp-platform/sdk';
import type { WorkspaceDocumentDraft } from './workspaceDocumentUpload';
import { WorkspaceDocumentSession } from './workspaceDocumentSession';
import { workspacePosition } from './workspacePosition';
import { WorkspaceClipboardSession } from './workspaceClipboard';

const clipboardOwner = Symbol('workspace.clipboard');
function clipboard(instance: StandardCrudActionInstance | undefined): WorkspaceClipboardSession {
  if (!instance?.active) throw new StandardCrudRequestError('Workspace.ClipboardUnavailable', 409, {}, { code: 'Workspace.ClipboardUnavailable' });
  return instance.getOrCreate(clipboardOwner, () => new WorkspaceClipboardSession(() => instance.changed()), session => session.close());
}

interface LinkRow { linkId: string; associatedObject: { modelKey: string; id: string }; fields: Record<string, unknown> }
const headers = (featurePosition?: string): Record<string, string> => featurePosition ? { 'X-NewP-Feature': featurePosition } : {};
const WorkspaceObjectCreateInput = lazy(() => import('./WorkspaceObjectCreateInput'));
const WorkspaceObjectEditInput = lazy(() => import('./WorkspaceObjectEditInput'));
const WorkspaceDocumentCreateInput = lazy(() => import('./WorkspaceDocumentCreateInput'));
async function referenceCommand(method: string, path: string, body: unknown, featurePosition?: string) {
  const response = await platformFetch(path, { method, headers: { ...headers(featurePosition), 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  if (!response.ok) throw await readStandardCrudRequestError(response);
  return { operation: 'action' as const, payload: await response.json() as unknown, change: { operation: 'update' as const, id: '' } };
}

/** 关系身份参与表格；异构属性使用所属模块正式 View 和真实 EID，未知类型不能猜测。 */
const resource: StandardCrudResourceRegistration = {
  resourceKey: 'workspace.folder', listViewKey: 'workspace.folder.list', detailViewKey: 'workspace.folder.detail',
  enumOptions: { 'workspace.content.types': [
    { value: '', label: '全部', labelKey: 'Workspace.All' }, { value: 'WorkspaceFolder', label: '文件夹', labelKey: 'Workspace.Folder' },
    { value: 'Part', label: '零组件', labelKey: 'Part.Title' }, { value: 'Document', label: '图文档', labelKey: 'Document.Title' },
  ] },
  listFieldRenderers: { ObjectType: ({ row, value }) => createElement('span', null,
    createElement('span', { 'aria-hidden': true }, row.ObjectModelKey === 'WorkspaceFolder' ? '📁 ' : row.ObjectModelKey === 'Part' ? '▦ ' : '▤ '), String(value ?? '')) },
  actionInputRenderers: {
    editObject: Object.assign((context: StandardCrudActionInputRenderContext) =>
      createElement(Suspense, { fallback: null }, createElement(WorkspaceObjectEditInput, context)), { presentation: 'drawer' as const }),
    confirmDelete: () => null,
    confirmReferences: () => null,
    createObject: Object.assign((context: StandardCrudActionInputRenderContext) =>
      createElement(Suspense, { fallback: null }, createElement(context.action.key === 'createDocument' ? WorkspaceDocumentCreateInput : WorkspaceObjectCreateInput, context)), { presentation: 'drawer' as const }),
  },
  actionHandlers: {
    copy: async ({ action, instance, id, selectedRows, queryScope, featurePosition }) => {
      const session = clipboard(instance);
      if (action?.key === 'clearClipboard') {
        if (!session.clear()) throw new StandardCrudRequestError('Workspace.ClipboardBusy', 409, {}, { code: 'Workspace.ClipboardBusy' });
        return { operation: 'action', payload: null, noChange: true, notice: { reasonKey: 'Workspace.ClipboardCleared' } };
      }
      const copied = await session.copy(workspacePosition(queryScope), id ? [id] : (selectedRows ?? []).map(row => String(row.LID)), featurePosition);
      return { operation: 'action', payload: null, noChange: true, ...(copied ? { notice: { reasonKey: 'Workspace.Copied' } } : {}) };
    },
    cut: async ({ instance, id, selectedRows, queryScope, featurePosition }) => {
      const session = clipboard(instance);
      const selected = await session.cut(workspacePosition(queryScope), id ? [id] : (selectedRows ?? []).map(row => String(row.LID)), featurePosition);
      return { operation: 'action', payload: null, noChange: true, ...(selected ? { notice: { reasonKey: 'Workspace.CutReady' } } : {}) };
    },
    paste: async ({ instance, queryScope, featurePosition }) => {
      const session = clipboard(instance); const mode = session.getSnapshot().clipboard?.mode;
      const receipt = await session.paste(workspacePosition(queryScope), featurePosition);
      return { operation: 'action', payload: receipt, ...(receipt.noChange ? { noChange: true as const } : { change: { operation: 'update' as const, id: '' } }),
        notice: { reasonKey: receipt.noChange ? 'Workspace.PositionUnchanged' : mode === 'cut' ? 'Workspace.Moved' : 'Workspace.Pasted' } };
    },
    reorder: async ({ action, id, queryScope, featurePosition }) => {
      if (!id || !action || !['moveUp', 'moveDown'].includes(action.key)) throw new Error('无效文件夹排序动作');
      const result = await referenceCommand('POST', '/api/workspace/content/tree-order', {
        parentPosition: workspacePosition(queryScope), linkId: id, direction: action.key === 'moveUp' ? 'up' : 'down',
      }, featurePosition);
      const receipt = result.payload as { moved: boolean; boundary: string | null };
      if (!receipt.moved && (receipt.boundary === 'first' || receipt.boundary === 'last')) return {
        operation: 'action', payload: receipt, noChange: true,
        notice: { reasonKey: receipt.boundary === 'first' ? 'Workspace.TreeOrderFirst' : 'Workspace.TreeOrderLast' },
      };
      return result;
    },
    editObject: async ({ id, input, queryScope, featurePosition }) => {
      if (!id) throw new Error('缺少引用标识');
      return referenceCommand('PUT', `/api/workspace/content/${encodeURIComponent(id)}`, { position: workspacePosition(queryScope), values: input }, featurePosition);
    },
    deleteObject: async ({ id, queryScope, featurePosition }) => {
      if (!id) throw new Error('缺少引用标识');
      return referenceCommand('DELETE', `/api/workspace/content/${encodeURIComponent(id)}`, { position: workspacePosition(queryScope) }, featurePosition);
    },
    deleteObjects: async ({ selectedRows, queryScope, featurePosition }) => referenceCommand('POST', '/api/workspace/content/delete',
      { position: workspacePosition(queryScope), linkIds: (selectedRows ?? []).map(row => String(row.LID)) }, featurePosition),
    createPart: async ({ input, queryScope, featurePosition }) => referenceCommand('POST', '/api/workspace/content/parts',
      { position: workspacePosition(queryScope), values: input }, featurePosition),
    createDocument: Object.assign(async ({ input, queryScope, featurePosition }: import('@icp-platform/sdk').StandardCrudActionContext) => {
      const draft = typeof input === 'object' && input !== null ? input as WorkspaceDocumentDraft : undefined;
      if (!(draft?.session instanceof WorkspaceDocumentSession)) throw new Error('缺少有效的上传会话，请重新打开。');
      const payload = await draft.session.submit(draft, queryScope, featurePosition);
      return { operation: 'action' as const, payload, change: { operation: 'update' as const, id: '' } };
    }, {
      // 重复代码已确定未消费暂存；修正字段后可由用户再次保存，其他409沿共享冲突生命周期。
      isCorrectableInputError: (failure: unknown) => failure instanceof StandardCrudRequestError && failure.status === 409
        && failure.code === 'Document.CodeDuplicated'
        && failure.fieldErrorDetails.some(error => error.field === 'ECODE' && error.code === 'Document.CodeDuplicated'),
    }),
    remove: async ({ id, queryScope, featurePosition }) => {
      if (!id) throw new Error('缺少引用标识');
      return referenceCommand('DELETE', `/api/workspace/content/references/${encodeURIComponent(id)}`, { position: workspacePosition(queryScope) }, featurePosition);
    },
    removeSelected: async ({ selectedRows, queryScope, featurePosition }) => referenceCommand('POST', '/api/workspace/content/references/remove',
      { position: workspacePosition(queryScope), linkIds: (selectedRows ?? []).map(row => String(row.LID)) }, featurePosition),
  },
  actionStateResolver: ({ action, instance, queryScope }) => {
    const root = queryScope?.linkIds === '[]';
    if (root && ['createPart', 'createDocument'].includes(action.key)) return { visible: false, available: false, reasonKey: 'Workspace.RootOnlyFolders' };
    const state = instance?.active ? clipboard(instance).getSnapshot() : undefined;
    const label = state?.clipboard?.mode === 'cut' && ['cut', 'cutSelected', 'paste'].includes(action.key)
      ? { labelKey: action.key === 'paste' ? 'Workspace.PastePending' : 'Workspace.CutPending' } : {};
    if (state?.phase === 'pasting' || state?.phase === 'validating') return { visible: true, available: false, reasonKey: 'Workspace.ClipboardBusy', ...label };
    if (!['copy', 'copySelected', 'cut', 'cutSelected', 'paste', 'clearClipboard'].includes(action.key)) return { visible: true, available: true };
    if (!state) return { visible: true, available: false, reasonKey: 'Workspace.ClipboardUnavailable' };
    if (action.key === 'paste' && state.phase === 'unknown') return { visible: true, available: false, reasonKey: 'Workspace.ClipboardOutcomeUnknown', ...label };
    if (['paste', 'clearClipboard'].includes(action.key) && !state.clipboard) return { visible: true, available: false, reasonKey: 'Workspace.ClipboardUnavailable' };
    return { visible: true, available: true, ...label };
  },
  dataAdapter: {
    list: async (query = {}, context) => {
      const { anchorId, ...position } = context?.queryScope ?? {};
      if (typeof anchorId !== 'string' || !position.spaceId || !position.rootFolderId || !position.linkIds) throw new Error('缺少工作空间出现位置');
      const parameters = new URLSearchParams({ direction: 'forward', anchorModelKey: 'WorkspaceFolder', anchorId,
        queryScope: JSON.stringify(position) });
      for (const [key, value] of Object.entries(query)) if (value !== undefined && value !== null && value !== '') parameters.set(key, String(value));
      const response = await platformFetch(`/api/links/workspace.content?${parameters}`, { signal: context?.signal, headers: headers(context?.featurePosition) });
      if (!response.ok) throw await readStandardCrudRequestError(response);
      const payload = await response.json() as { items: LinkRow[]; total: number; page: number; pageSize: number };
      return { ...payload, items: payload.items.map(row => ({ ...row.fields, LID: row.linkId,
        ObjectModelKey: row.associatedObject.modelKey, ObjectId: row.associatedObject.id })) };
    },
    get: async (id, context) => {
      // 树动作 recordId 是入向 LID；对象读取仍使用 resolver 提供的 EID，写入再重验源位置。
      const referenceObjectId = context?.queryScope?.referenceObjectId;
      const response = await platformFetch(`/api/workspace/folders/${encodeURIComponent(typeof referenceObjectId === 'string' ? referenceObjectId : id)}`, {
        signal: context?.signal, headers: headers(context?.featurePosition),
      });
      if (!response.ok) throw await readStandardCrudRequestError(response);
      const payload = await response.json() as { fields: Record<string, unknown> };
      const detail: StandardCrudDetail = { fields: typeof referenceObjectId === 'string'
        ? { ...payload.fields, LID: id, ObjectModelKey: 'WorkspaceFolder', ObjectId: referenceObjectId } : payload.fields, summaries: {} };
      return { normalized: detail, value: detail };
    },
    create: async (values, _before, context) => {
      const { WorkspacePosition: position, ...fields } = values;
      if (!position) throw new Error('缺少工作空间创建位置');
      const response = await platformFetch('/api/workspace/content/folders', { method: 'POST', headers: { ...headers(context?.featurePosition), 'Content-Type': 'application/json' },
        body: JSON.stringify({ position, values: fields }) });
      if (!response.ok) throw await readStandardCrudRequestError(response);
      const payload: unknown = await response.json();
      // unknown-change 刷新全部已加载出现位置，读取失败不能重放已成功创建。
      return { operation: 'create', payload, change: { operation: 'create', id: '' } };
    },
    getRowId: row => String(row.LID),
    getDetailId: row => {
      if (row.ObjectModelKey !== 'WorkspaceFolder' || typeof row.ObjectId !== 'string') throw new Error('未登记的文件夹属性端点');
      return row.ObjectId;
    },
    getDetailTarget: row => {
      if (typeof row.ObjectId !== 'string' || !row.ObjectId) throw new Error('缺少对象标识');
      if (row.ObjectModelKey === 'WorkspaceFolder') return undefined;
      const views: Record<string, string> = { 'Part': 'part.part.detail', 'Document': 'document.document.detail' };
      const view = views[String(row.ObjectModelKey)];
      if (!view) throw new Error('未登记的对象属性端点');
      return { detailViewKey: view, id: row.ObjectId };
    },
  },
};
export default resource;
