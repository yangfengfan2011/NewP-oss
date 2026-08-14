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

  it('shows a stable local conflict when duplicate custom page keys are registered', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    ModuleRegistry.replaceAll([
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
    ], 'snapshot-duplicate-custom-page');

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(renderPageEntry({
        path: '/x',
        entryType: 'custom',
        targetKey: 'same.page',
      }));
      await Promise.resolve();
    });

    expect(container.querySelector('[data-route-capability-error]')?.getAttribute(
      'data-route-capability-error',
    )).toBe('ROUTE_CAPABILITY_CONFLICT');
    expect(container.textContent).toContain('ROUTE_CAPABILITY_CONFLICT');
  });
});
