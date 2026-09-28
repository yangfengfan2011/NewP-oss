import { afterEach, describe, expect, it, vi } from 'vitest';
import { StandardCrudRequestError } from '@icp-platform/sdk';
import {
  installPlatformRequestLocaleProvider,
  invalidatePlatformAntiforgeryToken,
  platformFetch,
} from './platformFetch';

const tokenResponse = (requestToken: string) => new Response(
  JSON.stringify({ requestToken }),
  {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  },
);

const antiforgeryFailure = () => new Response(
  JSON.stringify({ code: 'ANTIFORGERY_TOKEN_INVALID', traceId: 'trace-1' }),
  {
    status: 400,
    headers: { 'Content-Type': 'application/problem+json' },
  },
);

describe('platformFetch', () => {
  afterEach(() => {
    invalidatePlatformAntiforgeryToken();
    vi.unstubAllGlobals();
  });

  it('passes ordinary reads through without obtaining or attaching a token', async () => {
    const response = new Response(JSON.stringify({ value: 1 }), { status: 200 });
    const fetchMock = vi.fn().mockResolvedValue(response);
    vi.stubGlobal('fetch', fetchMock);

    await expect(platformFetch('/api/test?value=1')).resolves.toBe(response);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledWith('/api/test?value=1', {
      credentials: 'include',
    });
  });

  it('attaches the controlled request locale to ordinary reads', async () => {
    const response = new Response(JSON.stringify({ value: 1 }), { status: 200 });
    const fetchMock = vi.fn().mockResolvedValue(response);
    vi.stubGlobal('fetch', fetchMock);
    const uninstall = installPlatformRequestLocaleProvider(() => 'en-US');

    await platformFetch('/api/test', {
      headers: { 'X-ICP-Locale': 'fr-FR' },
    });

    const request = fetchMock.mock.calls[0][0] as Request;
    expect(request.headers.get('X-ICP-Locale')).toBe('en-US');
    uninstall();
  });

  it('obtains and attaches the Host antiforgery token to cookie writes', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(tokenResponse('token-1'))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);

    const response = await platformFetch('/api/test', { method: 'POST' });

    expect(response.status).toBe(204);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    const writeRequest = fetchMock.mock.calls[1][0] as Request;
    expect(writeRequest.headers.get('X-CSRF-TOKEN')).toBe('token-1');
    expect(writeRequest.credentials).toBe('include');
  });

  it('refreshes and retries exactly once only for the stable pre-business antiforgery failure', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(tokenResponse('token-1'))
      .mockResolvedValueOnce(antiforgeryFailure())
      .mockResolvedValueOnce(tokenResponse('token-2'))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);

    const response = await platformFetch('/api/test', {
      method: 'POST',
      body: JSON.stringify({ value: 1 }),
    });

    expect(response.status).toBe(204);
    expect(fetchMock).toHaveBeenCalledTimes(4);
    const firstWrite = fetchMock.mock.calls[1][0] as Request;
    const retryWrite = fetchMock.mock.calls[3][0] as Request;
    expect(firstWrite.headers.get('X-CSRF-TOKEN')).toBe('token-1');
    expect(retryWrite.headers.get('X-CSRF-TOKEN')).toBe('token-2');
  });

  it('never replays a write when the result is unknown after a network failure', async () => {
    const cause = new TypeError('network unavailable');
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(tokenResponse('token-1'))
      .mockRejectedValueOnce(cause);
    vi.stubGlobal('fetch', fetchMock);

    const error = await platformFetch('/api/test', { method: 'DELETE' }).catch(value => value);
    expect(error).toBeInstanceOf(StandardCrudRequestError);
    expect(error).toMatchObject({ status: 0, code: 'Platform.MutationOutcomeUnknown', reasonKey: 'Platform.MutationOutcomeUnknown', cause });
    expect(error.message).toContain('请先查询实际结果，再决定是否重试');
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('preserves a read network failure without labelling it an unknown write outcome', async () => {
    const cause = new TypeError('read unavailable');
    const fetchMock = vi.fn().mockRejectedValue(cause);
    vi.stubGlobal('fetch', fetchMock);
    await expect(platformFetch('/api/test')).rejects.toBe(cause);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('preserves an antiforgery network failure before the business write is sent', async () => {
    const cause = new TypeError('token unavailable');
    const fetchMock = vi.fn().mockRejectedValue(cause);
    vi.stubGlobal('fetch', fetchMock);
    await expect(platformFetch('/api/test', { method: 'POST' })).rejects.toBe(cause);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toBe('/api/auth/antiforgery-token');
  });

  it('does not mark a rejected antiforgery token request as an unknown business outcome', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 503 }));
    vi.stubGlobal('fetch', fetchMock);
    const error = await platformFetch('/api/test', { method: 'POST' }).catch(value => value);
    expect(error).not.toBeInstanceOf(StandardCrudRequestError);
    expect(error.message).toBe('Unable to obtain antiforgery token (503).');
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('does not retry other HTTP failures', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(tokenResponse('token-1'))
      .mockResolvedValueOnce(new Response(null, { status: 503 }));
    vi.stubGlobal('fetch', fetchMock);

    const response = await platformFetch('/api/test', { method: 'PATCH' });

    expect(response.status).toBe(503);
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('does not treat an unrelated 400 problem code as permission to replay', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(tokenResponse('token-1'))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        code: 'IDENTITY_REVISION_CONFLICT',
        traceId: 'trace-2',
      }), {
        status: 400,
        headers: { 'Content-Type': 'application/problem+json' },
      }));
    vi.stubGlobal('fetch', fetchMock);

    const response = await platformFetch('/api/test', { method: 'PUT' });

    expect(response.status).toBe(400);
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('never performs a third write when the single refreshed retry is also rejected', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(tokenResponse('token-1'))
      .mockResolvedValueOnce(antiforgeryFailure())
      .mockResolvedValueOnce(tokenResponse('token-2'))
      .mockResolvedValueOnce(antiforgeryFailure());
    vi.stubGlobal('fetch', fetchMock);

    const response = await platformFetch('/api/test', { method: 'POST' });

    expect(response.status).toBe(400);
    expect(fetchMock).toHaveBeenCalledTimes(4);
  });

  it('drops the cached token when the authentication subject changes', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(tokenResponse('anonymous-token'))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(tokenResponse('authenticated-token'))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);

    await platformFetch('/api/auth/login', { method: 'POST' });
    invalidatePlatformAntiforgeryToken();
    await platformFetch('/api/test', { method: 'POST' });

    expect(fetchMock).toHaveBeenCalledTimes(4);
    expect((fetchMock.mock.calls[3][0] as Request).headers.get('X-CSRF-TOKEN'))
      .toBe('authenticated-token');
  });

  it('does not restore a stale token when an earlier token request completes after invalidation', async () => {
    let resolveFirstToken!: (response: Response) => void;
    const firstToken = new Promise<Response>((resolve) => {
      resolveFirstToken = resolve;
    });
    const fetchMock = vi.fn()
      .mockReturnValueOnce(firstToken)
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(tokenResponse('new-subject-token'))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);

    const earlierWrite = platformFetch('/api/test', { method: 'POST' });
    await Promise.resolve();
    invalidatePlatformAntiforgeryToken();
    resolveFirstToken(tokenResponse('stale-subject-token'));
    await earlierWrite;
    await platformFetch('/api/test', { method: 'POST' });

    expect(fetchMock).toHaveBeenCalledTimes(4);
    expect((fetchMock.mock.calls[3][0] as Request).headers.get('X-CSRF-TOKEN'))
      .toBe('new-subject-token');
  });
});
