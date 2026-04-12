import { beforeEach, describe, expect, it, vi } from 'vitest';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      'Content-Type': 'application/json',
    },
  });
}

describe('metadataConfigCenterService', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('loads metadata definitions from the api namespace instead of the page route', async () => {
    const fetchMock = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(jsonResponse([]))
      .mockResolvedValueOnce(jsonResponse([]))
      .mockResolvedValueOnce(jsonResponse({
        definitions: [],
        status: '已保存未应用',
        hasUnsavedChanges: false,
        hasUnappliedChanges: true,
        gitSyncPending: true,
      }))
      .mockResolvedValueOnce(jsonResponse({
        status: '已应用',
        hasUnappliedChanges: false,
      }));

    const spy = vi.spyOn(globalThis, 'fetch').mockImplementation(fetchMock);
    const service = await import('../../../../modules/metadata-config-center/src/services/metadataConfigCenterService');

    await service.getModules();
    await service.getSettings('ConfigCenter');
    await service.saveSettings('ConfigCenter', { definitions: [] });
    await service.applySettings('ConfigCenter');

    expect(spy).toHaveBeenNthCalledWith(1, '/api/metadata-config/modules');
    expect(spy).toHaveBeenNthCalledWith(
      2,
      '/api/metadata-config/modules/ConfigCenter/settings',
    );
    expect(spy).toHaveBeenNthCalledWith(
      3,
      '/api/metadata-config/modules/ConfigCenter/settings',
      expect.objectContaining({
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
        },
      }),
    );
    expect(spy).toHaveBeenNthCalledWith(
      4,
      '/api/metadata-config/modules/ConfigCenter/settings/apply',
      expect.objectContaining({
        method: 'POST',
      }),
    );
  });
});
