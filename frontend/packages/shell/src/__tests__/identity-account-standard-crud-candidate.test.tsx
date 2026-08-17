// @vitest-environment jsdom

import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, Route, Routes, useNavigate, useParams } from 'react-router-dom';
import { LocalizationProvider } from '@icp-platform/core';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import identityModule from '../../../../modules/identity/src/index';
import accountDetailDictionary from '../../../../../modules/Identity/metadata/dictionaries/AccountDetailView.dic.json';
import accountListDictionary from '../../../../../modules/Identity/metadata/dictionaries/AccountListView.dic.json';
import accountModelDocument from '../../../../../modules/Identity/metadata/models/IdentityAccount.model.json';
import identityNavigationDocument from '../../../../../modules/Identity/metadata/navigation/Identity.nav.json';
import accountListViewDocument from '../../../../../modules/Identity/metadata/views/AccountList.view.json';
import accountDetailViewDocument from '../../../../../modules/Identity/metadata/views/AccountDetail.view.json';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { resolveStandardCrudDetailSchema } from '../metadata/standardCrudSchemaResolver';
import StandardCrudListPageHost from '../pages/StandardCrudListPageHost';
import StandardCrudDetailPageHost from '../pages/StandardCrudDetailPageHost';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const accountListCatalog = Object.fromEntries(
  [
    ...Object.entries(accountListDictionary.AccountListView).map(([key, value]) => [
      `AccountListView.${key}`,
      value.zhs,
    ]),
    ...Object.entries(accountDetailDictionary.AccountDetailView).map(([key, value]) => [
      `AccountDetailView.${key}`,
      value.zhs,
    ]),
  ],
);

