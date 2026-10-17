import { platformFetch } from '@icp-platform/core';
import {
  createStandardCrudRestDataAdapter,
  isStandardCrudRecord,
  readStandardCrudBoolean,
  readStandardCrudString,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';
import { createOrganizationObjectAuthorizationActions } from './organizationObjectAuthorizationActions';
import { getOrganizationInitializationEligibility, initializeOrganization } from '../services/organizationService';

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
  hasChangeSnapshot: fields => typeof fields.EID === 'string',
  apiRoute: '/api/organization/companies',
  fetcher: (input, init) => platformFetch(input, init),
  mapListItem: mapCompanyListItem,
  getRowId: (row: CompanyListRow) => row.id,
});

const authorizationActions = createOrganizationObjectAuthorizationActions('organization.company', '公司', 'Organization.Company.ObjectTitle');

// 普通公司维护复用标准 REST；显式初始化只增加独立命令，保留现有授权输入与动作生命周期。
export const companyStandardCrudResource: StandardCrudResourceRegistration<Record<string, unknown>, CompanyListRow> = {
  resourceKey: 'organization.company',
  dataAdapter: companyStandardCrudDataAdapter,
  listViewKey: 'organization.company.list',
  detailViewKey: 'organization.company.detail',
  ...authorizationActions,
  actionHandlers: {
    ...authorizationActions.actionHandlers,
    initialize: ({ featurePosition }) => initializeOrganization(featurePosition),
  },
  actionEligibilityResolvers: {
    ...authorizationActions.actionEligibilityResolvers,
    initialize: async ({ featurePosition, signal }) => {
      const allowed = await getOrganizationInitializationEligibility(featurePosition, signal);
      return { visible: allowed, available: allowed };
    },
  },
};
