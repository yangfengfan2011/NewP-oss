import { platformFetch } from '@icp-platform/core';
import {
  createStandardCrudRestDataAdapter,
  isStandardCrudRecord,
  readStandardCrudBoolean,
  readStandardCrudNullableString,
  readStandardCrudString,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';
import { organizationCompanyLookupProvider } from './personStandardCrudResource';

export interface DepartmentListRow {
  id: string;
  EID: string;
  CompanyId: string;
  CompanyId_DisplayValue: string | null;
  ECODE: string;
  ENAME: string;
  IsEnabled: boolean;
  SortOrder: string;
  UpdatedAt: string;
}

export function mapDepartmentListItem(value: unknown): DepartmentListRow {
  const row = isStandardCrudRecord(value) ? value : {};
  const id = readStandardCrudString(row, ['EID']);
  return {
    id,
    EID: id,
    CompanyId: readStandardCrudString(row, ['CompanyId']),
    CompanyId_DisplayValue: readStandardCrudNullableString(row, ['CompanyId_DisplayValue']),
    ECODE: readStandardCrudString(row, ['ECODE']),
    ENAME: readStandardCrudString(row, ['ENAME']),
    IsEnabled: readStandardCrudBoolean(row, ['IsEnabled'], true),
    SortOrder: readStandardCrudString(row, ['SortOrder']),
    UpdatedAt: readStandardCrudString(row, ['UpdatedAt']),
  };
}

const departmentStandardCrudDataAdapter = createStandardCrudRestDataAdapter({
  apiRoute: '/api/organization/departments',
  fetcher: (input, init) => platformFetch(input, init),
  mapListItem: mapDepartmentListItem,
  getRowId: (row: DepartmentListRow) => row.id,
});

// 根部门页面复用标准 REST 生命周期和 Organization 公司 Lookup，不注册层级、命令或版本字段。
export const departmentStandardCrudResource: StandardCrudResourceRegistration<Record<string, unknown>, DepartmentListRow> = {
  resourceKey: 'organization.department',
  dataAdapter: departmentStandardCrudDataAdapter,
  listViewKey: 'organization.department.list',
  lookupProviders: [organizationCompanyLookupProvider],
};
