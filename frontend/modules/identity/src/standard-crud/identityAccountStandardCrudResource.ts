import {
  isStandardCrudRecord,
  readStandardCrudBoolean,
  readStandardCrudString,
  StandardCrudRequestError,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';
import {
  createAccount,
  getAccount,
  IdentityApiError,
  updateAccountName,
  type IdentityAccount,
} from '../api/identityClient';
import { identityAccountOperationResultPresenter } from './identityAccountOperationResultPresenter';

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

function mapIdentityAccountDetail(account: IdentityAccount) {
  return {
    fields: {
      EID: account.eid,
      ECODE: account.ecode,
      ENAME: account.ename,
      Status: account.status,
      Revision: account.revision,
      IsLocalAdministrator: account.isLocalAdministrator,
      IsPersonnelBound: account.isPersonnelBound,
      PersonnelDisableHold: account.personnelDisableHold,
      CreatedBy: account.createdBy ?? null,
      CreatedAt: account.createdAt ?? null,
      UpdatedBy: account.updatedBy ?? null,
      UpdatedAt: account.updatedAt ?? null,
    },
    summaries: {},
  };
}

function readMutationString(values: Record<string, unknown>, field: string): string {
  const value = values[field];
  return typeof value === 'string' ? value.trim() : '';
}

function toSharedRequestError(error: IdentityApiError): StandardCrudRequestError {
  const fieldErrors = Object.fromEntries(Object.entries(error.errors ?? {}).flatMap(([field, messages]) => (
    messages[0] ? [[field, messages[0]]] : []
  )));
  const message = error.status === 403 ? '权限已变化，当前操作已被拒绝' : error.code;
  return new StandardCrudRequestError(message, error.status, fieldErrors);
}

// 创建结果可能已在服务端生效；网络结果未知时只提示人工核对，绝不自动重试签发。
async function createIdentityAccount(values: Record<string, unknown>) {
  try {
    const issuance = await createAccount({
      ecode: readMutationString(values, 'ECODE'),
      ename: readMutationString(values, 'ENAME'),
    });
    return { operation: 'create' as const, payload: issuance };
  } catch (error) {
    if (error instanceof IdentityApiError) {
      throw toSharedRequestError(error);
    }
    throw new Error('创建结果未知。系统不会自动重试，请先刷新账号列表并核对结果。');
  }
}

async function updateIdentityAccount(
  id: string | undefined,
  values: Record<string, unknown>,
  detailFields: Record<string, unknown>,
) {
  const expectedRevision = detailFields.Revision;
  if (!id || typeof expectedRevision !== 'number' || !Number.isSafeInteger(expectedRevision) || expectedRevision < 0) {
    throw new Error('账号最新 Revision 不可用，请重新加载后再编辑。');
  }

  try {
    const account = await updateAccountName(id, {
      ename: readMutationString(values, 'ENAME'),
      expectedRevision,
    });
    return { operation: 'update' as const, payload: account };
  } catch (error) {
    if (error instanceof IdentityApiError) {
      throw toSharedRequestError(error);
    }
    throw error instanceof Error ? error : new Error('保存账号失败');
  }
}

// 该候选注册标准列表、普通表单与正式 Identity 写入；生产导航切换仍由后续迁移票拥有。
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
  operationHandlers: {
    create: ({ values }) => createIdentityAccount(values),
    update: ({ id, values, detailFields }) => updateIdentityAccount(id, values, detailFields),
  },
  operationResultPresenters: [identityAccountOperationResultPresenter],
  createEmptyFilters,
  buildListQuery,
  mapListItem: mapIdentityAccountListItem,
  getRowId: (row) => row.id,
  loadDetail: async (id) => mapIdentityAccountDetail((await getAccount(id)).account),
};
