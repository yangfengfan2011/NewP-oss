import { beforeEach, describe, expect, it, vi } from 'vitest';

const platformFetchMock = vi.hoisted(() => vi.fn());
vi.mock('@icp-platform/core', () => ({ platformFetch: platformFetchMock }));

import {
  createAccount,
  setAccountStatus,
  getAccountSnapshot,
  listAccountSecurityEvents,
  resetCredential,
} from '../api/identityClient';

describe('identity management client', () => {
  it('carries the initiating feature on reads and security writes with distinct status routes', async () => {
    platformFetchMock.mockImplementation(async () => new Response('{}', { status: 200 }));
    const context = { featurePosition: 'Identity/navigation:accounts/action:create' };
    await createAccount({ ecode: 'alice', ename: 'Alice' }, context);
    await getAccountSnapshot('u1', context);
    await listAccountSecurityEvents('u1', 1, 20, context);
    await setAccountStatus('u1', { targetStatus: 'Disabled', expectedRevision: 1, reason: '停用' }, context);
    for (const call of platformFetchMock.mock.calls) expect(call[1].headers).toMatchObject({ 'X-NewP-Feature': context.featurePosition });
    expect(platformFetchMock.mock.calls[3][0]).toBe('/api/identity/accounts/u1/status/disable');
  });
  beforeEach(() => platformFetchMock.mockReset());

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

  it('loads the account snapshot separately from paged security events', async () => {
    platformFetchMock
      .mockResolvedValueOnce(new Response(JSON.stringify({ eid: 'User_1', ecode: 'alice' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ items: [], page: 2, pageSize: 20, totalCount: 21 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }));

    await getAccountSnapshot('User/1');
    await listAccountSecurityEvents('User/1', 2, 20);

    expect(platformFetchMock).toHaveBeenNthCalledWith(
      1,
      '/api/identity/accounts/User%2F1/snapshot',
      { method: 'GET' },
    );
    expect(platformFetchMock).toHaveBeenNthCalledWith(
      2,
      '/api/identity/accounts/User%2F1/security-events?page=2&pageSize=20',
      { method: 'GET' },
    );
  });

  it('forwards the account snapshot signal to the final GET', async () => {
    platformFetchMock.mockResolvedValue(new Response(JSON.stringify({ eid: 'User_1', ecode: 'alice' }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }));
    const abortController = new AbortController();

    await getAccountSnapshot('User/1', { signal: abortController.signal });

    expect(platformFetchMock).toHaveBeenCalledWith(
      '/api/identity/accounts/User%2F1/snapshot',
      { method: 'GET', signal: abortController.signal },
    );
  });

  it('forwards the security-event query signal to the final GET', async () => {
    platformFetchMock.mockResolvedValue(new Response(JSON.stringify({
      items: [], page: 2, pageSize: 20, totalCount: 0,
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }));
    const abortController = new AbortController();

    await listAccountSecurityEvents('User/1', 2, 20, { signal: abortController.signal });

    expect(platformFetchMock).toHaveBeenCalledWith(
      '/api/identity/accounts/User%2F1/security-events?page=2&pageSize=20',
      { method: 'GET', signal: abortController.signal },
    );
  });
});
