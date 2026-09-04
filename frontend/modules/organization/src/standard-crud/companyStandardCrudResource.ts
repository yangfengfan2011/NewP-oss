import { platformFetch } from '@icp-platform/core';
import {
  createStandardCrudRestDataAdapter,
  isStandardCrudRecord,
  readStandardCrudBoolean,
  readStandardCrudString,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';

export interface CompanyListRow {
  id: string;
  EID: string;
  ECODE: string;
  ENAME: string;
  IsEnabled: boolean;
  SortOrder: string;
  UpdatedAt: string;
}

export function mapCompanyListItem(value: unknown): CompanyListRow {
  const row = isStandardCrudRecord(value) ? value : {};
  const id = readStandardCrudString(row, ['EID']);
  return {
    id,
    EID: id,
    ECODE: readStandardCrudString(row, ['ECODE']),
    ENAME: readStandardCrudString(row, ['ENAME']),
    IsEnabled: readStandardCrudBoolean(row, ['IsEnabled'], true),
    SortOrder: readStandardCrudString(row, ['SortOrder']),
    UpdatedAt: readStandardCrudString(row, ['UpdatedAt']),
  };
}

const companyStandardCrudDataAdapter = createStandardCrudRestDataAdapter({
  apiRoute: '/api/organization/companies',
  fetcher: (input, init) => platformFetch(input, init),
  mapListItem: mapCompanyListItem,
  getRowId: (row: CompanyListRow) => row.id,
});

// 公司页面只使用标准 REST 生命周期；启停属于普通 IsEnabled 编辑，不注册模块命令或版本字段。
export const companyStandardCrudResource: StandardCrudResourceRegistration<Record<string, unknown>, CompanyListRow> = {
  resourceKey: 'organization.company',
  dataAdapter: companyStandardCrudDataAdapter,
  listViewKey: 'organization.company.list',
};
