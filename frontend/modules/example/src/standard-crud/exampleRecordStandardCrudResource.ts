import { platformFetch } from '@icp-platform/core';
import {
  createStandardCrudRestDataAdapter,
  isStandardCrudRecord,
  readStandardCrudNullableString,
  readStandardCrudString,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';

export interface ExampleRecordListRow extends Record<string, unknown> {
  id: string;
  EID: string;
  ECODE: string;
  ENAME: string;
  ReviewerUserEID: string | null;
  ReviewerUserEID_DisplayValue: string | null;
}

function mapExampleRecordListItem(value: unknown): ExampleRecordListRow {
  const row = isStandardCrudRecord(value) ? value : {};
  const eid = readStandardCrudString(row, ['EID', 'eid']);

  return {
    id: eid,
    EID: eid,
    ECODE: readStandardCrudString(row, ['ECODE', 'ecode']),
    ENAME: readStandardCrudString(row, ['ENAME', 'ename']),
    ReviewerUserEID: readStandardCrudNullableString(row, ['ReviewerUserEID', 'reviewerUserEID']),
    ReviewerUserEID_DisplayValue: readStandardCrudNullableString(
      row,
      ['ReviewerUserEID_DisplayValue', 'reviewerUserEID_DisplayValue'],
    ),
  };
}

/** 创建持久化 ExampleRecord 的标准 REST 注册，Shell 只消费规范 list/detail 与 mutation 生命周期。 */
export function createExampleRecordStandardCrudResource(
  fetcher: typeof fetch = platformFetch as typeof fetch,
): StandardCrudResourceRegistration<Record<string, unknown>, ExampleRecordListRow> {
  return {
    resourceKey: 'example.record',
    dataAdapter: createStandardCrudRestDataAdapter({
      apiRoute: '/api/example/records',
      fetcher,
      mapListItem: mapExampleRecordListItem,
      getRowId: (row: ExampleRecordListRow) => row.id,
    }),
    listViewKey: 'example.record.list',
    detailViewKey: 'example.record.detail',
  };
}

export const exampleRecordStandardCrudResource = createExampleRecordStandardCrudResource();
