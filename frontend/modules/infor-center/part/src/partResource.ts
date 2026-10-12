import { platformFetch } from '@icp-platform/core';
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
