import { shellTestCatalogs } from './localizationTestCatalogs';
// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { LanguageSelect, LocalizationProvider } from '@icp-platform/core';
import Home from '../pages/Home';

const localizationProps = {
  defaultLanguage: 'zhs',
  supportedLanguages: [{ code: 'zhs', displayName: '简体中文' }],
  catalogs: shellTestCatalogs,
};

vi.mock('../contexts/PlatformContext', () => ({
  usePlatform: () => ({
    shellView: 'business',
    uiConfig: {
      shellExperienceMode: 'hybrid',
    },
  }),
}));

describe('Home shortcuts', () => {
  let container: HTMLDivElement;
  let root: Root;

  it('renders public home labels and shortcuts in English from authoring', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);
    await act(async () => {
      root = createRoot(container);
      root.render(<LocalizationProvider {...localizationProps} supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }, { code: 'en', displayName: 'English' }]}>
        <MemoryRouter><LanguageSelect /><Home /></MemoryRouter>
      </LocalizationProvider>);
    });
    await act(async () => {
      container.querySelector('select')!.value = 'en';
      container.querySelector('select')!.dispatchEvent(new Event('change', { bubbles: true }));
    });
    expect(container.textContent).toContain('Business portal');
    expect(container.textContent).toContain('Value lists');
    expect(container.textContent).toContain('2 portal entries available');
    expect(container.textContent).not.toContain('PlatformShell.');
  });

  afterEach(async () => {
    if (root) {
      await act(async () => {
        root.unmount();
      });
    }

    if (container) {
      container.remove();
    }
  });

  it('shows value list management in platform shortcuts', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(
        <LocalizationProvider {...localizationProps}>
          <MemoryRouter>
            <Home />
          </MemoryRouter>
        </LocalizationProvider>,
      );
    });

    expect(container.textContent).toContain('值列表管理');
    const valueListLink = Array.from(container.querySelectorAll('a')).find(
      (link) => link.textContent?.includes('值列表管理'),
    );
    expect(valueListLink?.getAttribute('href')).toBe('/config/value-lists');
  });

  it('shows metadata config center in platform shortcuts', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(
        <LocalizationProvider {...localizationProps}>
          <MemoryRouter>
            <Home />
          </MemoryRouter>
        </LocalizationProvider>,
      );
    });

    expect(container.textContent).toContain('元数据配置中心');
    const metadataLink = Array.from(container.querySelectorAll('a')).find(
      (link) => link.textContent?.includes('元数据配置中心'),
    );
    expect(metadataLink?.getAttribute('href')).toBe('/metadata-config/modules');
  });
});
