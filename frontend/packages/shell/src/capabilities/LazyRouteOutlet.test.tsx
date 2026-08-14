// @vitest-environment jsdom

import { act } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createRoot, type Root } from 'react-dom/client';
import { LazyRouteOutlet } from './LazyRouteOutlet';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

describe('LazyRouteOutlet', () => {
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

  it('renders a route-local loading state before the page module resolves', async () => {
    function LoadedPage() {
      return <div>lazy shell page</div>;
    }
    let completeLoad!: (value: { default: typeof LoadedPage }) => void;
    const load = vi.fn(() => new Promise<{ default: typeof LoadedPage }>((resolve) => {
      completeLoad = resolve;
    }));

    await act(async () => {
      root = createRoot(container);
      root.render(<LazyRouteOutlet load={load} />);
    });
    expect(container.querySelector('[role="status"]')?.textContent).toContain('正在加载页面');

    await act(async () => {
      completeLoad({ default: LoadedPage });
      await Promise.resolve();
    });
    expect(container.textContent).toContain('lazy shell page');
    expect(load).toHaveBeenCalledTimes(1);
  });

  it('keeps a chunk failure local and retries only that Shell route', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    function RecoveredPage() {
      return <div>recovered shell page</div>;
    }
    const load = vi.fn()
      .mockRejectedValueOnce(new Error('showcase chunk 404'))
      .mockResolvedValueOnce({ default: RecoveredPage });

    await act(async () => {
      root = createRoot(container);
      root.render(<LazyRouteOutlet load={load} />);
      await Promise.resolve();
    });
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('ROUTE_CAPABILITY_LOAD_FAILED');

    await act(async () => {
      container.querySelector('button')?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await Promise.resolve();
    });
    expect(container.textContent).toContain('recovered shell page');
    expect(load).toHaveBeenCalledTimes(2);
  });
});
