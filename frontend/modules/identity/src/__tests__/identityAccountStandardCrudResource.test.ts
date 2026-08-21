import { beforeEach, describe, expect, it, vi } from 'vitest';
import { IdentityApiError } from '../api/identityClient';
import { identityAccountStandardCrudResource } from '../standard-crud/identityAccountStandardCrudResource';

const createAccountMock = vi.hoisted(() => vi.fn());
const getAccountSnapshotMock = vi.hoisted(() => vi.fn());
const updateAccountNameMock = vi.hoisted(() => vi.fn());
const setAccountStatusMock = vi.hoisted(() => vi.fn());
const resetCredentialMock = vi.hoisted(() => vi.fn());
const deleteAccountMock = vi.hoisted(() => vi.fn());
const platformFetchMock = vi.hoisted(() => vi.fn());

vi.mock('@icp-platform/core', async (importOriginal) => ({
  ...await importOriginal<typeof import('@icp-platform/core')>(),
  platformFetch: platformFetchMock,
}));

vi.mock('../api/identityClient', async (importOriginal) => ({
  ...await importOriginal<typeof import('../api/identityClient')>(),
  createAccount: createAccountMock,
  getAccountSnapshot: getAccountSnapshotMock,
  updateAccountName: updateAccountNameMock,
  setAccountStatus: setAccountStatusMock,
  resetCredential: resetCredentialMock,
  deleteAccount: deleteAccountMock,
}));

function actionRow(overrides: Record<string, unknown> = {}) {
  return {
    id: 'User_2', EID: 'User_2', ECODE: 'alice', ENAME: 'Alice', Status: 'Enabled', Revision: 7,
    IsLocalAdministrator: false, IsPersonnelBound: false, PersonnelDisableHold: false,
    ...overrides,
  };
}

