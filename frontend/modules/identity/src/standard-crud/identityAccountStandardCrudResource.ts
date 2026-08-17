import {
  isStandardCrudRecord,
  readStandardCrudBoolean,
  readStandardCrudString,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';

export interface IdentityAccountListFilters {
  [key: string]: string;
  query: string;
  status: string;
  isPersonnelBound: string;
  isLocalAdministrator: string;
}

export interface IdentityAccountListRow {
  [key: string]: unknown;
  id: string;
  EID: string;
  ECODE: string;
  ENAME: string;
  Status: string;
  Revision: number;
  IsLocalAdministrator: boolean;
  IsPersonnelBound: boolean;
  PersonnelDisableHold: boolean;
}

function createEmptyFilters(): IdentityAccountListFilters {
  return {
    query: '',
    status: '',
    isPersonnelBound: '',
    isLocalAdministrator: '',
  };
}

function buildListQuery(filters: IdentityAccountListFilters): Record<string, string | null> {
  return {
    query: filters.query,
    status: filters.status,
    isPersonnelBound: filters.isPersonnelBound,
    isLocalAdministrator: filters.isLocalAdministrator,
  };
}

function mapIdentityAccountListItem(value: unknown): IdentityAccountListRow {
  const row = isStandardCrudRecord(value) ? value : {};
  const eid = readStandardCrudString(row, ['EID', 'eid']);

  // mapper 显式恢复 metadata 协议字段，Shell 无需了解 Identity API 的 JSON camelCase 约定。
  return {
    id: eid,
    EID: eid,
    ECODE: readStandardCrudString(row, ['ECODE', 'ecode']),
    ENAME: readStandardCrudString(row, ['ENAME', 'ename']),
    Status: readStandardCrudString(row, ['Status', 'status']),
    Revision: Number(row.Revision ?? row.revision ?? 0),
    IsLocalAdministrator: readStandardCrudBoolean(row, ['IsLocalAdministrator', 'isLocalAdministrator']),
    IsPersonnelBound: readStandardCrudBoolean(row, ['IsPersonnelBound', 'isPersonnelBound']),
    PersonnelDisableHold: readStandardCrudBoolean(row, ['PersonnelDisableHold', 'personnelDisableHold']),
  };
}

// 该候选只注册账号列表 mapper 与枚举；生产导航仍由 identity.accounts.page 自定义 capability 拥有。
export const identityAccountStandardCrudResource: StandardCrudResourceRegistration<
  IdentityAccountListFilters,
  IdentityAccountListRow
> = {
  resourceKey: 'identity.account',
  apiRoute: '/api/identity/accounts',
  listViewKey: 'identity.account.list',
  concurrencyRevisionField: 'Revision',
  enumOptions: {
    IdentityAccountStatus: [
      { value: 'Enabled', label: '启用' },
      { value: 'Disabled', label: '停用' },
      { value: 'Deleted', label: '已删除' },
    ],
  },
  createEmptyFilters,
  buildListQuery,
  mapListItem: mapIdentityAccountListItem,
  getRowId: (row) => row.id,
};
