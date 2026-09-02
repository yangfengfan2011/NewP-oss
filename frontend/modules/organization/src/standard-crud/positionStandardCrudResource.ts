import { platformFetch } from '@icp-platform/core';
import {
  createStandardCrudRestDataAdapter,
  isStandardCrudRecord,
  readStandardCrudBoolean,
  readStandardCrudNullableString,
  readStandardCrudString,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';

export interface PositionListRow {
  id: string;
  EID: string;
  ENAME: string;
  Description: string | null;
  IsEnabled: boolean;
}

export function mapPositionListItem(value: unknown): PositionListRow {
  const row = isStandardCrudRecord(value) ? value : {};
  const id = readStandardCrudString(row, ['EID']);
  return {
    id,
    EID: id,
    ENAME: readStandardCrudString(row, ['ENAME']),
    Description: readStandardCrudNullableString(row, ['Description']),
    IsEnabled: readStandardCrudBoolean(row, ['IsEnabled'], true),
  };
}

const positionStandardCrudDataAdapter = createStandardCrudRestDataAdapter({
  apiRoute: '/api/organization/positions',
  fetcher: (input, init) => platformFetch(input, init),
  mapListItem: mapPositionListItem,
  getRowId: (row: PositionListRow) => row.id,
});

// 岗位创建和编辑只使用标准 REST adapter；启停作为 IsEnabled 普通字段写入，不注册岗位专用 handler。
export const positionStandardCrudResource: StandardCrudResourceRegistration<Record<string, unknown>, PositionListRow> = {
  resourceKey: 'organization.position',
  dataAdapter: positionStandardCrudDataAdapter,
  listViewKey: 'organization.position.list',
};
