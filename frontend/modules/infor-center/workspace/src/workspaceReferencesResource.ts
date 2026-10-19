import { platformFetch } from '@icp-platform/core';
import { readStandardCrudRequestError, StandardCrudRequestError, type StandardCrudResourceRegistration } from '@icp-platform/sdk';
import folder from './workspaceRootResource';

/** 同一标准 Adapter 读取反向关系，行身份仍是 LID；父对象属性从关联端点 EID 打开。 */
const resource: StandardCrudResourceRegistration = {
  resourceKey: 'workspace.references', listViewKey: 'workspace.references.list', detailViewKey: 'workspace.folder.detail',
  dataAdapter: {
    ...folder.dataAdapter,
    list: async (query = {}, context) => {
      const { anchorId, anchorModelKey } = context?.queryScope ?? {};
      if (typeof anchorId !== 'string' || !anchorId || typeof anchorModelKey !== 'string' || !anchorModelKey) throw new Error('缺少直接引用对象');
      const parameters = new URLSearchParams({ direction: 'reverse', anchorModelKey, anchorId });
      for (const [key, value] of Object.entries(query)) if (value !== undefined && value !== null && value !== '') parameters.set(key, String(value));
      if (!context?.dataAccess?.id) throw new StandardCrudRequestError('缺少当前引用读取声明。', 400);
      const response = await platformFetch(`/api/links/workspace.content?${parameters}`, { signal: context.signal,
        headers: { ...(context.featurePosition ? { 'X-NewP-Feature': context.featurePosition } : {}), 'X-NewP-Data-Declaration': context.dataAccess.id } });
      if (!response.ok) throw await readStandardCrudRequestError(response);
      const payload = await response.json() as { items: Array<{ linkId: string; associatedObject: { modelKey: string; id: string }; fields: Record<string, unknown> }>; total: number; page: number; pageSize: number };
      return { ...payload, items: payload.items.map(row => ({ ...row.fields, LID: row.linkId,
        ObjectModelKey: row.associatedObject.modelKey, ObjectId: row.associatedObject.id })) };
    },
  },
};
export default resource;
