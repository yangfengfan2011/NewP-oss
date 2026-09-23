import { platformFetch } from '@icp-platform/core';
import {
  createStandardCrudRestDataAdapter,
  isStandardCrudRecord,
  readStandardCrudBoolean,
  readStandardCrudNullableString,
  readStandardCrudString,
  type StandardCrudLookupProvider,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';
import { createOrganizationObjectAuthorizationActions } from './organizationObjectAuthorizationActions';
import {
  getDepartmentCompanyLookup,
  getDepartmentParentLookup,
} from '../services/organizationService';

export interface DepartmentListRow {
  id: string;
  EID: string;
  CompanyId: string;
  CompanyId_DisplayValue: string | null;
  ParentDepartmentId: string | null;
  ParentDepartmentId_DisplayValue: string | null;
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
    ParentDepartmentId: readStandardCrudNullableString(row, ['ParentDepartmentId']),
    ParentDepartmentId_DisplayValue: readStandardCrudNullableString(row, ['ParentDepartmentId_DisplayValue']),
    ECODE: readStandardCrudString(row, ['ECODE']),
    ENAME: readStandardCrudString(row, ['ENAME']),
    IsEnabled: readStandardCrudBoolean(row, ['IsEnabled'], true),
    SortOrder: readStandardCrudString(row, ['SortOrder']),
    UpdatedAt: readStandardCrudString(row, ['UpdatedAt']),
  };
}

const departmentStandardCrudDataAdapter = createStandardCrudRestDataAdapter({
  hasChangeSnapshot: fields => typeof fields.CompanyId === 'string' && (fields.ParentDepartmentId === null || typeof fields.ParentDepartmentId === 'string'),
  apiRoute: '/api/organization/departments',
  fetcher: (input, init) => platformFetch(input, init),
  mapListItem: mapDepartmentListItem,
  getRowId: (row: DepartmentListRow) => row.id,
});

const departmentCompanyLookupProvider: StandardCrudLookupProvider = {
  key: 'organization.companies',
  load: async (request, context) => getDepartmentCompanyLookup(
    request,
    request.fieldKey === 'companyId',
    context?.signal,
    context?.featurePosition,
  ),
};

const departmentParentLookupProvider: StandardCrudLookupProvider = {
  key: 'organization.department.parents',
  load: async (request, context) => {
    const companyId = request.dependencyValues.CompanyId;
    if (typeof companyId !== 'string' || companyId.length === 0) {
      return { options: [], currentOptions: [], page: request.page, pageSize: request.pageSize, hasMore: false };
    }

    const code = typeof request.values.ECODE === 'string' && request.values.ECODE.length > 0
      ? request.values.ECODE
      : undefined;
    return getDepartmentParentLookup(companyId, code, request, false, context?.signal, context?.featurePosition);
  },
};

const departmentParentFilterLookupProvider: StandardCrudLookupProvider = {
  key: 'organization.department.parent-filter',
  load: async (request, context) => {
    const companyId = request.dependencyValues.companyId;
    if (typeof companyId !== 'string' || companyId.length === 0) {
      return { options: [], currentOptions: [], page: request.page, pageSize: request.pageSize, hasMore: false };
    }

    return getDepartmentParentLookup(companyId, undefined, request, true, context?.signal, context?.featurePosition);
  },
};

// 部门页面复用标准 REST 生命周期，并把公司与上级部门候选留在自己的 Organization provider 内。
export const departmentStandardCrudResource: StandardCrudResourceRegistration<Record<string, unknown>, DepartmentListRow> = {
  resourceKey: 'organization.department',
  dataAdapter: departmentStandardCrudDataAdapter,
  listViewKey: 'organization.department.list',
  detailViewKey: 'organization.department.detail',
  lookupProviders: [
    departmentCompanyLookupProvider,
    departmentParentLookupProvider,
    departmentParentFilterLookupProvider,
  ],
  ...createOrganizationObjectAuthorizationActions('organization.department', '部门'),
};