const runtimeMetadata = {
  manifest: { modules: [] },
  navDocuments: [identityNavigationDocument],
  viewDocuments: [accountListViewDocument, accountDetailViewDocument],
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

async function waitForBodyText(expectedText: string): Promise<void> {
  for (let attempt = 0; attempt < 100; attempt += 1) {
    if (document.body.textContent?.includes(expectedText)) return;
    await act(async () => new Promise((resolve) => setTimeout(resolve, 10)));
  }
  throw new Error(`Timed out waiting for body text: ${expectedText}`);
}

function IdentityAccountDetailCandidate({ registration }: { registration: StandardCrudResourceRegistration }) {
  const { id } = useParams();
  const navigate = useNavigate();
  return (
    <>
      <button data-testid="history-back" onClick={() => navigate(-1)}>后退</button>
      <button data-testid="history-forward" onClick={() => navigate(1)}>前进</button>
      <StandardCrudDetailPageHost
        viewKey="identity.account.detail"
        id={id ?? null}
        registration={registration}
      />
    </>
  );
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
    window.localStorage.clear();
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

  it('resolves one complete account Detail View for both surfaces without actions', () => {
    const schema = resolveStandardCrudDetailSchema(
      runtimeMetadata.viewDocuments,
      registration,
      { modelDocuments: runtimeMetadata.modelDocuments },
    );

    expect(schema.viewKey).toBe('identity.account.detail');
    expect(schema.sections.map((section) => ({
      key: section.key,
      fields: section.fields.map((field) => field.key),
    }))).toEqual([
      { key: 'basic', fields: ['ECODE', 'ENAME', 'Status'] },
      { key: 'protection', fields: ['IsLocalAdministrator', 'IsPersonnelBound', 'PersonnelDisableHold'] },
      { key: 'audit', fields: ['CreatedBy', 'CreatedAt', 'UpdatedBy', 'UpdatedAt'] },
    ]);
    expect(schema.summaryBlocks).toEqual(['securityEvents']);
    expect(schema.pageActions).toEqual([]);
  });

  it('opens a Deleted account Drawer from an explicit result without loading security events or write actions', async () => {
    const account = {
      eid: 'User_2', ecode: 'retired', ename: 'Retired', status: 'Deleted', revision: 7,
      isLocalAdministrator: false, isPersonnelBound: false, personnelDisableHold: false,
      createdBy: 'User_1', createdAt: '2026-08-01T00:00:00Z',
      updatedBy: 'User_1', updatedAt: '2026-08-02T00:00:00Z',
    };
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.endsWith('/User_2/snapshot')) {
        return new Response(JSON.stringify(account), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }
      return new Response(JSON.stringify({
        items: [account], total: 1, totalCount: 1, page: 1, pageSize: 20,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);

    await act(async () => {
      root.render(
        <MemoryRouter initialEntries={['/identity/accounts?status=Deleted']}>
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
    await waitForBodyText('retired');
    const buttonTexts = Array.from(container.querySelectorAll('button')).map((button) => button.textContent);
    expect(buttonTexts).toContain('retired');
    const accountLink = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === 'retired');
    await act(async () => accountLink?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await waitForBodyText('审计信息');

    const drawer = document.body.querySelector('.ant-drawer');
    expect(drawer?.textContent).toContain('账号信息');
    expect(drawer?.textContent).toContain('安全保护');
    expect(drawer?.textContent).toContain('已删除');
    expect(Array.from(drawer?.querySelectorAll('button') ?? []).map((button) => button.textContent)).not.toContain('编辑');
    expect(fetchMock.mock.calls.some(([input]) => String(input).includes('/security-events'))).toBe(false);
  });

  it('supports candidate detail direct entry, refresh, back and forward without switching production navigation', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.includes('/security-events')) {
        return new Response(JSON.stringify({ items: [], page: 1, pageSize: 20, totalCount: 0 }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      const id = decodeURIComponent(url.split('/').at(-2) ?? '');
      return new Response(JSON.stringify({
        eid: id,
        ecode: id.toLowerCase(),
        ename: `Account ${id}`,
        status: 'Enabled',
        revision: 1,
        isLocalAdministrator: false,
        isPersonnelBound: false,
        personnelDisableHold: false,
        createdBy: 'User_1',
        createdAt: '2026-08-01T00:00:00Z',
        updatedBy: 'User_1',
        updatedAt: '2026-08-02T00:00:00Z',
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);

    const renderRouter = (entries: string[], initialIndex?: number) => (
      <LocalizationProvider
        defaultLanguage="zhs"
        supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
        catalogs={{ zhs: accountListCatalog }}
      >
        <RuntimeMetadataProvider value={runtimeMetadata}>
          <MemoryRouter initialEntries={entries} initialIndex={initialIndex}>
            <Routes>
              <Route
                path="/identity/accounts/:id"
                element={<IdentityAccountDetailCandidate registration={registration} />}
              />
            </Routes>
          </MemoryRouter>
        </RuntimeMetadataProvider>
      </LocalizationProvider>
    );
    await act(async () => root.render(renderRouter([
      '/identity/accounts/User_1',
      '/identity/accounts/User_2',
    ], 1)));
    await waitForBodyText('Account User_2');

    await act(async () => container.querySelector<HTMLButtonElement>('[data-testid="history-back"]')
      ?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await waitForBodyText('Account User_1');
    await act(async () => container.querySelector<HTMLButtonElement>('[data-testid="history-forward"]')
      ?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await waitForBodyText('Account User_2');

    await act(async () => root.unmount());
    root = createRoot(container);
    await act(async () => root.render(renderRouter(['/identity/accounts/User_2'])));
    await waitForBodyText('Account User_2');

    const snapshotUrls = fetchMock.mock.calls
      .map(([input]) => String(input))
      .filter((url) => url.endsWith('/snapshot'));
    expect(snapshotUrls).toEqual([
      '/api/identity/accounts/User_2/snapshot',
      '/api/identity/accounts/User_1/snapshot',
      '/api/identity/accounts/User_2/snapshot',
      '/api/identity/accounts/User_2/snapshot',
    ]);
    expect(identityNavigationDocument.navigation.items.find((item) => item.key === 'identity.accounts')).toMatchObject({
      entryType: 'custom',
      targetKey: 'identity.accounts.page',
    });
  });

  it('keeps an account detail API 403 local and never starts the security-event renderer', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      code: 'IDENTITY_ACCOUNT_FORBIDDEN',
    }), { status: 403, headers: { 'Content-Type': 'application/problem+json' } }));
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);

    await act(async () => {
      root.render(
        <MemoryRouter>
          <LocalizationProvider
            defaultLanguage="zhs"
            supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
            catalogs={{ zhs: accountListCatalog }}
          >
            <RuntimeMetadataProvider value={runtimeMetadata}>
              <StandardCrudDetailPageHost
                viewKey="identity.account.detail"
                id="User_2"
                registration={registration}
              />
            </RuntimeMetadataProvider>
          </LocalizationProvider>
        </MemoryRouter>,
      );
    });
    await waitForBodyText('IDENTITY_ACCOUNT_FORBIDDEN');

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/identity/accounts/User_2/snapshot');
  });

  it('derives account create and edit drawers from metadata without exposing immutable fields', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [], total: 0, totalCount: 0, page: 1, pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);

    await act(async () => {
      root.render(
        <MemoryRouter initialEntries={['/identity/accounts']}>
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

    const createButton = Array.from(container.querySelectorAll('button')).find((button) =>
      button.textContent?.replace(/\s+/g, '') === '创建账号');
    expect(createButton).not.toBeUndefined();
    await act(async () => createButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await waitForBodyText('账号信息');

    const drawer = document.body.querySelector('.ant-drawer');
    expect(drawer).not.toBeNull();
    expect(drawer?.querySelector('#ECODE')).not.toBeNull();
    expect(drawer?.querySelector('#ENAME')).not.toBeNull();
    expect(drawer?.querySelector('#Status') ?? null).toBeNull();
    expect(drawer?.querySelector('#Revision') ?? null).toBeNull();
    const saveButton = Array.from(document.body.querySelectorAll('button')).find((button) =>
      button.textContent?.trim() === '保存并关闭');
    await act(async () => saveButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await waitForBodyText('请填写此字段');
    expect(document.body.textContent?.split('请填写此字段')).toHaveLength(3);
    expect(vi.mocked(fetch).mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false);
  });

  it('creates through the Identity handler and clears the one-time result when its presenter closes', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (_input, init) => {
      if (init?.method === 'POST') {
        return new Response(JSON.stringify({
          account: { eid: 'User_2', ecode: 'alice', ename: 'Alice', status: 'Enabled', revision: 1 },
          temporaryCredential: 'only-once',
        }), { status: 201, headers: { 'Content-Type': 'application/json' } });
      }
      return new Response(JSON.stringify({
        items: [], total: 0, totalCount: 0, page: 1, pageSize: 20,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);

    await act(async () => {
      root.render(
        <MemoryRouter initialEntries={['/identity/accounts']}>
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
    const createButton = Array.from(container.querySelectorAll('button')).find((button) =>
      button.textContent?.replace(/\s+/g, '') === '创建账号');
    await act(async () => createButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await waitForBodyText('账号信息');

    const codeInput = document.body.querySelector<HTMLInputElement>('#ECODE');
    const nameInput = document.body.querySelector<HTMLInputElement>('#ENAME');
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(codeInput, ' alice ');
      codeInput?.dispatchEvent(new Event('input', { bubbles: true }));
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(nameInput, ' Alice ');
      nameInput?.dispatchEvent(new Event('input', { bubbles: true }));
    });
    const saveButton = Array.from(document.body.querySelectorAll('button')).find((button) =>
      button.textContent?.trim() === '保存并关闭');
    await act(async () => saveButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await waitForBodyText('only-once');

    const createCall = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST');
    expect(createCall?.[0]).toBe('/api/identity/accounts');
    expect(JSON.parse(String(createCall?.[1]?.body))).toEqual({ ecode: 'alice', ename: 'Alice' });
    expect(window.location.href).not.toContain('only-once');
    const persistedValues = Array.from({ length: window.localStorage.length }, (_, index) => (
      window.localStorage.getItem(window.localStorage.key(index) ?? '')
    ));
    expect(JSON.stringify(persistedValues)).not.toContain('only-once');

    const closeButton = Array.from(document.body.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('我已安全保存'));
    await act(async () => closeButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    expect(document.body.textContent).not.toContain('only-once');
  });

  it('edits only ENAME through the Identity API with the loaded ExpectedRevision', async () => {
    const account = {
      eid: 'User_2', ecode: 'alice', ename: 'Alice', status: 'Enabled', revision: 7,
      isLocalAdministrator: false, isPersonnelBound: false, personnelDisableHold: false,
    };
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input);
      if (init?.method === 'PUT') {
        return new Response(JSON.stringify({ ...account, ename: 'Alice 2', revision: 8 }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      if (url.endsWith('/User_2/snapshot')) {
        return new Response(JSON.stringify(account), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }
      return new Response(JSON.stringify({
        items: [account], total: 1, totalCount: 1, page: 1, pageSize: 20,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);

    await act(async () => {
      root.render(
        <MemoryRouter initialEntries={['/identity/accounts']}>
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
    const editButton = Array.from(container.querySelectorAll('button')).find((button) => button.textContent?.trim() === '编辑');
    await act(async () => editButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await waitForBodyText('账号信息');

    expect(document.body.querySelector<HTMLInputElement>('#ECODE')).toBeNull();
    const nameInput = document.body.querySelector<HTMLInputElement>('#ENAME');
    expect(nameInput?.value).toBe('Alice');
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(nameInput, 'Alice 2');
      nameInput?.dispatchEvent(new Event('input', { bubbles: true }));
    });
    const saveButton = Array.from(document.body.querySelectorAll('button')).find((button) =>
      button.textContent?.trim() === '保存修改');
    await act(async () => saveButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushRequests();

    const updateCall = fetchMock.mock.calls.find(([, init]) => init?.method === 'PUT');
    expect(updateCall?.[0]).toBe('/api/identity/accounts/User_2');
    expect(JSON.parse(String(updateCall?.[1]?.body))).toEqual({ ename: 'Alice 2', expectedRevision: 7 });
    expect(document.body.querySelector('.ant-drawer')).toBeNull();
  });
});
