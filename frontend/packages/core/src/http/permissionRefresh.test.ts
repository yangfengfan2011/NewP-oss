import { afterEach, expect, it, vi } from 'vitest';
import { installPlatformAccessRejectionHandler, invalidatePlatformAntiforgeryToken, platformFetch } from './platformFetch';

afterEach(() => { invalidatePlatformAntiforgeryToken(); vi.unstubAllGlobals(); });

it('delivers the original rejection while current-user refresh is still pending', async () => {
  const pending = new Promise<void>(() => undefined);
  const refresh = vi.fn(() => pending);
  const uninstall = installPlatformAccessRejectionHandler(refresh);
  const fetchMock = vi.fn().mockResolvedValueOnce(new Response('{"requestToken":"token"}'))
    .mockResolvedValueOnce(new Response(null, { status: 403 }));
  vi.stubGlobal('fetch', fetchMock);
  try {
    expect((await platformFetch('/api/resource', { method: 'PUT' })).status).toBe(403);
    expect(refresh).toHaveBeenCalledOnce();
    expect(fetchMock).toHaveBeenCalledTimes(2);
  } finally { uninstall(); }
});

it('keeps notification failure from replacing a known rejected response', async () => {
  const errorLog = vi.spyOn(console, 'error').mockImplementation(() => undefined);
  const uninstall = installPlatformAccessRejectionHandler(async () => { throw new Error('notification failure'); });
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 403 })));
  try {
    expect((await platformFetch('/api/resource')).status).toBe(403);
    await Promise.resolve();
    expect(errorLog).toHaveBeenCalledWith('当前权限刷新失败，原请求拒绝结果保持不变。');
  } finally { uninstall(); errorLog.mockRestore(); }
});

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
