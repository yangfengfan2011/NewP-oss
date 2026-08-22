import { platformFetch } from '@icp-platform/core';
import {
  createStandardCrudRestDataAdapter,
  isStandardCrudRecord,
  readStandardCrudBoolean,
  readStandardCrudString,
  StandardCrudRequestError,
  type StandardCrudDataAdapter,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';
import {
  createAccount,
  deleteAccount,
  getAccountSnapshot,
  IdentityApiError,
  resetCredential,
  setAccountStatus,
  updateAccountName,
  type IdentityAccount,
} from '../api/identityClient';
import { identityAccountOperationResultPresenter } from './identityAccountOperationResultPresenter';
import {
  identityAccountDeleteConfirmationInput,
  identityAccountSecurityReasonInput,
} from './IdentityAccountSecurityActionInput';
import { identityAccountSecurityEventsSummaryRenderer } from './IdentityAccountSecurityEventsSummary';

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
    summaries: {
      securityEvents: {
        status: 'ready' as const,
        data: { accountEid: account.eid },
      },
    },
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

interface IdentitySecurityActionInput {
  reason?: unknown;
  confirmation?: unknown;
}

interface IdentityActionSnapshot {
  eid: string;
  ecode: string;
  status: string;
  revision: number;
  isLocalAdministrator: boolean;
  isPersonnelBound: boolean;
  personnelDisableHold: boolean;
}

function readIdentityActionSnapshot(detail: unknown, row: unknown): IdentityActionSnapshot | null {
  const detailRecord = isStandardCrudRecord(detail) && isStandardCrudRecord(detail.fields)
    ? detail.fields
    : detail;
  const source = isStandardCrudRecord(detailRecord)
    ? detailRecord
    : isStandardCrudRecord(row) ? row : null;
  if (!source) return null;

  const eid = readStandardCrudString(source, ['EID', 'eid', 'id']);
  const revision = Number(source.Revision ?? source.revision);
  if (!eid || !Number.isSafeInteger(revision) || revision < 0) return null;

  return {
    eid,
    ecode: readStandardCrudString(source, ['ECODE', 'ecode']),
    status: readStandardCrudString(source, ['Status', 'status']),
    revision,
    isLocalAdministrator: readStandardCrudBoolean(source, ['IsLocalAdministrator', 'isLocalAdministrator']),
    isPersonnelBound: readStandardCrudBoolean(source, ['IsPersonnelBound', 'isPersonnelBound']),
    personnelDisableHold: readStandardCrudBoolean(source, ['PersonnelDisableHold', 'personnelDisableHold']),
  };
}

function resolveIdentityAccountActionState({
  action,
  detail,
  row,
}: Parameters<NonNullable<StandardCrudResourceRegistration['actionStateResolver']>>[0]) {
  const snapshot = readIdentityActionSnapshot(detail, row);
  const securityAction = ['enable', 'disable', 'delete', 'credentialReset'].includes(action.key);
  if (!snapshot) {
    return securityAction
      ? { visible: true, available: false, reason: '账号最新安全状态不可用，请刷新后重试。' }
      : { visible: true, available: true };
  }

  if (snapshot.status === 'Deleted' && (securityAction || action.key === 'edit')) {
    return { visible: false, available: false };
  }
  if (action.key === 'enable') {
    if (snapshot.status !== 'Disabled') return { visible: false, available: false };
    if (snapshot.isLocalAdministrator) return { visible: true, available: false, reason: '本地管理员账号受保护。' };
    if (snapshot.personnelDisableHold) return { visible: true, available: false, reason: '存在人员停用阻断，不能启用账号。' };
  }
  if (action.key === 'disable') {
    if (snapshot.status !== 'Enabled') return { visible: false, available: false };
    if (snapshot.isLocalAdministrator) return { visible: true, available: false, reason: '本地管理员账号受保护。' };
  }
  if (action.key === 'delete') {
    if (snapshot.isLocalAdministrator) return { visible: true, available: false, reason: '本地管理员账号受保护。' };
    if (snapshot.isPersonnelBound) return { visible: true, available: false, reason: '账号已绑定人员，只能停用，不能删除。' };
  }
  if (action.key === 'credentialReset' && snapshot.isLocalAdministrator) {
    return { visible: true, available: false, reason: '本地管理员凭据只能通过本机维护命令重置。' };
  }

  return { visible: true, available: true };
}

function readIdentitySecurityInput(input: unknown): { reason: string; confirmation: string } {
  const value = isStandardCrudRecord(input) ? input as IdentitySecurityActionInput : {};
  const reason = typeof value.reason === 'string' ? value.reason.trim() : '';
  if (!reason) throw new Error('请填写操作原因。');
  return {
    reason,
    confirmation: typeof value.confirmation === 'string' ? value.confirmation : '',
  };
}

function toIdentitySecurityActionError(error: unknown): Error {
  if (!(error instanceof IdentityApiError)) {
    return new Error('操作结果未知。系统不会自动重放，请刷新账号最新安全状态后核对。');
  }
  if (error.status === 403) {
    return new StandardCrudRequestError('权限已变化，当前安全操作已被拒绝。', error.status);
  }
  if (error.code === 'IDENTITY_REVISION_CONFLICT' && error.current) {
    return new StandardCrudRequestError(
      `账号已发生变化：最新状态 ${error.current.status}，Revision ${error.current.revision}。请查看最新安全状态并重新确认。`,
      error.status,
    );
  }
  const guardMessages: Record<string, string> = {
    IDENTITY_LOCAL_ADMIN_PROTECTED: '本地管理员账号受保护，不能执行该操作。',
    IDENTITY_PERSONNEL_BOUND: '账号已绑定人员，只能停用，不能删除。',
    IDENTITY_PERSONNEL_DISABLE_HOLD: '存在人员停用阻断，不能启用账号。',
    IDENTITY_ACCOUNT_DELETED: '已删除账号处于不可恢复终态，不能执行该操作。',
  };
  return new StandardCrudRequestError(guardMessages[error.code] ?? error.code, error.status);
}

async function setIdentityAccountLifecycleStatus(
  targetStatus: 'Enabled' | 'Disabled',
  context: Parameters<NonNullable<StandardCrudResourceRegistration['actionHandlers']>[string]>[0],
) {
  const snapshot = readIdentityActionSnapshot(context.detail, context.row);
  const input = readIdentitySecurityInput(context.input);
  if (!context.id || !snapshot) throw new Error('账号最新安全状态不可用，请刷新后重试。');
  try {
    await setAccountStatus(context.id, {
      targetStatus,
      expectedRevision: snapshot.revision,
      reason: input.reason,
    });
  } catch (error) {
    throw toIdentitySecurityActionError(error);
  }
}

async function deleteIdentityAccount(
  context: Parameters<NonNullable<StandardCrudResourceRegistration['actionHandlers']>[string]>[0],
) {
  const snapshot = readIdentityActionSnapshot(context.detail, context.row);
  const input = readIdentitySecurityInput(context.input);
  if (!context.id || !snapshot) throw new Error('账号最新安全状态不可用，请刷新后重试。');
  if (input.confirmation !== snapshot.ecode) {
    throw new Error(`请完整输入登录账号 ${snapshot.ecode} 以确认不可恢复删除。`);
  }
  try {
    await deleteAccount(context.id, { expectedRevision: snapshot.revision, reason: input.reason });
  } catch (error) {
    throw toIdentitySecurityActionError(error);
  }
}

async function resetIdentityAccountCredential(
  context: Parameters<NonNullable<StandardCrudResourceRegistration['actionHandlers']>[string]>[0],
) {
  const snapshot = readIdentityActionSnapshot(context.detail, context.row);
  const input = readIdentitySecurityInput(context.input);
  if (!context.id || !snapshot) throw new Error('账号最新安全状态不可用，请刷新后重试。');
  try {
    const issuance = await resetCredential(context.id, {
      expectedRevision: snapshot.revision,
      reason: input.reason,
    });
    return { operation: 'action' as const, payload: issuance };
  } catch (error) {
    throw toIdentitySecurityActionError(error);
  }
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

const identityAccountRestListAdapter = createStandardCrudRestDataAdapter({
  apiRoute: '/api/identity/accounts',
  fetcher: (input, init) => platformFetch(input, init),
  mapListItem: mapIdentityAccountListItem,
  getRowId: (row: IdentityAccountListRow) => row.id,
});

const identityAccountDataAdapter: StandardCrudDataAdapter<IdentityAccountListRow> = {
  list: identityAccountRestListAdapter.list,
  getRowId: identityAccountRestListAdapter.getRowId,
  get: async (id, readContext) => {
    const normalized = mapIdentityAccountDetail(await getAccountSnapshot(id, readContext));
    return { normalized, value: normalized };
  },
  create: (values) => createIdentityAccount(values),
  update: (id, values, detailFields) => updateIdentityAccount(id, values, detailFields ?? {}),
};

// 该候选注册标准列表、普通表单与正式 Identity 写入；生产导航切换仍由后续迁移票拥有。
export const identityAccountStandardCrudResource: StandardCrudResourceRegistration<
  Record<string, unknown>,
  IdentityAccountListRow
> = {
  resourceKey: 'identity.account',
  dataAdapter: identityAccountDataAdapter,
  listViewKey: 'identity.account.list',
  detailViewKey: 'identity.account.detail',
  concurrencyRevisionField: 'Revision',
  enumOptions: {
    IdentityAccountStatus: [
      { value: 'Enabled', label: '启用' },
      { value: 'Disabled', label: '停用' },
      { value: 'Deleted', label: '已删除' },
    ],
  },
  operationResultPresenters: [identityAccountOperationResultPresenter],
  actionStateResolver: resolveIdentityAccountActionState,
  actionHandlers: {
    enable: (context) => setIdentityAccountLifecycleStatus('Enabled', context),
    disable: (context) => setIdentityAccountLifecycleStatus('Disabled', context),
    delete: deleteIdentityAccount,
    credentialReset: resetIdentityAccountCredential,
  },
  actionInputRenderers: {
    identityAccountSecurityReason: identityAccountSecurityReasonInput,
    identityAccountDeleteConfirmation: identityAccountDeleteConfirmationInput,
  },
  summaryRenderers: {
    securityEvents: identityAccountSecurityEventsSummaryRenderer,
  },
};
