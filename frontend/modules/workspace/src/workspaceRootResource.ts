import { platformFetch } from '@icp-platform/core';
import { createElement, lazy, Suspense } from 'react';
import { readStandardCrudRequestError, type StandardCrudDetail, type StandardCrudResourceRegistration } from '@icp-platform/sdk';

interface LinkRow { linkId: string; associatedObject: { modelKey: string; id: string }; fields: Record<string, unknown> }
const headers = (featurePosition?: string): Record<string, string> => featurePosition ? { 'X-NewP-Feature': featurePosition } : {};
const FolderCandidateInput = lazy(() => import('./FolderCandidateInput'));
function positionFromScope(scope?: Record<string, unknown>) {
  if (typeof scope?.spaceId !== 'string' || typeof scope.rootFolderId !== 'string' || typeof scope.linkIds !== 'string') throw new Error('缺少工作空间出现位置');
  return { spaceId: scope.spaceId, rootFolderId: scope.rootFolderId, linkIds: JSON.parse(scope.linkIds) as unknown };
}
async function referenceCommand(method: string, path: string, body: unknown, featurePosition?: string) {
  const response = await platformFetch(path, { method, headers: { ...headers(featurePosition), 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  if (!response.ok) throw await readStandardCrudRequestError(response);
  return { operation: 'action' as const, payload: await response.json() as unknown, change: { operation: 'update' as const, id: '' } };
}

/** 关系身份参与表格；异构属性使用所属模块正式 View 和真实 EID，未知类型不能猜测。 */
const resource: StandardCrudResourceRegistration = {
  resourceKey: 'workspace.folder', listViewKey: 'workspace.folder.list', detailViewKey: 'workspace.folder.detail',
  enumOptions: { 'workspace.content.types': [
    { value: '', label: '全部', labelKey: 'Workspace.All' }, { value: 'Workspace/WorkspaceFolder', label: '文件夹', labelKey: 'Workspace.Folder' },
    { value: 'Part/Part', label: '零组件', labelKey: 'Part.Title' }, { value: 'Document/Document', label: '图文档', labelKey: 'Document.Title' },
  ] },
  listFieldRenderers: { ObjectType: ({ row, value }) => createElement('span', null,
    createElement('span', { 'aria-hidden': true }, row.ObjectModelKey === 'Workspace/WorkspaceFolder' ? '📁 ' : row.ObjectModelKey === 'Part/Part' ? '▦ ' : '▤ '), String(value ?? '')) },
  actionInputRenderers: {
    folderCandidate: context => createElement(Suspense, { fallback: null }, createElement(FolderCandidateInput, context)),
  },
  actionHandlers: {
    add: async ({ input, queryScope, featurePosition }) => {
      if (typeof input !== 'object' || input === null || !('modelKey' in input) || !('id' in input)
        || typeof input.modelKey !== 'string' || typeof input.id !== 'string' || !input.id) throw new Error('请选择对象');
      return referenceCommand('POST', '/api/workspace/content/references', { position: positionFromScope(queryScope), target: input }, featurePosition);
    },
    remove: async ({ id, queryScope, featurePosition }) => {
      if (!id) throw new Error('缺少引用标识');
      return referenceCommand('DELETE', `/api/workspace/content/references/${encodeURIComponent(id)}`, { position: positionFromScope(queryScope) }, featurePosition);
    },
  },
  dataAdapter: {
    list: async (query = {}, context) => {
      const { anchorId, ...position } = context?.queryScope ?? {};
      if (typeof anchorId !== 'string' || !position.spaceId || !position.rootFolderId || !position.linkIds) throw new Error('缺少工作空间出现位置');
      const parameters = new URLSearchParams({ direction: 'forward', anchorModelKey: 'Workspace/WorkspaceFolder', anchorId,
        queryScope: JSON.stringify(position) });
      for (const [key, value] of Object.entries(query)) if (value !== undefined && value !== null && value !== '') parameters.set(key, String(value));
      const response = await platformFetch(`/api/links/workspace.content?${parameters}`, { signal: context?.signal, headers: headers(context?.featurePosition) });
      if (!response.ok) throw await readStandardCrudRequestError(response);
      const payload = await response.json() as { items: LinkRow[]; total: number; page: number; pageSize: number };
      return { ...payload, items: payload.items.map(row => ({ ...row.fields, LID: row.linkId,
        ObjectModelKey: row.associatedObject.modelKey, ObjectId: row.associatedObject.id })) };
    },
    get: async (id, context) => {
      const response = await platformFetch(`/api/workspace/folders/${encodeURIComponent(id)}`, {
        signal: context?.signal, headers: headers(context?.featurePosition),
      });
      if (!response.ok) throw await readStandardCrudRequestError(response);
      const payload = await response.json() as { fields: Record<string, unknown> };
      const detail: StandardCrudDetail = { fields: payload.fields, summaries: {} };
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
      if (row.ObjectModelKey !== 'Workspace/WorkspaceFolder' || typeof row.ObjectId !== 'string') throw new Error('未登记的文件夹属性端点');
      return row.ObjectId;
    },
    getDetailTarget: row => {
      if (typeof row.ObjectId !== 'string' || !row.ObjectId) throw new Error('缺少对象标识');
      if (row.ObjectModelKey === 'Workspace/WorkspaceFolder') return undefined;
      const views: Record<string, string> = { 'Part/Part': 'part.part.detail', 'Document/Document': 'document.document.detail' };
      const view = views[String(row.ObjectModelKey)];
      if (!view) throw new Error('未登记的对象属性端点');
      return { detailViewKey: view, id: row.ObjectId };
    },
  },
};
export default resource;
