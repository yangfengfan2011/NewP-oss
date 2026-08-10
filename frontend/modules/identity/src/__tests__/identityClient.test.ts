import { beforeEach, describe, expect, it, vi } from 'vitest';

const platformFetchMock = vi.hoisted(() => vi.fn());
vi.mock('@icp-platform/core', () => ({ platformFetch: platformFetchMock }));

import { listUsers, resetCredential } from '../api/identityClient';
import { closeOneTimeSecret, revealOneTimeSecret } from '../state/oneTimeSecret';
import { availableIdentityActions } from '../state/accountActions';

describe('identity management client', () => {
  beforeEach(() => platformFetchMock.mockReset());

  it('uses platformFetch and sends only confirmed server-side filters', async () => {
    platformFetchMock.mockResolvedValue(new Response(JSON.stringify({ items: [], page: 2, pageSize: 20, totalCount: 0 }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }));

    await listUsers({ query: 'alice', status: 'Disabled', isPersonnelBound: false, page: 2, pageSize: 20 });

    expect(platformFetchMock).toHaveBeenCalledWith(
      '/api/identity/users?query=alice&status=Disabled&isPersonnelBound=false&page=2&pageSize=20',
      expect.objectContaining({ method: 'GET' }),
    );
  });

  it('never retries a credential reset when the server reports a revision conflict', async () => {
    platformFetchMock.mockResolvedValue(new Response(JSON.stringify({
      code: 'IDENTITY_REVISION_CONFLICT',
      current: { eid: 'User_1', ecode: 'alice', ename: 'Alice', status: 'Enabled', revision: 8 },
    }), {
      status: 409,
      headers: { 'Content-Type': 'application/problem+json' },
    }));

    await expect(resetCredential('User_1', { expectedRevision: 7, reason: 'rotation' }))
      .rejects.toMatchObject({ code: 'IDENTITY_REVISION_CONFLICT', current: { revision: 8 } });
    expect(platformFetchMock).toHaveBeenCalledTimes(1);
  });
});

describe('one-time secret state', () => {
  it('destroys the secret when the result closes', () => {
    const visible = revealOneTimeSecret('temporary-password');
    expect(visible.value).toBe('temporary-password');
    expect(closeOneTimeSecret(visible)).toEqual({ value: undefined });
  });
});

describe('account lifecycle actions', () => {
  const account = {
    eid: 'User_1', ecode: 'alice', ename: 'Alice', status: 'Enabled' as const, revision: 1,
    isLocalAdministrator: false, isPersonnelBound: false, personnelDisableHold: false,
  };

  it('strictly contracts protected and terminal account actions', () => {
    expect(availableIdentityActions(account)).toEqual(['edit', 'disable', 'reset', 'delete']);
    expect(availableIdentityActions({ ...account, isLocalAdministrator: true })).toEqual(['edit']);
    expect(availableIdentityActions({ ...account, isPersonnelBound: true })).not.toContain('delete');
    expect(availableIdentityActions({ ...account, status: 'Deleted' })).toEqual([]);
    expect(availableIdentityActions({ ...account, status: 'Disabled', personnelDisableHold: true })).toEqual(['edit', 'reset', 'delete']);
  });
});
