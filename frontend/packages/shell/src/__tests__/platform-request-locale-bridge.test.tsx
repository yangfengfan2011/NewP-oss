// @vitest-environment jsdom

import { act, useEffect } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { LocalizationProvider, useLocalization } from '@icp-platform/core';
import { platformFetch } from '../../../core/src/http/platformFetch';
import { PlatformRequestLocaleBridge } from '../localization/PlatformRequestLocaleBridge';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

function Probe() {
  const { currentLanguage, setLanguage } = useLocalization();
  useEffect(() => {
    void platformFetch(`/api/probe/${currentLanguage}`);
  }, [currentLanguage]);
  return <button type="button" onClick={() => setLanguage('en')}>English</button>;
}

describe('PlatformRequestLocaleBridge', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    await act(async () => root?.unmount());
    container?.remove();
    vi.unstubAllGlobals();
  });

  it('uses the current language canonical locale for each request generation', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);

    await act(async () => {
      root.render(
        <LocalizationProvider
          defaultLanguage="zhs"
          supportedLanguages={[
            { code: 'zhs', displayName: '简体中文', locale: 'zh-CN' },
            { code: 'en', displayName: 'English', locale: 'en-US' },
          ]}
          catalogs={{}}
        >
          <PlatformRequestLocaleBridge>
            <Probe />
          </PlatformRequestLocaleBridge>
        </LocalizationProvider>,
      );
    });

    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    expect(requestLocale(fetchMock.mock.calls[0])).toBe('zh-CN');
    await act(async () => container.querySelector('button')?.click());
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    expect(requestLocale(fetchMock.mock.calls[1])).toBe('en-US');
  });
});

function requestLocale(call: unknown[]): string | null {
  const [input, init] = call as [RequestInfo | URL, RequestInit | undefined];
  return input instanceof Request
    ? input.headers.get('X-ICP-Locale')
    : new Headers(init?.headers).get('X-ICP-Locale');
}
