import { platformFetch } from '@icp-platform/core';
import { readStandardCrudRequestError, type StandardCrudDetail, type StandardCrudResourceRegistration } from '@icp-platform/sdk';

interface LinkRow { linkId: string; associatedObject: { modelKey: string; id: string }; fields: Record<string, unknown> }
const headers = (featurePosition?: string): Record<string, string> => featurePosition ? { 'X-NewP-Feature': featurePosition } : {};

/** 本片正式目录仅有文件夹；关系身份参与表格，属性打开始终使用真实端点 EID。 */
const resource: StandardCrudResourceRegistration = {
  resourceKey: 'workspace.folder', listViewKey: 'workspace.folder.list', detailViewKey: 'workspace.folder.detail',
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
  },
};
export default resource;
