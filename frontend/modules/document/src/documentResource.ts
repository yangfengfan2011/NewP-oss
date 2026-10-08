import { platformFetch } from '@icp-platform/core';
import {
  createStandardCrudRestDataAdapter,
  isStandardCrudRecord,
  readStandardCrudString,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';

// 保留 canonical raw/display 输出，字段与布局由正式 Model/View 投影拥有。
export const documentResource: StandardCrudResourceRegistration = {
  resourceKey: 'document.document',
  listViewKey: 'document.document.list',
  detailViewKey: 'document.document.detail',
  dataAdapter: createStandardCrudRestDataAdapter({
    apiRoute: '/api/document/documents',
    fetcher: (input, init) => platformFetch(input, init),
    mapListItem: (value): Record<string, unknown> => {
      const row = isStandardCrudRecord(value) ? value : {};
      return { ...row, id: readStandardCrudString(row, ['EID']) };
    },
    getRowId: row => String(row.id),
  }),
};
