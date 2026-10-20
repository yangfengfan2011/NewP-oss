import { workspaceHistoryCheck } from './workspaceHistoryCheck';
import { platformFetch } from '@icp-platform/core';
import { createObjectOwnershipTransferActions, createObjectOwnershipActions, createObjectAuthorizationActions } from '@icp-platform/ui';
import { createElement, lazy, Suspense } from 'react';
import { isStandardCrudRecord, readStandardCrudRequestError, StandardCrudRequestError, type StandardCrudActionContext, type StandardCrudActionInputRenderContext, type StandardCrudDetail, type StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { workspaceContentSource } from './workspaceTree';
import type { WorkspaceDocumentDraft } from './workspaceDocumentUpload';
import { WorkspaceDocumentSession } from './workspaceDocumentSession';
import { workspacePosition } from './workspacePosition';
import { batchOutcomeUnknown, saveBatch } from './workspaceBatchAuthorization';
import { workspaceClipboard as clipboard, type FolderCopyDraft, type ObjectCopyDraft, type ObjectCopyReceipt, type ObjectCopyValuesRow } from './workspaceClipboard';

interface LinkRow { linkId: string; associatedObject: { modelKey: string; id: string }; fields: Record<string, unknown> }
const headers = (featurePosition?: string): Record<string, string> => featurePosition ? { 'X-NewP-Feature': featurePosition } : {};
// 创建只透传当前实际动作或adapter上下文的正式声明；无来源旧调用不能借列表read或默认值。
function folderCreateHeaders(featurePosition?: string, declarationId?: string): Record<string, string> {
  if (!declarationId) throw new StandardCrudRequestError('缺少文件夹创建声明。', 400);
  return { ...headers(featurePosition), 'Content-Type': 'application/json', 'X-NewP-Data-Declaration': declarationId };
}
const WorkspaceBatchAuthorizationInput = lazy(() => import('./WorkspaceBatchAuthorizationInput'));
const WorkspaceObjectCreateInput = lazy(() => import('./WorkspaceObjectCreateInput'));
const WorkspaceFolderCreateInput = lazy(() => import('./WorkspaceFolderCreateInput'));
const WorkspaceObjectEditInput = lazy(() => import('./WorkspaceObjectEditInput'));
const WorkspaceDocumentCreateInput = lazy(() => import('./WorkspaceDocumentCreateInput'));
const WorkspaceFolderCopyInput = lazy(() => import('./WorkspaceFolderCopyInput'));
const WorkspaceObjectCopyInput = lazy(() => import('./WorkspaceObjectCopyInput'));
const WorkspaceObjectCopyRecovery = lazy(() => import('./WorkspaceObjectCopyRecovery'));
const WorkspaceObjectCopyResult = lazy(() => import('./WorkspaceObjectCopyResult'));
const ownership = createObjectOwnershipActions(({ id, row, queryScope }) => ({ initialId: id ?? (row as { LID?: string } | undefined)?.LID,
  pendingPath: '/api/workspace/ownership/pending?queryScope=' + encodeURIComponent(JSON.stringify(workspacePosition(queryScope))),
  statusPath: '/api/workspace/content/{id}/ownership/status?queryScope=' + encodeURIComponent(JSON.stringify(workspacePosition(queryScope))), savePath: '/api/workspace/content/{id}/ownership',
  accountsPath: '/api/workspace/ownership/accounts', eligibilityPath: '/api/workspace/ownership/eligibility',
  batchPath: '/api/workspace/ownership/initialize', batchIdField: 'linkIds',
  body: { position: workspacePosition(queryScope) },
}), '/api/workspace/ownership/eligibility');
function authorizationSource(context: Pick<StandardCrudActionContext, 'id' | 'row' | 'detail' | 'queryScope'>) {
  const detail = isStandardCrudRecord(context.detail) && isStandardCrudRecord(context.detail.fields) ? context.detail.fields : {};
  const row = isStandardCrudRecord(context.row) ? context.row : detail;
  return workspaceContentSource({ ...context.queryScope, sourceLinkId: context.queryScope?.sourceLinkId ?? context.id ?? row.LID,
    modelKey: context.queryScope?.sourceModelKey ?? row.ObjectModelKey, objectId: context.queryScope?.sourceObjectId ?? row.ObjectId });
}
function authorization(context: Pick<StandardCrudActionContext, 'id' | 'row' | 'detail' | 'queryScope'>) {
  const source = authorizationSource(context);
  return createObjectAuthorizationActions(source.resourceKey, () => ({ id: source.objectId, title: '对象', titleKey: 'Workspace.Object',
    basePath: `/api/workspace/content/${encodeURIComponent(source.sourceLinkId)}/permissions?queryScope=${encodeURIComponent(JSON.stringify({ spaceId: source.spaceId, rootFolderId: source.rootFolderId, linkIds: source.linkIds }))}` }));
}
async function referenceCommand(method: string, path: string, body: unknown, featurePosition?: string, declarationId?: string) {
  const response = await platformFetch(path, { method, headers: { ...headers(featurePosition), 'Content-Type': 'application/json',
    ...(declarationId ? { 'X-NewP-Data-Declaration': declarationId } : {}) }, body: JSON.stringify(body) });
  if (!response.ok) throw await readStandardCrudRequestError(response);
  return { operation: 'action' as const, payload: await response.json() as unknown, change: { operation: 'update' as const, id: '' } };
}

// 转交请求只携带真实父位置与所选LID，服务端重解规范EID并重验归属。
const transfer = createObjectOwnershipTransferActions(context => {
  const position = workspacePosition(context.queryScope);
  const fields = isStandardCrudRecord(context.row) ? context.row
    : isStandardCrudRecord(context.detail) && isStandardCrudRecord(context.detail.fields) ? context.detail.fields : {};
  const sourceLinkId = context.queryScope?.sourceLinkId ?? context.id ?? fields.LID;
  const linkIds = typeof sourceLinkId === 'string' && sourceLinkId ? [sourceLinkId] : (context.selectedRows ?? []).map(row => {
    if (!isStandardCrudRecord(row) || typeof row.LID !== 'string' || !row.LID) throw new StandardCrudRequestError('缺少转交来源。', 409);
    return row.LID;
  });
  if (!linkIds.length) throw new StandardCrudRequestError('请选择转交对象。', 409);
  const selected = [...new Set(linkIds)];
  const base = '/api/workspace/ownership/transfer';
  const query = new URLSearchParams({ queryScope: JSON.stringify(position), selectedIds: JSON.stringify(selected) });
  return { savePath: base, eligibilityPath: `${base}/eligibility?${query}`, accountsPath: `${base}/accounts?${query}`, body: { position, linkIds: selected } };
});

/** 关系身份参与表格；异构属性使用所属模块正式 View 和真实 EID，未知类型不能猜测。 */
const resource: StandardCrudResourceRegistration = {
  actionPrechecks: { history: workspaceHistoryCheck },
  resourceKey: 'workspace.folder', listViewKey: 'workspace.folder.list', detailViewKey: 'workspace.folder.detail',
  enumOptions: { 'workspace.content.types': [
    { value: '', label: '全部', labelKey: 'Workspace.All' }, { value: 'WorkspaceFolder', label: '文件夹', labelKey: 'Workspace.Folder' },
    { value: 'Part', label: '零组件', labelKey: 'Part.Title' }, { value: 'Document', label: '图文档', labelKey: 'Document.Title' },
  ] },
  listFieldRenderers: { ObjectType: ({ row, value }) => createElement('span', null,
    createElement('span', { 'aria-hidden': true }, row.ObjectModelKey === 'WorkspaceFolder' ? '📁 ' : row.ObjectModelKey === 'Part' ? '▦ ' : '▤ '), String(value ?? '')) },
  operationResultPresenters: [{
    supports: result => {
      const payload = result.payload as { kind?: string; receipt?: ObjectCopyReceipt } | undefined;
      return payload?.kind === 'workspace.objectCopy' && (payload.receipt?.items?.length ?? 0) > 1
        && ['committed', 'cancelled'].includes(payload.receipt?.state ?? '');
    },
    render: context => createElement(Suspense, { fallback: null }, createElement(WorkspaceObjectCopyResult, context)),
  }],
  actionInputRenderers: {
    ...transfer.actionInputRenderers, ...ownership.actionInputRenderers,
    batchAuthorization: Object.assign((context: StandardCrudActionInputRenderContext) => createElement(Suspense, { fallback: null }, createElement(WorkspaceBatchAuthorizationInput, context)), { dialogWidth: 1360 }),
    createFolder: Object.assign((context: StandardCrudActionInputRenderContext) =>
      createElement(Suspense, { fallback: null }, createElement(WorkspaceFolderCreateInput, context)), { presentation: 'drawer' as const }),
    objectAuthorization: Object.assign((context: StandardCrudActionInputRenderContext) => authorization(context).actionInputRenderers.objectAuthorization(context), { dialogWidth: 1360 }),
    recoverObjectCopy: Object.assign((context: StandardCrudActionInputRenderContext) => createElement(Suspense, { fallback: null }, createElement(WorkspaceObjectCopyRecovery, context)), { presentation: 'drawer' as const }),
    copyObject: Object.assign((context: StandardCrudActionInputRenderContext) => createElement(Suspense, { fallback: null }, createElement(WorkspaceObjectCopyInput, context)), { presentation: 'drawer' as const }),
    copyFolder: (context: StandardCrudActionInputRenderContext) => createElement(Suspense, { fallback: null }, createElement(WorkspaceFolderCopyInput, context)),
    editObject: Object.assign((context: StandardCrudActionInputRenderContext) =>
      createElement(Suspense, { fallback: null }, createElement(WorkspaceObjectEditInput, context)), { presentation: 'drawer' as const }),
    confirmDelete: () => null,
    confirmReferences: () => null,
    createObject: Object.assign((context: StandardCrudActionInputRenderContext) =>
      createElement(Suspense, { fallback: null }, createElement(context.action.key === 'createDocument' ? WorkspaceDocumentCreateInput : WorkspaceObjectCreateInput, context)), { presentation: 'drawer' as const }),
  },
  actionHandlers: {
    ...transfer.actionHandlers, ...ownership.actionHandlers,
    create: async ({ input, queryScope, featurePosition, action }) => {
      if (!isStandardCrudRecord(input)) throw new Error('缺少文件夹创建输入');
      const values = Object.fromEntries(['ENAME', 'Description'].filter(key => input[key] !== undefined).map(key => [key, input[key]]));
      const position = workspacePosition(queryScope);
      const response = await platformFetch('/api/workspace/content/folders', { method: 'POST', headers: folderCreateHeaders(featurePosition, action?.dataAccess?.id),
        body: JSON.stringify({ position, values, copyParentAuthorization: position.linkIds.length > 0 && input.copyParentAuthorization === true }) });
      if (!response.ok) throw await readStandardCrudRequestError(response);
      const payload: unknown = await response.json();
      return { operation: 'create', payload, change: { operation: 'create', id: '' } };
    },
    authorizeContent: context => authorization(context).actionHandlers.authorize(context),
    authorize: Object.assign((context: StandardCrudActionContext) => context.action?.inputKey === 'batchAuthorization' ? saveBatch(context) : authorization(context).actionHandlers.authorize(context),
      { isCorrectableInputError: batchOutcomeUnknown }),
    pasteObjectCopy: Object.assign(async ({ instance, input, queryScope, featurePosition }: import('@icp-platform/sdk').StandardCrudActionContext) => {
      const value = input as { draft?: ObjectCopyDraft; values?: Record<string, unknown> } | undefined;
      if (!value?.draft || !value.values) throw new StandardCrudRequestError('Workspace.CopyInputExpired', 409, {}, { code: 'Workspace.CopyInputExpired' });
      const receipt = await clipboard(instance).pasteObjectCopy(value.draft, value.values, workspacePosition(queryScope), featurePosition);
      return { operation: 'action' as const, payload: { kind: 'workspace.objectCopy', receipt, rows: value.values.items as ObjectCopyValuesRow[] | undefined }, change: { operation: 'create' as const, id: '' }, notice: { reasonKey: 'Workspace.ObjectCopied' } };
    }, { isCorrectableInputError: (failure: unknown) => failure instanceof StandardCrudRequestError
      && (['Part.CodeDuplicated', 'Document.CodeDuplicated', 'StandardCrud.PersistenceConflict', 'Workspace.CopySourceChanged', 'Workspace.CopyOutcomeUnknown'].includes(failure.code ?? '')
        || failure.status >= 400 && failure.status < 500 && Object.keys(failure.fieldErrors).some(key => key.startsWith('items.'))) }),
    resolveObjectCopy: async ({ instance, featurePosition, input }) => {
      const recovered = input as ObjectCopyReceipt | undefined;
      const receipt = recovered?.state === 'committed' || recovered?.state === 'cancelled' ? recovered : await clipboard(instance).resolveObjectCopy(false, featurePosition);
      return { operation: 'action', payload: { kind: 'workspace.objectCopy', receipt }, ...(receipt.succeeded ? { change: { operation: 'create' as const, id: '' } } : { noChange: true as const }),
        notice: { reasonKey: receipt.succeeded ? 'Workspace.ObjectCopied' : receipt.state === 'cancelled' ? 'Workspace.CopyCancelled' : 'Workspace.CopyNotCommitted' } };
    },
    pasteFolderCopy: Object.assign(async ({ instance, input, queryScope, featurePosition, action }: import('@icp-platform/sdk').StandardCrudActionContext) => {
      const value = input as { draft?: FolderCopyDraft; name?: string; copyParentAuthorization?: boolean } | undefined;
      if (!value?.draft || typeof value.name !== 'string') throw new StandardCrudRequestError('Workspace.FolderCopyInputExpired', 409, {}, { code: 'Workspace.FolderCopyInputExpired' });
      const receipt = await clipboard(instance).pasteFolderCopy(value.draft, value.name, workspacePosition(queryScope), featurePosition, 'copy', value.copyParentAuthorization, action?.dataAccess?.id);
      return { operation: 'action' as const, payload: receipt, change: { operation: 'update' as const, id: '' }, notice: { reasonKey: 'Workspace.FolderCopied' } };
    }, { isCorrectableInputError: (failure: unknown) => failure instanceof StandardCrudRequestError && failure.code === 'Workspace.SiblingNameConflict' }),
    pasteFolderStructure: Object.assign(async ({ instance, input, queryScope, featurePosition, action }: import('@icp-platform/sdk').StandardCrudActionContext) => {
      const value = input as { draft?: FolderCopyDraft; name?: string; copyParentAuthorization?: boolean } | undefined;
      if (!value?.draft || typeof value.name !== 'string') throw new StandardCrudRequestError('Workspace.FolderCopyInputExpired', 409, {}, { code: 'Workspace.FolderCopyInputExpired' });
      const receipt = await clipboard(instance).pasteFolderCopy(value.draft, value.name, workspacePosition(queryScope), featurePosition, 'copy-structure', value.copyParentAuthorization, action?.dataAccess?.id);
      return { operation: 'action' as const, payload: receipt, change: { operation: 'update' as const, id: '' }, notice: { reasonKey: 'Workspace.FolderStructureCopied' } };
    }, { isCorrectableInputError: (failure: unknown) => failure instanceof StandardCrudRequestError && failure.code === 'Workspace.SiblingNameConflict' }),
    copy: async ({ action, instance, id, selectedRows, queryScope, featurePosition }) => {
      const session = clipboard(instance);
      if (action?.key === 'clearClipboard') {
        if (!session.clear()) throw new StandardCrudRequestError('Workspace.ClipboardBusy', 409, {}, { code: 'Workspace.ClipboardBusy' });
        return { operation: 'action', payload: null, noChange: true, notice: { reasonKey: 'Workspace.ClipboardCleared' } };
      }
      const copied = await session.copy(workspacePosition(queryScope), id ? [id] : (selectedRows ?? []).map(row => String(row.LID)), featurePosition, action?.dataAccess?.id);
      return { operation: 'action', payload: null, noChange: true, ...(copied ? { notice: { reasonKey: 'Workspace.Copied' } } : {}) };
    },
    cut: async ({ action, instance, id, selectedRows, queryScope, featurePosition }) => {
      const session = clipboard(instance);
      const selected = await session.cut(workspacePosition(queryScope), id ? [id] : (selectedRows ?? []).map(row => String(row.LID)), featurePosition, action?.dataAccess?.id);
      return { operation: 'action', payload: null, noChange: true, ...(selected ? { notice: { reasonKey: 'Workspace.CutReady' } } : {}) };
    },
    paste: async ({ action, instance, queryScope, featurePosition }) => {
      const session = clipboard(instance); const mode = session.getSnapshot().clipboard?.mode;
      const receipt = await session.paste(workspacePosition(queryScope), featurePosition, action?.dataAccess?.id);
      return { operation: 'action', payload: receipt, ...(receipt.noChange ? { noChange: true as const } : { change: { operation: 'update' as const, id: '' } }),
        notice: { reasonKey: receipt.noChange ? 'Workspace.PositionUnchanged' : mode === 'cut' ? 'Workspace.Moved' : 'Workspace.Pasted' } };
    },
    reorder: async ({ action, id, queryScope, featurePosition }) => {
      if (!id || !action || !['moveUp', 'moveDown'].includes(action.key)) throw new Error('无效文件夹排序动作');
      if (!action.dataAccess?.id) throw new StandardCrudRequestError('缺少当前排序声明。', 400);
      const result = await referenceCommand('POST', '/api/workspace/content/tree-order', {
        parentPosition: workspacePosition(queryScope), linkId: id, direction: action.key === 'moveUp' ? 'up' : 'down',
      }, featurePosition, action.dataAccess.id);
      const receipt = result.payload as { moved: boolean; boundary: string | null };
      if (!receipt.moved && (receipt.boundary === 'first' || receipt.boundary === 'last')) return {
        operation: 'action', payload: receipt, noChange: true,
        notice: { reasonKey: receipt.boundary === 'first' ? 'Workspace.TreeOrderFirst' : 'Workspace.TreeOrderLast' },
      };
      return result;
    },
    editObject: async ({ id, input, queryScope, featurePosition, action }) => {
      if (!id) throw new Error('缺少引用标识');
      if (!action?.dataAccess?.id) throw new StandardCrudRequestError('缺少当前编辑声明。', 400);
      return referenceCommand('PUT', `/api/workspace/content/${encodeURIComponent(id)}`, { position: workspacePosition(queryScope), values: input }, featurePosition, action.dataAccess.id);
    },
    deleteObject: async ({ id, queryScope, featurePosition, action }) => {
      if (!id) throw new Error('缺少引用标识');
      if (!action?.dataAccess?.id) throw new StandardCrudRequestError('缺少当前删除对象声明。', 400);
      return referenceCommand('DELETE', `/api/workspace/content/${encodeURIComponent(id)}`, { position: workspacePosition(queryScope) }, featurePosition, action.dataAccess.id);
    },
    deleteObjects: async ({ selectedRows, queryScope, featurePosition, action }) => {
      if (!action?.dataAccess?.id) throw new StandardCrudRequestError('缺少当前选择删除声明。', 400);
      return referenceCommand('POST', '/api/workspace/content/delete',
        { position: workspacePosition(queryScope), linkIds: (selectedRows ?? []).map(row => String(row.LID)) }, featurePosition, action.dataAccess.id);
    },
    createPart: async ({ input, queryScope, featurePosition, action }) => {
      if (!action?.dataAccess?.id) throw new StandardCrudRequestError('缺少当前Part创建声明。', 400);
      return referenceCommand('POST', '/api/workspace/content/parts',
        { position: workspacePosition(queryScope), values: input }, featurePosition, action.dataAccess.id);
    },
    createDocument: Object.assign(async ({ input, queryScope, featurePosition, action }: import('@icp-platform/sdk').StandardCrudActionContext) => {
      const draft = typeof input === 'object' && input !== null ? input as WorkspaceDocumentDraft : undefined;
      if (!(draft?.session instanceof WorkspaceDocumentSession)) throw new Error('缺少有效的上传会话，请重新打开。');
      if (!action?.dataAccess?.id) throw new StandardCrudRequestError('缺少当前图文档创建声明。', 400);
      const payload = await draft.session.submit(draft, queryScope, featurePosition, action.dataAccess.id);
      return { operation: 'action' as const, payload, change: { operation: 'update' as const, id: '' } };
    }, {
      // 重复代码已确定未消费暂存；修正字段后可由用户再次保存，其他409沿共享冲突生命周期。
      isCorrectableInputError: (failure: unknown) => failure instanceof StandardCrudRequestError && failure.status === 409
        && failure.code === 'Document.CodeDuplicated'
        && failure.fieldErrorDetails.some(error => error.field === 'ECODE' && error.code === 'Document.CodeDuplicated'),
    }),
    remove: async ({ action, id, queryScope, featurePosition }) => {
      if (!id) throw new Error('缺少引用标识');
      return referenceCommand('DELETE', `/api/workspace/content/references/${encodeURIComponent(id)}`, { position: workspacePosition(queryScope) }, featurePosition, action?.dataAccess?.id);
    },
    removeSelected: async ({ action, selectedRows, queryScope, featurePosition }) => referenceCommand('POST', '/api/workspace/content/references/remove',
      { position: workspacePosition(queryScope), linkIds: (selectedRows ?? []).map(row => String(row.LID)) }, featurePosition, action?.dataAccess?.id),
  },
  actionEligibilityResolvers: { ...transfer.actionEligibilityResolvers, ...ownership.actionEligibilityResolvers,
    authorizeContent: context => authorization(context).actionEligibilityResolvers.authorize(context) },
  actionStateResolver: ({ action, instance, queryScope }) => {
    const root = queryScope?.linkIds === '[]';
    if (root && ['createPart', 'createDocument', 'pasteObjectCopy'].includes(action.key)) return { visible: false, available: false, reasonKey: 'Workspace.RootOnlyFolders' };
    const state = instance?.active ? clipboard(instance).getSnapshot() : undefined;
    const label = state?.clipboard?.mode === 'cut' && ['cut', 'cutSelected', 'paste'].includes(action.key)
      ? { labelKey: action.key === 'paste' ? 'Workspace.PastePending' : 'Workspace.CutPending' } : {};
    if (state?.phase === 'pasting' || state?.phase === 'validating') return { visible: true, available: false, reasonKey: 'Workspace.ClipboardBusy', ...label };
    if (action.key === 'pasteObjectCopy') return { visible: true, available: !!instance?.active && clipboard(instance).canCopyObject(), reasonKey: 'Workspace.ObjectCopySourceRequired' };
    if (action.key === 'resolveObjectCopy') return { visible: !!instance?.active && clipboard(instance).canResolveObjectCopy(), available: true };
    if (['pasteFolderCopy', 'pasteFolderStructure'].includes(action.key)) return { visible: true, available: !!instance?.active && clipboard(instance).canCopyFolder(), reasonKey: state?.phase === 'unknown' ? 'Workspace.ClipboardOutcomeUnknown' : 'Workspace.FolderCopySourceRequired' };
    if (!['copy', 'copySelected', 'cut', 'cutSelected', 'paste', 'clearClipboard'].includes(action.key)) return { visible: true, available: true };
    if (!state) return { visible: true, available: false, reasonKey: 'Workspace.ClipboardUnavailable' };
    if (action.key === 'paste' && state.phase === 'unknown') return { visible: true, available: false, reasonKey: 'Workspace.ClipboardOutcomeUnknown', ...label };
    if (['paste', 'clearClipboard'].includes(action.key) && !state.clipboard) return { visible: true, available: false, reasonKey: 'Workspace.ClipboardUnavailable' };
    return { visible: true, available: true, ...label };
  },
  dataAdapter: {
    getReadNavigationParameters: (row, context) => {
      const source = authorizationSource({ row, id: typeof row.LID === 'string' ? row.LID : undefined, queryScope: context.queryScope });
      return { objectId: source.objectId, modelKey: source.modelKey, sourceLinkId: source.sourceLinkId,
        spaceId: source.spaceId, rootFolderId: source.rootFolderId, linkIds: JSON.stringify(source.linkIds), name: String(row.Name ?? '') };
    },
    list: async (query = {}, context) => {
      const { anchorId, ...position } = context?.queryScope ?? {};
      if (typeof anchorId !== 'string' || !position.spaceId || !position.rootFolderId || !position.linkIds) throw new Error('缺少工作空间出现位置');
      const parameters = new URLSearchParams({ direction: 'forward', anchorModelKey: 'WorkspaceFolder', anchorId,
        queryScope: JSON.stringify(position) });
      for (const [key, value] of Object.entries(query)) if (value !== undefined && value !== null && value !== '') parameters.set(key, String(value));
      if (!context?.dataAccess?.id) throw new StandardCrudRequestError('缺少当前内容读取声明。', 400);
      const response = await platformFetch(`/api/links/workspace.content?${parameters}`, { signal: context.signal,
        headers: { ...headers(context.featurePosition), 'X-NewP-Data-Declaration': context.dataAccess.id } });
      if (!response.ok) throw await readStandardCrudRequestError(response);
      const payload = await response.json() as { items: LinkRow[]; total: number; page: number; pageSize: number };
      return { ...payload, items: payload.items.map(row => ({ ...row.fields, LID: row.linkId,
        ObjectModelKey: row.associatedObject.modelKey, ObjectId: row.associatedObject.id })) };
    },
    get: async (id, context) => {
      // 树动作 recordId 是入向 LID；对象读取仍使用 resolver 提供的 EID，写入再重验源位置。
      const referenceObjectId = context?.queryScope?.referenceObjectId;
      const response = await platformFetch(`/api/workspace/folders/${encodeURIComponent(typeof referenceObjectId === 'string' ? referenceObjectId : id)}`, {
        signal: context?.signal, headers: { ...headers(context?.featurePosition),
          ...(context?.dataAccess?.id ? { 'X-NewP-Data-Declaration': context.dataAccess.id } : {}) },
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
      const response = await platformFetch('/api/workspace/content/folders', { method: 'POST', headers: folderCreateHeaders(context?.featurePosition, context?.dataAccess?.id),
        body: JSON.stringify({ position, values: fields }) });
      if (!response.ok) throw await readStandardCrudRequestError(response);
      const payload: unknown = await response.json();
      // unknown-change 刷新全部已加载出现位置，读取失败不能重放已成功创建。
      return { operation: 'create', payload, change: { operation: 'create', id: '' } };
    },
    getRowId: row => String(row.LID),
    getRowModelKey: row => typeof row.ObjectModelKey === 'string' ? row.ObjectModelKey : '',
    getRowActivationTarget: (row, context) => {
      if (row.ObjectModelKey !== 'WorkspaceFolder') return undefined;
      if (typeof row.LID !== 'string' || !row.LID) throw new Error('缺少文件夹引用标识');
      const position = workspacePosition(context.queryScope);
      return { kind: 'tree-node', nodeKey: [position.spaceId, position.rootFolderId, ...position.linkIds, row.LID].map(encodeURIComponent).join(':') };
    },
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
