import { afterEach, expect, it, vi } from 'vitest';
import { installPlatformAccessRejectionHandler, invalidatePlatformAntiforgeryToken, platformFetch } from './platformFetch';

afterEach(() => { invalidatePlatformAntiforgeryToken(); vi.unstubAllGlobals(); });

it('refreshes the current subject after explicit rejection without replaying the write', async () => {
  const refresh = vi.fn().mockResolvedValue(undefined);
  const uninstall = installPlatformAccessRejectionHandler(refresh);
  const fetchMock = vi.fn().mockResolvedValueOnce(new Response('{"requestToken":"token"}'))
    .mockResolvedValueOnce(new Response('{"code":"AUTH_FORBIDDEN"}', { status: 403 }));
  vi.stubGlobal('fetch', fetchMock);
  try {
    expect((await platformFetch('/api/resource/1', { method: 'PUT' })).status).toBe(403);
    expect(refresh).toHaveBeenCalledOnce();
    expect(fetchMock).toHaveBeenCalledTimes(2);
  } finally { uninstall(); }
});

it('does not interpret a dependency outage as revocation', async () => {
  const refresh = vi.fn().mockResolvedValue(undefined);
  const uninstall = installPlatformAccessRejectionHandler(refresh);
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 503 })));
  try {
    expect((await platformFetch('/api/resource')).status).toBe(503);
    expect(refresh).not.toHaveBeenCalled();
  } finally { uninstall(); }
});

it('tells the user to inspect an uncertain write without replaying it', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(new Response('{"requestToken":"token"}'))
    .mockRejectedValueOnce(new TypeError('response lost'));
  vi.stubGlobal('fetch', fetchMock);
  await expect(platformFetch('/api/resource/1', { method: 'PUT' })).rejects.toThrow('请先查询实际结果，再决定是否重试');
  expect(fetchMock).toHaveBeenCalledTimes(2);
});
