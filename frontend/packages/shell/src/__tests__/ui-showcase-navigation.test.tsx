import { TestLocalization } from './TestLocalization';
// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { Sidebar } from '../components/Sidebar';

vi.mock('../contexts/PlatformContext', () => ({
  usePlatform: () => ({
    shellView: 'business',
  }),
}));

vi.mock('../contexts/AuthContext', () => ({
  useAuth: () => ({
    hasAnyPermission: () => true,
  }),
}));

describe('production navigation inventory', () => {
  let container: HTMLDivElement;
  let root: Root;

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

  it('does not inject the development-only UI showcase into metadata navigation', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(<TestLocalization>{<MemoryRouter>
          <Sidebar menus={[]} collapsed={false} onCollapse={() => undefined} />
        </MemoryRouter>}</TestLocalization>);
    });

    expect(container.textContent).not.toContain('控件展示');
    const link = Array.from(container.querySelectorAll('a')).find((item) =>
      item.textContent?.includes('控件展示'),
    );
    expect(link).toBeUndefined();
  });
});
