import { beforeEach, describe, expect, it, vi } from 'vitest';
import { IdentityApiError } from '../api/identityClient';
import { identityAccountStandardCrudResource } from '../standard-crud/identityAccountStandardCrudResource';

const createAccountMock = vi.hoisted(() => vi.fn());
const getAccountSnapshotMock = vi.hoisted(() => vi.fn());
const updateAccountNameMock = vi.hoisted(() => vi.fn());

vi.mock('../api/identityClient', async (importOriginal) => ({
  ...await importOriginal<typeof import('../api/identityClient')>(),
  createAccount: createAccountMock,
  getAccountSnapshot: getAccountSnapshotMock,
  updateAccountName: updateAccountNameMock,
}));

describe('Identity account Standard CRUD resource candidate', () => {
  beforeEach(() => {
    createAccountMock.mockReset();
    getAccountSnapshotMock.mockReset();
    updateAccountNameMock.mockReset();
  });

  it('maps the existing Identity list response into metadata protocol fields', () => {
    const row = identityAccountStandardCrudResource.mapListItem({
      eid: 'User_2',
      ecode: 'alice',
      ename: 'Alice',
      status: 'Disabled',
      revision: 7,
      isLocalAdministrator: false,
      isPersonnelBound: true,
      personnelDisableHold: false,
    });

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
    expect(identityAccountStandardCrudResource.getRowId(row)).toBe('User_2');
  });

  it('keeps the existing Identity API filter names and exposes Deleted as an explicit option', () => {
    expect(identityAccountStandardCrudResource.createEmptyFilters()).toEqual({
      query: '',
      status: '',
      isPersonnelBound: '',
      isLocalAdministrator: '',
    });
    expect(identityAccountStandardCrudResource.buildListQuery({
      query: 'ali',
      status: 'Deleted',
      isPersonnelBound: 'true',
      isLocalAdministrator: 'false',
    })).toEqual({
      query: 'ali',
      status: 'Deleted',
      isPersonnelBound: 'true',
      isLocalAdministrator: 'false',
    });
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

    const result = await identityAccountStandardCrudResource.operationHandlers?.create?.({
      values: { ECODE: ' alice ', ENAME: ' Alice ' },
      detailFields: {},
    });

    expect(createAccountMock).toHaveBeenCalledTimes(1);
    expect(createAccountMock).toHaveBeenCalledWith({ ecode: 'alice', ename: 'Alice' });
    expect(result).toEqual({ operation: 'create', payload: issuance });
  });

  it('updates only ENAME with the latest loaded ExpectedRevision', async () => {
    const account = { eid: 'User_2', ecode: 'alice', ename: 'Alice 2', revision: 8 };
    updateAccountNameMock.mockResolvedValue(account);

    const result = await identityAccountStandardCrudResource.operationHandlers?.update?.({
      id: 'User_2',
      values: { ECODE: 'must-not-be-sent', ENAME: ' Alice 2 ' },
      detailFields: { Revision: 7 },
    });

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

    const detail = await identityAccountStandardCrudResource.loadDetail?.('User_2');

    expect(getAccountSnapshotMock).toHaveBeenCalledWith('User_2');
    expect(detail?.fields).toMatchObject({
      EID: 'User_2', ECODE: 'alice', ENAME: 'Alice', Status: 'Enabled', Revision: 7,
    });
    expect(detail?.summaries).toEqual({
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

    await expect(identityAccountStandardCrudResource.operationHandlers?.create?.({
      values: { ECODE: 'alice', ENAME: 'Alice' },
      detailFields: {},
    })).rejects.toMatchObject({
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

    await expect(identityAccountStandardCrudResource.operationHandlers?.create?.({
      values: { ECODE: '', ENAME: 'Alice' },
      detailFields: {},
    })).rejects.toMatchObject({
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

    await expect(identityAccountStandardCrudResource.operationHandlers?.update?.({
      id: 'User_2',
      values: { ENAME: 'Draft Alice' },
      detailFields: { Revision: 7 },
    })).rejects.toMatchObject({ status: 409 });
    expect(updateAccountNameMock).toHaveBeenCalledTimes(1);
  });

  it('does not retry when account creation has an unknown network result', async () => {
    createAccountMock.mockRejectedValue(new TypeError('Failed to fetch'));

    await expect(identityAccountStandardCrudResource.operationHandlers?.create?.({
      values: { ECODE: 'alice', ENAME: 'Alice' },
      detailFields: {},
    })).rejects.toThrow('创建结果未知');
    expect(createAccountMock).toHaveBeenCalledTimes(1);
  });
});
