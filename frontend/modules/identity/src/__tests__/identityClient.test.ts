import { beforeEach, describe, expect, it, vi } from 'vitest';

const platformFetchMock = vi.hoisted(() => vi.fn());
vi.mock('@icp-platform/core', () => ({ platformFetch: platformFetchMock }));

import {
  getAccountSnapshot,
  listAccountSecurityEvents,
  resetCredential,
} from '../api/identityClient';

describe('identity management client', () => {
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
