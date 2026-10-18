import { platformFetch } from '@icp-platform/core';
import { createObjectOwnershipActions, createObjectAuthorizationActions, createObjectOwnershipTransferActions } from '@icp-platform/ui';
import {
  createStandardCrudRestDataAdapter,
  isStandardCrudRecord,
  readStandardCrudString,
  StandardCrudRequestError,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';

const partDataAdapter = createStandardCrudRestDataAdapter({
  apiRoute: '/api/part/parts',
  fetcher: (input, init) => platformFetch(input, init),
  mapListItem: (value): Record<string, unknown> => {
    const row = isStandardCrudRecord(value) ? value : {};
    return { ...row, id: readStandardCrudString(row, ['EID']) };
  },
  getRowId: row => String(row.id),
});

const ownership = createObjectOwnershipActions(({ id, row }) => ({ initialId: id ?? (row as { EID?: string } | undefined)?.EID, pendingPath: '/api/part/parts/ownership/pending',
  statusPath: '/api/part/parts/ownership/{id}/status', savePath: '/api/part/parts/ownership/{id}',
  accountsPath: '/api/part/parts/ownership/accounts', eligibilityPath: '/api/part/parts/ownership/eligibility', batchPath: '/api/part/parts/ownership/initialize' }), '/api/part/parts/ownership/eligibility');

const authorization = createObjectAuthorizationActions('part.part', context => {
  const fields = isStandardCrudRecord(context.row) ? context.row
    : isStandardCrudRecord(context.detail) && isStandardCrudRecord(context.detail.fields) ? context.detail.fields : {};
  const id = context.id ?? readStandardCrudString(fields, ['EID', 'id']);
  return { id, title: '零组件', titleKey: 'Part.Title', name: readStandardCrudString(fields, ['ENAME']),
    basePath: `/api/part/parts/${encodeURIComponent(id)}/permissions` };
});

const transfer = createObjectOwnershipTransferActions(context => {
  const fields = isStandardCrudRecord(context.row) ? context.row
    : isStandardCrudRecord(context.detail) && isStandardCrudRecord(context.detail.fields) ? context.detail.fields : {};
  const id = context.id ?? readStandardCrudString(fields, ['EID', 'id']);
  if (!id) throw new StandardCrudRequestError('缺少转交对象。', 409);
  const base = `/api/part/parts/${encodeURIComponent(id)}/transfer`;
  return { savePath: base, eligibilityPath: `${base}/eligibility`, accountsPath: `${base}/accounts` };
});

// 保留 canonical raw/display 输出，字段与布局由正式 Model/View 投影拥有。
export const partResource: StandardCrudResourceRegistration = {
  resourceKey: 'part.part',
  listViewKey: 'part.part.list',
  detailViewKey: 'part.part.detail',
  actionHandlers: { ...transfer.actionHandlers, ...ownership.actionHandlers, ...authorization.actionHandlers },
  actionInputRenderers: { ...transfer.actionInputRenderers, ...ownership.actionInputRenderers, ...authorization.actionInputRenderers },
  actionEligibilityResolvers: { ...transfer.actionEligibilityResolvers, ...ownership.actionEligibilityResolvers, ...authorization.actionEligibilityResolvers },
  dataAdapter: {
    ...partDataAdapter,
    get: (id, context) => {
      // 只读页签携带原入口位置；普通详情已有的入口位置仍然优先。
      const sourceFeaturePosition = context?.queryScope?.sourceFeaturePosition;
      return partDataAdapter.get(id, context?.featurePosition === undefined && typeof sourceFeaturePosition === 'string'
        ? { ...context, featurePosition: sourceFeaturePosition } : context);
    },
  },
};
