// @vitest-environment jsdom

import { act } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createRoot, type Root } from 'react-dom/client';
import { ModuleRegistry } from '@icp-platform/sdk';
import { RouteCapabilityOutlet } from './RouteCapabilityOutlet';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

describe('RouteCapabilityOutlet', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
  });

  afterEach(async () => {
    if (root) {
      await act(async () => {
        root.unmount();
      });
    }
    container.remove();
    vi.restoreAllMocks();
  });

  it('shows local loading state and then renders the resolved capability', async () => {
    function LoadedPage() {
      return <div>loaded capability</div>;
    }

    let completeLoad!: (value: { default: typeof LoadedPage }) => void;
    const load = vi.fn(() => new Promise<{ default: typeof LoadedPage }>((resolve) => {
      completeLoad = resolve;
    }));
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'outlet', displayName: 'Outlet', version: '1.0.0' },
      routeCapabilities: [{ kind: 'custom', targetKey: 'outlet.page', load }],
    }], 'snapshot-outlet-loading');

    await act(async () => {
      root = createRoot(container);
      root.render(<RouteCapabilityOutlet capability={{ kind: 'custom', targetKey: 'outlet.page' }} />);
    });

    expect(container.querySelector('[role="status"]')?.textContent).toContain('正在加载页面');

    await act(async () => {
      completeLoad({ default: LoadedPage });
      await Promise.resolve();
    });

    expect(container.textContent).toContain('loaded capability');
    expect(load).toHaveBeenCalledTimes(1);
  });

  it('keeps a load failure local and retries only the current capability', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    function RecoveredPage() {
      return <div>recovered capability</div>;
    }

    const load = vi.fn()
      .mockRejectedValueOnce(new Error('chunk 404'))
      .mockResolvedValueOnce({ default: RecoveredPage });
    const module = {
      metadata: { moduleId: 'retry-outlet', displayName: 'Retry Outlet', version: '1.0.0' },
      routeCapabilities: [{ kind: 'custom' as const, targetKey: 'retry-outlet.page', load }],
    };
    ModuleRegistry.replaceAll([module], 'snapshot-outlet-retry');

    await act(async () => {
      root = createRoot(container);
      root.render(
        <RouteCapabilityOutlet capability={{ kind: 'custom', targetKey: 'retry-outlet.page' }} />,
      );
      await Promise.resolve();
    });

    expect(container.querySelector('[role="alert"]')?.textContent)
      .toContain('ROUTE_CAPABILITY_LOAD_FAILED');
    expect(ModuleRegistry.getModule('retry-outlet')).toBe(module);

    await act(async () => {
      container.querySelector('button')?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await Promise.resolve();
    });

    expect(container.textContent).toContain('recovered capability');
    expect(load).toHaveBeenCalledTimes(2);
    expect(ModuleRegistry.getModule('retry-outlet')).toBe(module);
  });

  it('contains a capability render error behind a stable route-local code', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    function BrokenRenderPage(): never {
      throw new Error('render failed');
    }
    const module = {
      metadata: { moduleId: 'render-outlet', displayName: 'Render Outlet', version: '1.0.0' },
      routeCapabilities: [{
        kind: 'custom' as const,
        targetKey: 'render-outlet.page',
        load: vi.fn().mockResolvedValue({ default: BrokenRenderPage }),
      }],
    };
    ModuleRegistry.replaceAll([module], 'snapshot-outlet-render-error');

    await act(async () => {
      root = createRoot(container);
      root.render(
        <RouteCapabilityOutlet capability={{ kind: 'custom', targetKey: 'render-outlet.page' }} />,
      );
      await Promise.resolve();
    });

    expect(container.querySelector('[data-route-capability-error]')?.getAttribute(
      'data-route-capability-error',
    )).toBe('ROUTE_CAPABILITY_RENDER_FAILED');
    expect(ModuleRegistry.getModule('render-outlet')).toBe(module);
  });
});
