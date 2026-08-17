// @vitest-environment jsdom

import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { LocalizationProvider } from '@icp-platform/core';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import identityModule from '../../../../modules/identity/src/index';
import accountListDictionary from '../../../../../modules/Identity/metadata/dictionaries/AccountListView.dic.json';
import accountModelDocument from '../../../../../modules/Identity/metadata/models/IdentityAccount.model.json';
import identityNavigationDocument from '../../../../../modules/Identity/metadata/navigation/Identity.nav.json';
import accountListViewDocument from '../../../../../modules/Identity/metadata/views/AccountList.view.json';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import StandardCrudListPageHost from '../pages/StandardCrudListPageHost';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const accountListCatalog = Object.fromEntries(
  Object.entries(accountListDictionary.AccountListView).map(([key, value]) => [
    `AccountListView.${key}`,
    value.zhs,
  ]),
);

const runtimeMetadata = {
  manifest: { modules: [] },
  navDocuments: [identityNavigationDocument],
  viewDocuments: [accountListViewDocument],
  modelDocuments: [accountModelDocument],
};

async function flushRequests(): Promise<void> {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

describe('Identity account Standard CRUD list candidate', () => {
  let container: HTMLDivElement;
  let root: Root;
  let registration: StandardCrudResourceRegistration;

  beforeAll(async () => {
    const capability = identityModule.standardCrudCapabilities?.find(
      (item) => item.kind === 'standard-list' && item.targetKey === 'identity.account.list',
    );
    if (!capability) {
      throw new Error('未找到账号标准列表候选 capability。');
    }
    registration = (await capability.load()).default;
  });

  beforeEach(() => {
    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      value: vi.fn().mockImplementation((query: string) => ({
        matches: true,
        media: query,
        onchange: null,
        addListener: vi.fn(),
        removeListener: vi.fn(),
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        dispatchEvent: vi.fn(),
      })),
    });
  });

  afterEach(async () => {
    vi.restoreAllMocks();
    if (root) {
      await act(async () => root.unmount());
    }
    container?.remove();
  });

  it('parses, queries and renders the candidate through the shared list host', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [{
        eid: 'User_2',
        ecode: 'alice',
        ename: 'Alice',
        status: 'Deleted',
        revision: 7,
        isLocalAdministrator: false,
        isPersonnelBound: true,
        personnelDisableHold: false,
      }],
      total: 21,
      totalCount: 21,
      page: 2,
      pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);

    await act(async () => {
      root.render(
        <MemoryRouter initialEntries={['/identity/accounts?status=Deleted&page=2&pageSize=20']}>
          <LocalizationProvider
            defaultLanguage="zhs"
            supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
            catalogs={{ zhs: accountListCatalog }}
          >
            <RuntimeMetadataProvider value={runtimeMetadata}>
              <StandardCrudListPageHost viewKey="identity.account.list" registration={registration} />
            </RuntimeMetadataProvider>
          </LocalizationProvider>
        </MemoryRouter>,
      );
    });
    await flushRequests();

    expect(fetch).toHaveBeenCalledWith(
      '/api/identity/accounts?status=Deleted&fields=EID%2CECODE%2CENAME%2CStatus%2CIsPersonnelBound%2CIsLocalAdministrator%2CPersonnelDisableHold&pageIndex=2&pageSize=20&sortBy=ECODE&sortDirection=asc',
      { method: 'GET' },
    );
    expect(container.querySelector('.ant-pro-table')).not.toBeNull();
    expect(container.textContent).toContain('账号管理');
    expect(container.textContent).toContain('alice');
    expect(container.textContent).toContain('Alice');
    expect(container.textContent).toContain('已删除');
    expect(container.textContent).toContain('共 21 条');
  });
});
