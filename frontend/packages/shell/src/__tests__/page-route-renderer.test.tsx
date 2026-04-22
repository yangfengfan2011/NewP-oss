// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { ModuleRegistry } from '@icp-platform/sdk';
import { renderPageEntry } from '../navigation/pageRouteRenderer';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

describe('pageRouteRenderer', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    vi.restoreAllMocks();

    if (root) {
      await act(async () => {
        root.unmount();
      });
    }

    if (container) {
      container.remove();
    }
  });

  it('shows a missing custom page registration error when duplicate page keys are removed from the registry', async () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: {
          moduleId: 'module-a',
          displayName: 'Module A',
          version: '1.0.0',
        },
        customPages: [
          { pageKey: 'same.page', component: () => null },
        ],
      },
      {
        metadata: {
          moduleId: 'module-b',
          displayName: 'Module B',
          version: '1.0.0',
        },
        customPages: [
          { pageKey: 'same.page', component: () => null },
        ],
      },
    ]);

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(renderPageEntry({
        path: '/x',
        entryType: 'custom',
        targetKey: 'same.page',
      }));
    });

    expect(errorSpy).toHaveBeenCalledTimes(1);
    expect(String(errorSpy.mock.calls[0]?.[0])).toContain('same.page');
    expect(container.textContent).toContain('未找到自定义页面注册项');
  });
});
