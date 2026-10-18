import { createElement } from 'react';
import { platformFetch, useLocalization } from '@icp-platform/core';
import { isStandardCrudRecord, readStandardCrudRequestError, StandardCrudRequestError, type StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { workspaceContentSource } from './workspaceTree';

function invalid(): never { throw new StandardCrudRequestError('接收通知格式无效。', 502); }
function readNotification(value: unknown): Record<string, unknown> {
  if (!isStandardCrudRecord(value) || typeof value.id !== 'string' || !value.id || typeof value.occurredAt !== 'string'
    || !['available', 'unavailable'].includes(String(value.status)) || (value.senderName !== null && typeof value.senderName !== 'string')) return invalid();
  let location: ReturnType<typeof workspaceContentSource> | undefined;
  if (value.status === 'available') {
    if (typeof value.targetName !== 'string' || !value.targetName || !isStandardCrudRecord(value.location)
      || !isStandardCrudRecord(value.location.position)) return invalid();
    location = workspaceContentSource({ ...value.location.position, objectId: value.location.objectId,
      modelKey: value.location.modelKey, sourceLinkId: value.location.sourceLinkId });
  }
  // 白名单投影；不可用目标即使带有多余原始字段也不进入行/导航参数。
  return { id: value.id, occurredAt: value.occurredAt, senderName: value.senderName,
    targetName: location ? value.targetName : null, location };
}
function SafeName({ value, unavailable }: { value: unknown; unavailable: string }) {
  const { t } = useLocalization();
  return createElement('span', null, typeof value === 'string' && value ? value : t(unavailable));
}

/** 通知记录只有接收人私有列表身份；打开沿原对象正式读取入口重新验证，不创建通知 ACL。 */
const resource: StandardCrudResourceRegistration = {
  resourceKey: 'workspace.transfers', listViewKey: 'workspace.transfers.list',
  dataAdapter: {
    list: async (query = {}, context) => {
      if (!context?.featurePosition) throw new StandardCrudRequestError('缺少接收通知功能位置。', 400);
      const parameters = new URLSearchParams({ page: String(query.page ?? 1), pageSize: String(query.pageSize ?? 20) });
      const response = await platformFetch(`/api/workspace/transfers/notifications?${parameters}`, {
        signal: context.signal, headers: { 'X-NewP-Feature': context.featurePosition } });
      if (!response.ok) throw await readStandardCrudRequestError(response);
      const payload: unknown = await response.json();
      if (!isStandardCrudRecord(payload) || !Array.isArray(payload.items) || !Number.isInteger(payload.total) || Number(payload.total) < 0
        || !Number.isInteger(payload.page) || Number(payload.page) < 1 || !Number.isInteger(payload.pageSize) || Number(payload.pageSize) < 1) return invalid();
      return { items: payload.items.map(readNotification), total: Number(payload.total), page: Number(payload.page), pageSize: Number(payload.pageSize) };
    },
    get: async () => { throw new StandardCrudRequestError('请通过对象读取入口打开。', 400); },
    getRowId: row => String(row.id),
    getReadNavigationParameters: row => {
      if (!isStandardCrudRecord(row.location)) throw new StandardCrudRequestError('对象当前不可读取。', 403);
      const source = workspaceContentSource(row.location);
      return { objectId: source.objectId, modelKey: source.modelKey, sourceLinkId: source.sourceLinkId,
        spaceId: source.spaceId, rootFolderId: source.rootFolderId, linkIds: JSON.stringify(source.linkIds) };
    },
  },
  actionStateResolver: ({ row }) => ({ visible: isStandardCrudRecord(row?.location), available: isStandardCrudRecord(row?.location) }),
  listFieldRenderers: {
    senderName: ({ value }) => createElement(SafeName, { value, unavailable: 'Workspace.TransferSenderUnavailable' }),
    targetName: ({ value }) => createElement(SafeName, { value, unavailable: 'Workspace.TransferTargetUnavailable' }),
  },
};
export default resource;