describe('Identity account Standard CRUD resource candidate', () => {
  beforeEach(() => {
    createAccountMock.mockReset();
    getAccountSnapshotMock.mockReset();
    updateAccountNameMock.mockReset();
    setAccountStatusMock.mockReset();
    resetCredentialMock.mockReset();
    deleteAccountMock.mockReset();
    platformFetchMock.mockReset();
  });

  it('maps the existing Identity list response into metadata protocol fields', async () => {
    platformFetchMock.mockResolvedValue(new Response(JSON.stringify({
      items: [{
        eid: 'User_2',
        ecode: 'alice',
        ename: 'Alice',
        status: 'Disabled',
        revision: 7,
        isLocalAdministrator: false,
        isPersonnelBound: true,
        personnelDisableHold: false,
      }],
      total: 1,
      page: 1,
      pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    const result = await identityAccountStandardCrudResource.dataAdapter.list();
    const row = result.items[0];

    expect(row).toMatchObject({
      id: 'User_2',
      EID: 'User_2',
      ECODE: 'alice',
      ENAME: 'Alice',
      Status: 'Disabled',
      Revision: 7,
      IsLocalAdministrator: false,
      IsPersonnelBound: true,
      PersonnelDisableHold: false,
    });
    expect(identityAccountStandardCrudResource.dataAdapter.getRowId(row)).toBe('User_2');
  });

  it('keeps filters in metadata and exposes Deleted as an explicit option', () => {
    expect(identityAccountStandardCrudResource).not.toHaveProperty('createEmptyFilters');
    expect(identityAccountStandardCrudResource).not.toHaveProperty('buildListQuery');
    expect(identityAccountStandardCrudResource).not.toHaveProperty('apiRoute');
    expect(identityAccountStandardCrudResource.enumOptions?.IdentityAccountStatus).toEqual([
      { value: 'Enabled', label: '启用' },
      { value: 'Disabled', label: '停用' },
      { value: 'Deleted', label: '已删除' },
    ]);
  });

  it('creates through the formal Identity API and returns an opaque one-time result', async () => {
    const issuance = {
      account: { eid: 'User_2', ecode: 'alice', ename: 'Alice', revision: 1 },
      temporaryCredential: 'only-once',
    };
    createAccountMock.mockResolvedValue(issuance);

    const result = await identityAccountStandardCrudResource.dataAdapter.create?.(
      { ECODE: ' alice ', ENAME: ' Alice ' },
      {},
    );

    expect(createAccountMock).toHaveBeenCalledTimes(1);
    expect(createAccountMock).toHaveBeenCalledWith({ ecode: 'alice', ename: 'Alice' });
    expect(result).toEqual({ operation: 'create', payload: issuance });
  });

  it('updates only ENAME with the latest loaded ExpectedRevision', async () => {
    const account = { eid: 'User_2', ecode: 'alice', ename: 'Alice 2', revision: 8 };
    updateAccountNameMock.mockResolvedValue(account);

    const result = await identityAccountStandardCrudResource.dataAdapter.update?.(
      'User_2',
      { ECODE: 'must-not-be-sent', ENAME: ' Alice 2 ' },
      { Revision: 7 },
    );

    expect(updateAccountNameMock).toHaveBeenCalledTimes(1);
    expect(updateAccountNameMock).toHaveBeenCalledWith('User_2', { ename: 'Alice 2', expectedRevision: 7 });
    expect(result).toEqual({ operation: 'update', payload: account });
  });

  it('loads only the account snapshot and publishes a lightweight security-event summary marker', async () => {
    getAccountSnapshotMock.mockResolvedValue({
      eid: 'User_2', ecode: 'alice', ename: 'Alice', status: 'Enabled', revision: 7,
      isLocalAdministrator: false, isPersonnelBound: false, personnelDisableHold: false,
      createdBy: 'User_1', createdAt: '2026-08-01T00:00:00Z',
      updatedBy: 'User_1', updatedAt: '2026-08-02T00:00:00Z',
    });

    const detail = await identityAccountStandardCrudResource.dataAdapter.get('User_2');

    expect(getAccountSnapshotMock).toHaveBeenCalledWith('User_2');
    expect(detail.normalized.fields).toMatchObject({
      EID: 'User_2', ECODE: 'alice', ENAME: 'Alice', Status: 'Enabled', Revision: 7,
    });
    expect(detail.normalized.summaries).toEqual({
      securityEvents: { status: 'ready', data: { accountEid: 'User_2' } },
    });
    expect(identityAccountStandardCrudResource.detailViewKey).toBe('identity.account.detail');
    expect(Object.keys(identityAccountStandardCrudResource.summaryRenderers ?? {})).toEqual(['securityEvents']);
  });

  it('maps Identity field and permission failures into the shared form error contract', async () => {
    createAccountMock.mockRejectedValue(new IdentityApiError(
      'IDENTITY_ACCOUNT_FORBIDDEN',
      403,
      undefined,
      { ECODE: ['账号不可用'] },
    ));

    await expect(identityAccountStandardCrudResource.dataAdapter.create?.(
      { ECODE: 'alice', ENAME: 'Alice' },
      {},
    )).rejects.toMatchObject({
      status: 403,
      fieldErrors: { ECODE: '账号不可用' },
    });
    expect(createAccountMock).toHaveBeenCalledTimes(1);
  });

  it('maps Identity validation failures to the matching shared form field', async () => {
    createAccountMock.mockRejectedValue(new IdentityApiError(
      'IDENTITY_ACCOUNT_CODE_REQUIRED',
      400,
      undefined,
      { ECODE: ['请填写登录账号'] },
    ));

    await expect(identityAccountStandardCrudResource.dataAdapter.create?.(
      { ECODE: '', ENAME: 'Alice' },
      {},
    )).rejects.toMatchObject({
      status: 400,
      fieldErrors: { ECODE: '请填写登录账号' },
    });
  });

  it('returns a revision conflict once and never replays the Identity update', async () => {
    updateAccountNameMock.mockRejectedValue(new IdentityApiError(
      'IDENTITY_REVISION_CONFLICT',
      409,
      { eid: 'User_2', ecode: 'alice', ename: 'Server Alice', status: 'Enabled', revision: 8,
        isLocalAdministrator: false, isPersonnelBound: false, personnelDisableHold: false },
    ));

    await expect(identityAccountStandardCrudResource.dataAdapter.update?.(
      'User_2',
      { ENAME: 'Draft Alice' },
      { Revision: 7 },
    )).rejects.toMatchObject({ status: 409 });
    expect(updateAccountNameMock).toHaveBeenCalledTimes(1);
  });

  it('does not retry when account creation has an unknown network result', async () => {
    createAccountMock.mockRejectedValue(new TypeError('Failed to fetch'));

    await expect(identityAccountStandardCrudResource.dataAdapter.create?.(
      { ECODE: 'alice', ENAME: 'Alice' },
      {},
    )).rejects.toThrow('创建结果未知');
    expect(createAccountMock).toHaveBeenCalledTimes(1);
  });

  it('resolves account security actions from the current snapshot inside the Identity binding', () => {
    const resolve = identityAccountStandardCrudResource.actionStateResolver;
    const enabled = actionRow();
    const held = { ...enabled, Status: 'Disabled', PersonnelDisableHold: true };
    const deleted = { ...enabled, Status: 'Deleted' };

    expect(resolve?.({ action: { key: 'disable', labelKey: 'disable' }, row: enabled })).toEqual({
      visible: true, available: true,
    });
    expect(resolve?.({ action: { key: 'enable', labelKey: 'enable' }, row: enabled })).toEqual({
      visible: false, available: false,
    });
    expect(resolve?.({ action: { key: 'enable', labelKey: 'enable' }, row: held })).toMatchObject({
      visible: true, available: false, reason: expect.stringContaining('人员停用阻断'),
    });
    expect(resolve?.({ action: { key: 'delete', labelKey: 'delete' }, row: { ...enabled, IsPersonnelBound: true } })).toMatchObject({
      visible: true, available: false, reason: expect.stringContaining('已绑定人员'),
    });
    expect(resolve?.({ action: { key: 'credentialReset', labelKey: 'reset' }, row: { ...enabled, IsLocalAdministrator: true } })).toMatchObject({
      visible: true, available: false, reason: expect.stringContaining('本地管理员'),
    });
    for (const key of ['disable', 'delete']) {
      expect(resolve?.({ action: { key, labelKey: key }, row: { ...enabled, IsLocalAdministrator: true } })).toMatchObject({
        visible: true, available: false, reason: expect.stringContaining('本地管理员'),
      });
    }
    for (const key of ['edit', 'enable', 'disable', 'delete', 'credentialReset']) {
      expect(resolve?.({ action: { key, labelKey: key }, row: deleted })).toEqual({ visible: false, available: false });
    }
  });

  it.each([
    ['enable', 'Enabled', 'Disabled'],
    ['disable', 'Disabled', 'Enabled'],
  ] as const)('executes %s once with ExpectedRevision and the required trimmed reason', async (actionKey, targetStatus, currentStatus) => {
    setAccountStatusMock.mockResolvedValue({ eid: 'User_2', status: targetStatus, revision: 8 });

    await identityAccountStandardCrudResource.actionHandlers?.[actionKey]?.({
      id: 'User_2',
      row: actionRow({ Status: currentStatus }),
      input: { reason: '  风险处置  ' },
    });

    expect(setAccountStatusMock).toHaveBeenCalledTimes(1);
    expect(setAccountStatusMock).toHaveBeenCalledWith('User_2', {
      targetStatus, expectedRevision: 7, reason: '风险处置',
    });
  });

  it('requires the full current ECODE before calling the irreversible delete command', async () => {
    const context = {
      id: 'User_2',
      row: actionRow({ Status: 'Disabled' }),
    };

    await expect(identityAccountStandardCrudResource.actionHandlers?.delete?.({
      ...context,
      input: { reason: '账号退役', confirmation: 'Alice' },
    })).rejects.toThrow('完整输入登录账号 alice');
    expect(deleteAccountMock).not.toHaveBeenCalled();

    deleteAccountMock.mockResolvedValue({ eid: 'User_2', status: 'Deleted', revision: 8 });
    await identityAccountStandardCrudResource.actionHandlers?.delete?.({
      ...context,
      input: { reason: '账号退役', confirmation: 'alice' },
    });
    expect(deleteAccountMock).toHaveBeenCalledWith('User_2', { expectedRevision: 7, reason: '账号退役' });
  });

  it('hands only the current credential reset issuance to the existing result seam', async () => {
    const issuance = {
      account: { eid: 'User_2', ecode: 'alice', status: 'Enabled', revision: 8 },
      temporaryCredential: 'reset-once',
    };
    resetCredentialMock.mockResolvedValue(issuance);

    const result = await identityAccountStandardCrudResource.actionHandlers?.credentialReset?.({
      id: 'User_2',
      detail: { fields: { EID: 'User_2', ECODE: 'alice', Status: 'Enabled', Revision: 7 }, summaries: {} },
      input: { reason: '凭据泄露' },
    });

    expect(resetCredentialMock).toHaveBeenCalledWith('User_2', { expectedRevision: 7, reason: '凭据泄露' });
    expect(result).toEqual({ operation: 'action', payload: issuance });
  });

  it.each([
    [new IdentityApiError('IDENTITY_ACCOUNT_FORBIDDEN', 403), '权限已变化'],
    [new IdentityApiError('IDENTITY_PERSONNEL_DISABLE_HOLD', 409), '人员停用阻断'],
  ])('maps permission and business guard failures without replaying the command', async (failure, message) => {
    setAccountStatusMock.mockRejectedValue(failure);

    await expect(identityAccountStandardCrudResource.actionHandlers?.enable?.({
      id: 'User_2',
      row: actionRow({ Status: 'Disabled' }),
      input: { reason: '恢复账号' },
    })).rejects.toThrow(message);
    expect(setAccountStatusMock).toHaveBeenCalledTimes(1);
  });

  it('shows the latest security state on 409 and never automatically replays the command', async () => {
    setAccountStatusMock.mockRejectedValue(new IdentityApiError(
      'IDENTITY_REVISION_CONFLICT',
      409,
      { eid: 'User_2', ecode: 'alice', ename: 'Alice', status: 'Disabled', revision: 8,
        isLocalAdministrator: false, isPersonnelBound: true, personnelDisableHold: true },
    ));

    await expect(identityAccountStandardCrudResource.actionHandlers?.disable?.({
      id: 'User_2',
      row: actionRow(),
      input: { reason: '风险处置' },
    })).rejects.toThrow('最新状态 Disabled，Revision 8');
    expect(setAccountStatusMock).toHaveBeenCalledTimes(1);
  });

  it('reports an unknown result on a network failure and never retries a security command', async () => {
    deleteAccountMock.mockRejectedValue(new TypeError('Failed to fetch'));

    await expect(identityAccountStandardCrudResource.actionHandlers?.delete?.({
      id: 'User_2',
      row: actionRow({ Status: 'Disabled' }),
      input: { reason: '账号退役', confirmation: 'alice' },
    })).rejects.toThrow('操作结果未知');
    expect(deleteAccountMock).toHaveBeenCalledTimes(1);
  });
});
