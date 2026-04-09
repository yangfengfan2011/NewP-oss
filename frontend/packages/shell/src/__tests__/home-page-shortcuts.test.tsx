// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import Home from '../pages/Home';

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
        <MemoryRouter>
          <Home />
        </MemoryRouter>,
      );
    });

    expect(container.textContent).toContain('值列表管理');
    const valueListLink = Array.from(container.querySelectorAll('a')).find(
      (link) => link.textContent?.includes('值列表管理'),
    );
    expect(valueListLink?.getAttribute('href')).toBe('/config/value-lists');
  });
});
