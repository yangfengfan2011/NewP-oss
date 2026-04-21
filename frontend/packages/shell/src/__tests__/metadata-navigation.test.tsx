// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { Sidebar } from '../components/Sidebar';
import { buildMenusFromNavDocuments } from '../metadata/navigationAdapter';

vi.mock('../contexts/PlatformContext', () => ({
  usePlatform: () => ({ shellView: 'business' }),
}));

vi.mock('../contexts/AuthContext', () => ({
  useAuth: () => ({ hasAnyPermission: () => true }),
}));

describe('metadata navigation', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    if (root) {
      await act(async () => root.unmount());
    }

    if (container) {
      container.remove();
    }
  });

  it('renders person center from nav metadata', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);

    const menus = buildMenusFromNavDocuments(
      [{
        type: 'nav',
        navigation: {
          items: [
            { key: 'organization', titleKey: 'Navigation.Organization', order: 100 },
            {
              key: 'organization.personCenter',
              titleKey: 'Navigation.PersonCenter',
              path: '/organization/persons',
              parentKey: 'organization',
              order: 110,
            },
          ],
        },
      }],
      (titleKey) => (titleKey === 'Navigation.Organization' ? '组织结构' : '人员中心'),
    );

    await act(async () => {
      root = createRoot(container);
      root.render(
        <MemoryRouter>
          <Sidebar menus={menus} />
        </MemoryRouter>,
      );
    });

    expect(container.textContent).toContain('人员中心');
  });
});

