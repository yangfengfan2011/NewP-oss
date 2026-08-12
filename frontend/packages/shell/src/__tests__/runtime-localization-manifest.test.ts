// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { loadRuntimeLocalizationManifest } from '../localization/runtimeLocalizationManifest';

const runtimeLocalizationManifest = {
  defaultLanguage: 'zhs',
  supportedLanguages: [
    { code: 'zhs', displayName: '简体中文' },
    { code: 'en', displayName: 'English' },
  ],
  catalogs: {
    zhs: {
      'Pages.ListTitle': '配置中心',
    },
    en: {
      'Pages.ListTitle': 'Config Center',
    },
  },
};

describe('runtime localization manifest loader', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('loads runtime localization manifest from the host route', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(runtimeLocalizationManifest), {
        status: 200,
        headers: {
          'Content-Type': 'application/json',
        },
      }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const manifest = await loadRuntimeLocalizationManifest();

    expect(fetchMock).toHaveBeenCalledWith('/plugins/localization-manifest.json', {
      credentials: 'same-origin',
    });
    expect(manifest.catalogs.zhs['Pages.ListTitle']).toBe('配置中心');
    expect(manifest.catalogs.en['Pages.ListTitle']).toBe('Config Center');
  });

  it('throws when the host route fails', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response('', {
          status: 404,
        }),
      ),
    );

    await expect(loadRuntimeLocalizationManifest()).rejects.toThrow('404');
  });

  it('shares only an in-flight manifest request', async () => {
    const fetchMock = vi.fn(async () => {
      await new Promise((resolve) => setTimeout(resolve, 1));
      return new Response(JSON.stringify(runtimeLocalizationManifest), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    });
    vi.stubGlobal('fetch', fetchMock);

    await Promise.all([
      loadRuntimeLocalizationManifest(),
      loadRuntimeLocalizationManifest(),
    ]);
    expect(fetchMock).toHaveBeenCalledTimes(1);

    await loadRuntimeLocalizationManifest();
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('clears a failed in-flight request so a later call can retry', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response('', { status: 503 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(runtimeLocalizationManifest), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }));

    await expect(
      loadRuntimeLocalizationManifest(fetchMock as typeof fetch),
    ).rejects.toThrow('503');
    const manifest = await loadRuntimeLocalizationManifest(fetchMock as typeof fetch);

    expect(manifest.defaultLanguage).toBe('zhs');
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});
