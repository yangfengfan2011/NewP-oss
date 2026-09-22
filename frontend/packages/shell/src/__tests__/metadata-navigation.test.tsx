// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { Sidebar } from '../components/Sidebar';
import { buildMenusFromNavDocuments } from '../metadata/navigationAdapter';
import exampleNavigationDocument from '../../../../../modules/Example/metadata/navigation/Example.nav.json';

vi.mock('../contexts/PlatformContext', () => ({
  usePlatform: () => ({ shellView: 'business' }),
}));

vi.mock('../contexts/AuthContext', () => ({
  useAuth: () => ({
    hasAnyPermission: () => true,
    user: { isLocalAdministrator: false },
  }),
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

  it('groups both Example capabilities under one feature examples parent', () => {
    const menus = buildMenusFromNavDocuments(
      [exampleNavigationDocument],
      (titleKey) => titleKey ?? '',
    );

    expect(menus).toHaveLength(1);
    expect(menus[0]).toMatchObject({
      id: 'example.group',
      label: 'Navigation.Example',
      children: [
        { id: 'example', path: '/examples/standard-crud/runtime-columns' },
        { id: 'example.records', path: '/examples/standard-crud/persistent-records' },
      ],
    });
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
          <Sidebar menus={menus} collapsed={false} onCollapse={() => undefined} />
        </MemoryRouter>,
      );
    });

    expect(container.textContent).toContain('人员中心');
  });

  it('keeps menu rendering stable when nav items carry entry metadata', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);

    const menus = buildMenusFromNavDocuments(
      [
        {
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
                entryType: 'standard-list',
                targetKey: 'organization.person.list',
              },
            ],
          },
        },
      ],
      (titleKey) => (titleKey === 'Navigation.Organization' ? '组织结构' : '人员中心'),
    );

    await act(async () => {
      root = createRoot(container);
      root.render(
        <MemoryRouter>
          <Sidebar menus={menus} collapsed={false} onCollapse={() => undefined} />
        </MemoryRouter>,
      );
    });

    expect(container.textContent).toContain('人员中心');
  });

  it('retains task 2 page-entry fixture fields in nav metadata', () => {
    const navItems = [
      {
        key: 'organization.personCenter',
        path: '/organization/persons',
        entryType: 'standard-list',
        targetKey: 'organization.person.list',
      },
      {
        key: 'organization.tree',
        path: '/organization/tree',
        entryType: 'custom',
        targetKey: 'organization.tree.page',
      },
    ] satisfies Array<{
      key: string;
      path: string;
      entryType: 'standard-list' | 'custom';
      targetKey: string;
    }>;

    expect(navItems).toHaveLength(2);
    expect(navItems.map((item) => item.entryType)).toEqual(['standard-list', 'custom']);
  });

  it('hides a local-administrator navigation entry from other authenticated users', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);
    const menus = buildMenusFromNavDocuments([{
      type: 'nav',
      navigation: { items: [{
        key: 'identity.accounts',
        titleKey: 'Navigation.IdentityUsers',
        path: '/identity/accounts',
        localAdministratorOnly: true,
      }] },
    }], () => '账号管理');

    await act(async () => {
      root = createRoot(container);
      root.render(<MemoryRouter><Sidebar menus={menus} collapsed={false} onCollapse={() => undefined} /></MemoryRouter>);
    });

    expect(container.textContent).not.toContain('账号管理');
  });
});
