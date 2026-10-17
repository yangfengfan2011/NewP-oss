import { platformFetch } from '@icp-platform/core';
import { createObjectOwnershipActions } from '@icp-platform/ui';
import {
  createStandardCrudRestDataAdapter,
  isStandardCrudRecord,
  readStandardCrudString,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';

// 保留 canonical raw/display 输出，字段与布局由正式 Model/View 投影拥有。
export const partResource: StandardCrudResourceRegistration = {
  resourceKey: 'part.part',
  listViewKey: 'part.part.list',
  detailViewKey: 'part.part.detail',
  ...createObjectOwnershipActions(({ id, row }) => ({ initialId: id ?? (row as { EID?: string } | undefined)?.EID, pendingPath: '/api/part/parts/ownership/pending',
    statusPath: '/api/part/parts/ownership/{id}/status', savePath: '/api/part/parts/ownership/{id}',
    accountsPath: '/api/part/parts/ownership/accounts', eligibilityPath: '/api/part/parts/ownership/eligibility', batchPath: '/api/part/parts/ownership/initialize' }), '/api/part/parts/ownership/eligibility'),
  dataAdapter: createStandardCrudRestDataAdapter({
    apiRoute: '/api/part/parts',
    fetcher: (input, init) => platformFetch(input, init),
    mapListItem: (value): Record<string, unknown> => {
      const row = isStandardCrudRecord(value) ? value : {};
      return { ...row, id: readStandardCrudString(row, ['EID']) };
    },
    getRowId: row => String(row.id),
  }),
};
