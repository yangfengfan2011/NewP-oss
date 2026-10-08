import { platformFetch } from '@icp-platform/core';
import { readStandardCrudRequestError, type StandardCrudDetail, type StandardCrudResourceRegistration } from '@icp-platform/sdk';

/** List View 仅提供共享详情宿主的正式资源关联，本片不装载内容列表。 */
const resource: StandardCrudResourceRegistration = {
  resourceKey: 'workspace.folder', listViewKey: 'workspace.folder.list', detailViewKey: 'workspace.folder.detail',
  dataAdapter: {
    list: async () => { throw new Error('工作空间内容列表尚未接入'); },
    get: async (rootFolderId, context) => {
      const response = await platformFetch(`/api/workspace/spaces/roots/${encodeURIComponent(rootFolderId)}`, {
        signal: context?.signal, headers: context?.featurePosition ? { 'X-NewP-Feature': context.featurePosition } : {},
      });
      if (!response.ok) throw await readStandardCrudRequestError(response);
      const payload = await response.json() as { fields: Record<string, unknown> };
      const detail: StandardCrudDetail = { fields: payload.fields, summaries: {} };
      return { normalized: detail, value: detail };
    },
    getRowId: row => String(row.EID),
  },
};
export default resource;
