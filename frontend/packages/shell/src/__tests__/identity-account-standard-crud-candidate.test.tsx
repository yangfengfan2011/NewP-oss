import identityMessages from '../../../../modules/platform/identity/metadata/dictionaries/IdentityMessages.dic.json';
import identityErrors from '../../../../modules/platform/identity/metadata/dictionaries/IdentityErrors.dic.json';
import identityStatus from '../../../../modules/platform/identity/metadata/dictionaries/IdentityAccountStatus.dic.json';
import { platformTestCatalogs } from './localizationTestCatalogs';
import './authorizedFeatureUiFixture';
// @vitest-environment jsdom

import { act, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, Route, Routes, useNavigate, useParams } from 'react-router-dom';
import { LocalizationProvider, PlatformQueryProvider, useLocalization } from '@icp-platform/core';
import { ModuleRegistry, type StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import identityModule from '../../../../modules/platform/identity/src/index';
import accountDetailDictionary from '../../../../modules/platform/identity/metadata/dictionaries/AccountDetailView.dic.json';
import accountListDictionary from '../../../../modules/platform/identity/metadata/dictionaries/AccountListView.dic.json';
import identityRuntimeModels from '../../../../../tests/Kernel.Tests/Fixtures/IdentityRuntimeModels.contract.json';
import identityNavigationDocument from '../../../../modules/platform/identity/metadata/navigation/Identity.nav.json';
import accountListViewDocument from '../../../../modules/platform/identity/metadata/views/AccountList.view.json';
import accountDetailViewDocument from '../../../../modules/platform/identity/metadata/views/AccountDetail.view.json';
import { formalDataAccessFor } from './formalRuntimeMetadataTestSupport';
import { RuntimeMetadataProvider } from './formalRuntimeMetadataProvider';
import { resolveStandardCrudDetailSchema } from '../metadata/standardCrudSchemaResolver';
import { compileStandardCrudRouteProjection } from '../navigation/standardCrudRouteAdapter';
import StandardCrudListPageHost from '../pages/StandardCrudListPageHost';
import StandardCrudDetailPageHost from '../pages/StandardCrudDetailPageHost';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const accountCatalogs = Object.fromEntries(['zhs', 'en'].map(language => [language, {
  ...platformTestCatalogs[language],
  ...Object.fromEntries([accountListDictionary, accountDetailDictionary, identityMessages, identityErrors, identityStatus].flatMap(document =>
    Object.entries(document).filter(([key]) => !['schemaVersion', 'type'].includes(key)).flatMap(([root, entries]) =>
      Object.entries(entries).map(([key, value]) => [`${root}.${key}`, (value as Record<string, string>)[language]])))),
}]));
const accountListCatalog = accountCatalogs.zhs;
function SwitchLanguage() {
  const { setLanguage } = useLocalization();
  return <><button onClick={() => setLanguage('en')}>English test</button><button onClick={() => setLanguage('zhs')}>Chinese test</button></>;
}

function LocaleQueries({ children }: { children: ReactNode }) {
  const { currentLanguage } = useLocalization();
  return <PlatformQueryProvider scopeKey={`user-1:${currentLanguage}`}>{children}</PlatformQueryProvider>;
}

const runtimeMetadata = {
  manifest: { modules: [] },
  navDocuments: [identityNavigationDocument],
  viewDocuments: [accountListViewDocument, accountDetailViewDocument],
  modelDocuments: [identityRuntimeModels.identityAccount],
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

describe('Identity account Standard CRUD production route', () => {
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
    ModuleRegistry.replaceAll([{ metadata: { moduleId: 'identity', displayName: 'identity', version: '1.0.0' }, standardCrudCapabilities: identityModule.standardCrudCapabilities }], 'identity-detail-test');
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
    ModuleRegistry.replaceAll([], 'detail-test-cleanup');
    window.localStorage.clear();
    if (root) {
      await act(async () => root.unmount());
    }
    container?.remove();
  });

  it('projects the production list target and same-resource detail route atomically', () => {
    const load = vi.fn();
    const module = {
      metadata: { moduleId: 'candidate', displayName: 'Candidate', version: '1.0.0' },
      standardCrudCapabilities: [
        { kind: 'standard-list' as const, targetKey: 'identity.account.list', load },
        { kind: 'standard-crud-detail' as const, targetKey: 'identity.account.detail', load },
      ],
    };

    expect(compileStandardCrudRouteProjection(
      [module],
      runtimeMetadata.navDocuments,
      runtimeMetadata.viewDocuments,
      [],
    )).toEqual({
      detailEntries: [{ path: '/identity/accounts/:id', viewKey: 'identity.account.detail' }],
      drawerRedirects: [{ from: '/identity/accounts/new', to: '/identity/accounts?drawer=create' }],
      fallbackRoutes: [],
    });
    expect(load).not.toHaveBeenCalled();

    expect(() => compileStandardCrudRouteProjection(
      [{ ...module, standardCrudCapabilities: module.standardCrudCapabilities.slice(0, 1) }],
      runtimeMetadata.navDocuments,
      runtimeMetadata.viewDocuments,
      [],
    )).toThrow('STANDARD_CRUD_DETAIL_CAPABILITY_ORPHAN');
    expect(load).not.toHaveBeenCalled();
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
            catalogs={{ zhs: { ...platformTestCatalogs.zhs, ...accountListCatalog } }}
          >
            <RuntimeMetadataProvider value={runtimeMetadata}>
              <PlatformQueryProvider scopeKey="user-1">
                <StandardCrudListPageHost viewKey="identity.account.list" registration={registration} />
              </PlatformQueryProvider>
            </RuntimeMetadataProvider>
          </LocalizationProvider>
        </MemoryRouter>,
      );
    });
    await flushRequests();

    expect(fetch).toHaveBeenCalledWith(
      '/api/identity/accounts?status=Deleted&fields=EID%2CECODE%2CENAME%2CStatus%2CIsPersonnelBound%2CIsLocalAdministrator%2CPersonnelDisableHold&pageIndex=2&pageSize=20&sortBy=ECODE&sortDirection=asc',
      { method: 'GET', headers: { 'X-NewP-Data-Declaration': formalDataAccessFor('identity.account.list', 'read').id }, signal: expect.any(AbortSignal) },
    );
    expect(container.querySelector('.ant-pro-table')).not.toBeNull();
    expect(container.querySelector('.np-crud-workspace-header')).toBeNull();
    expect(container.textContent).not.toContain('账号管理');
    expect(container.querySelector('.np-standard-crud-table')?.textContent).toContain('创建账号');
    expect(container.textContent).toContain('alice');
    expect(container.textContent).toContain('Alice');
    expect(container.textContent).toContain('已删除');
    expect(container.textContent).toContain('共 21 条');
    expect(container.querySelectorAll('button[aria-label="刷新"]')).toHaveLength(1);
    expect(accountListViewDocument.view.pageActions.map((action) => action.standardAction)).toEqual(['create']);
  });

  it('resolves one complete account Detail View with module-owned security action bindings', () => {
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
    expect(schema.pageActions.map((action) => ({
      key: action.key,
      handlerKey: action.handlerKey,
      inputKey: action.inputKey,
    }))).toEqual([
      { key: 'edit', handlerKey: undefined, inputKey: undefined },
      { key: 'enable', handlerKey: 'enable', inputKey: 'identityAccountSecurityReason' },
      { key: 'disable', handlerKey: 'disable', inputKey: 'identityAccountSecurityReason' },
      { key: 'credentialReset', handlerKey: 'credentialReset', inputKey: 'identityAccountSecurityReason' },
      { key: 'delete', handlerKey: 'delete', inputKey: 'identityAccountDeleteConfirmation' },
    ]);
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
            catalogs={{ zhs: { ...platformTestCatalogs.zhs, ...accountListCatalog } }}
          >
            <RuntimeMetadataProvider value={runtimeMetadata}>
              <PlatformQueryProvider scopeKey="user-1">
                <StandardCrudListPageHost viewKey="identity.account.list" registration={registration} />
              </PlatformQueryProvider>
            </RuntimeMetadataProvider>
          </LocalizationProvider>
        </MemoryRouter>,
      );
    });
    await waitForBodyText('retired');
    const moreActions = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === '更多');
    await act(async () => moreActions?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    expect(document.body.textContent).not.toContain('启用账号');
    expect(document.body.textContent).not.toContain('停用账号');
    expect(document.body.textContent).not.toContain('重置凭据');
    expect(document.body.textContent).not.toContain('删除账号');
    const buttonTexts = Array.from(container.querySelectorAll('button')).map((button) => button.textContent);
    expect(buttonTexts).toContain('retired');
    const accountLink = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === 'retired');
    await act(async () => accountLink?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    let drawer: Element | null = null;
    await vi.waitFor(() => {
      drawer = document.body.querySelector('.ant-drawer-open');
      expect(drawer?.textContent).toContain('审计信息');
    });

    expect(fetchMock.mock.calls.filter(([input]) => String(input).endsWith('/User_2/snapshot'))).toHaveLength(1);
    expect(drawer?.textContent).toContain('账号信息');
    expect(drawer?.textContent).toContain('安全保护');
    expect(drawer?.textContent).toContain('已删除');
    const drawerButtons = Array.from(drawer?.querySelectorAll('button') ?? []);
    const drawerButtonLabels = drawerButtons.map((button) => button.textContent?.replace(/\s+/g, '') ?? '');
    expect(drawerButtonLabels).not.toContain('编辑');
    expect(drawerButtonLabels).toContain('查看完整详情');
    expect(fetchMock.mock.calls.some(([input]) => String(input).includes('/security-events'))).toBe(false);
  });

  it('supports production detail direct entry, refresh, back and forward', async () => {
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
        catalogs={{ zhs: { ...platformTestCatalogs.zhs, ...accountListCatalog } }}
      >
        <PlatformQueryProvider scopeKey="user-1">
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
        </PlatformQueryProvider>
      </LocalizationProvider>
    );
    await act(async () => root.render(renderRouter([
      '/identity/accounts/User_1',
      '/identity/accounts/User_2',
    ], 1)));
    await waitForBodyText('Account User_2');
    const expectSingleSnapshot = (id: string) => {
      expect(container.textContent).toContain(`Account ${id}`);
      expect(fetchMock.mock.calls.map(([input]) => String(input)).filter(url => url.endsWith('/snapshot')))
        .toEqual([`/api/identity/accounts/${id}/snapshot`]);
      fetchMock.mockClear();
    };
    expectSingleSnapshot('User_2');

    // 离开详情后的查询只保留50ms孤儿宽限期，不保证历史导航永久命中缓存。
    // 明确推进既有回收时钟，再逐阶段核对唯一活跃读取，避免全包负载决定总次数。
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    try {
      await act(async () => container.querySelector<HTMLButtonElement>('[data-testid="history-back"]')
        ?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
      await act(async () => vi.advanceTimersByTimeAsync(50));
    } finally {
      vi.useRealTimers();
    }
    await waitForBodyText('Account User_1');
    expectSingleSnapshot('User_1');
    await act(async () => container.querySelector<HTMLButtonElement>('[data-testid="history-forward"]')
      ?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await waitForBodyText('Account User_2');
    expectSingleSnapshot('User_2');

    await act(async () => root.unmount());
    root = createRoot(container);
    await act(async () => root.render(renderRouter(['/identity/accounts/User_2'])));
    await waitForBodyText('Account User_2');
    expectSingleSnapshot('User_2');
    expect(identityNavigationDocument.navigation.items.find((item) => item.key === 'identity.accounts')).toMatchObject({
      entryType: 'standard-list',
      targetKey: 'identity.account.list',
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
            catalogs={{ zhs: { ...platformTestCatalogs.zhs, ...accountListCatalog } }}
          >
            <PlatformQueryProvider scopeKey="user-1">
              <RuntimeMetadataProvider value={runtimeMetadata}>
                <StandardCrudDetailPageHost
                  viewKey="identity.account.detail"
                  id="User_2"
                  registration={registration}
                />
              </RuntimeMetadataProvider>
            </PlatformQueryProvider>
          </LocalizationProvider>
        </MemoryRouter>,
      );
    });
    await waitForBodyText('当前操作已被拒绝，请核对当前权限。');

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
            catalogs={{ zhs: { ...platformTestCatalogs.zhs, ...accountListCatalog } }}
          >
            <RuntimeMetadataProvider value={runtimeMetadata}>
              <PlatformQueryProvider scopeKey="user-1">
                <StandardCrudListPageHost viewKey="identity.account.list" registration={registration} />
              </PlatformQueryProvider>
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

  it('retranslates Identity field errors in the real create drawer without losing the draft or retrying', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (_input, init) => {
      if (init?.method === 'POST') {
        return new Response(JSON.stringify({ code: 'IDENTITY_ECODE_INVALID', errors: { ECODE: ['IDENTITY_ECODE_INVALID'] } }),
          { status: 400, headers: { 'Content-Type': 'application/problem+json' } });
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
            supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }, { code: 'en', displayName: 'English' }]}
            catalogs={accountCatalogs}
          >
            <SwitchLanguage /><RuntimeMetadataProvider value={runtimeMetadata}>
              <PlatformQueryProvider scopeKey="user-1">
                <StandardCrudListPageHost viewKey="identity.account.list" registration={registration} />
              </PlatformQueryProvider>
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
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(codeInput, 'bad*');
      codeInput?.dispatchEvent(new Event('input', { bubbles: true }));
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(nameInput, ' Alice ');
      nameInput?.dispatchEvent(new Event('input', { bubbles: true }));
    });
    const saveButton = Array.from(document.body.querySelectorAll('button')).find((button) =>
      button.textContent?.trim() === '保存并关闭');
    await act(async () => saveButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await waitForBodyText('登录账号须为 3–64 个字符');
    await act(async () => Array.from(document.body.querySelectorAll('button')).find(button => button.textContent === 'English test')?.click());
    await waitForBodyText('Use 3–64 characters: letters, digits, periods, underscores, hyphens, or @.');
    expect(document.body.querySelector('#ECODE')).toBe(codeInput);
    expect(codeInput?.value).toBe('bad*');
    expect(nameInput?.value).toBe(' Alice ');
    expect(document.querySelector('[role="dialog"]')?.getAttribute('aria-labelledby')).toBeTruthy();
    expect(document.querySelector('[role="dialog"]')?.textContent).toContain('Create Account');
    expect(document.querySelector('[role="dialog"]')?.textContent).toContain('Account Code');
    expect(document.querySelector('[role="dialog"]')?.textContent).toContain('Display Name');
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1);
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
            catalogs={{ zhs: { ...platformTestCatalogs.zhs, ...accountListCatalog } }}
          >
            <RuntimeMetadataProvider value={runtimeMetadata}>
              <PlatformQueryProvider scopeKey="user-1">
                <StandardCrudListPageHost viewKey="identity.account.list" registration={registration} />
              </PlatformQueryProvider>
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

  it('runs a row credential reset through the Identity command and presents only that one-time result', async () => {
    const account = {
      eid: 'User_2', ecode: 'alice', ename: 'Alice', status: 'Enabled', revision: 7,
      isLocalAdministrator: false, isPersonnelBound: false, personnelDisableHold: false,
    };
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      if (String(input).endsWith('/User_2/credential-reset') && init?.method === 'POST') {
        return new Response(JSON.stringify({
          account: { ...account, revision: 8 }, temporaryCredential: 'reset-only-once',
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }
      return new Response(JSON.stringify({
        items: [account], total: 1, totalCount: 1, page: 1, pageSize: 20,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);

    await act(async () => root.render(
      <MemoryRouter initialEntries={['/identity/accounts']}>
        <LocalizationProvider
          defaultLanguage="zhs"
          supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }, { code: 'en', displayName: 'English' }]}
          catalogs={accountCatalogs}
        >
          <SwitchLanguage /><RuntimeMetadataProvider value={runtimeMetadata}>
            <LocaleQueries>
              <StandardCrudListPageHost viewKey="identity.account.list" registration={registration} />
            </LocaleQueries>
          </RuntimeMetadataProvider>
        </LocalizationProvider>
      </MemoryRouter>,
    ));
    await waitForBodyText('alice');
    const more = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === '更多');
    await act(async () => more?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    const reset = Array.from(document.body.querySelectorAll('[role="menuitem"]')).find((item) =>
      item.textContent?.includes('重置凭据'));
    await act(async () => reset?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await waitForBodyText('确认重置凭据并撤销全部会话？');
    const reason = document.body.querySelector<HTMLTextAreaElement>('textarea[aria-label="操作原因"]');
    expect(reason).not.toBeNull();
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')?.set?.call(reason, '凭据泄露');
      reason?.dispatchEvent(new Event('input', { bubbles: true }));
    });
    const english = Array.from(document.body.querySelectorAll('button')).find(button => button.textContent === 'English test');
    await act(async () => english?.click());
    await waitForBodyText('Operation reason');
    expect(document.body.querySelector<HTMLTextAreaElement>('textarea[aria-label="Operation reason"]')).toBe(reason);
    expect(reason?.value).toBe('凭据泄露');
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(0);
    const confirm = Array.from(document.body.querySelectorAll('button')).find((button) =>
      button.textContent?.replace(/\s+/g, '') === 'Confirm');
    await act(async () => confirm?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await waitForBodyText('reset-only-once');

    expect(document.body.textContent).toContain('One-time temporary password');
    const chinese = Array.from(document.body.querySelectorAll('button')).find(button => button.textContent === 'Chinese test');
    await act(async () => chinese?.click());
    await waitForBodyText('一次性临时密码');
    expect(document.body.textContent).toContain('reset-only-once');
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1);
    const resetCall = fetchMock.mock.calls.find(([input]) => String(input).endsWith('/User_2/credential-reset'));
    expect(JSON.parse(String(resetCall?.[1]?.body))).toEqual({ expectedRevision: 7, reason: '凭据泄露' });
    const close = Array.from(document.body.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('我已安全保存'));
    await act(async () => close?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    expect(document.body.textContent).not.toContain('reset-only-once');
  });

  it('uses the Identity strong confirmation instead of generic delete transport', async () => {
    const account = {
      eid: 'User_2', ecode: 'alice', ename: 'Alice', status: 'Disabled', revision: 7,
      isLocalAdministrator: false, isPersonnelBound: false, personnelDisableHold: false,
    };
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      if (String(input).endsWith('/User_2/delete') && init?.method === 'POST') {
        return new Response(JSON.stringify({ ...account, status: 'Deleted', revision: 8 }), {
          status: 200, headers: { 'Content-Type': 'application/json' },
        });
      }
      return new Response(JSON.stringify({ items: [account], total: 1, totalCount: 1, page: 1, pageSize: 20 }), {
        status: 200, headers: { 'Content-Type': 'application/json' },
      });
    });
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    await act(async () => root.render(
      <MemoryRouter initialEntries={['/identity/accounts']}>
        <LocalizationProvider
          defaultLanguage="zhs"
          supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
          catalogs={{ zhs: { ...platformTestCatalogs.zhs, ...accountListCatalog } }}
        >
          <RuntimeMetadataProvider value={runtimeMetadata}>
            <PlatformQueryProvider scopeKey="user-1">
              <StandardCrudListPageHost viewKey="identity.account.list" registration={registration} />
            </PlatformQueryProvider>
          </RuntimeMetadataProvider>
        </LocalizationProvider>
      </MemoryRouter>,
    ));
    await waitForBodyText('alice');
    const more = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === '更多');
    await act(async () => more?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    const deleteItem = Array.from(document.body.querySelectorAll('[role="menuitem"]')).find((item) =>
      item.textContent?.includes('删除账号'));
    await act(async () => deleteItem?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await waitForBodyText('该操作不可恢复');

    const reason = document.body.querySelector<HTMLTextAreaElement>('textarea[aria-label="操作原因"]');
    const confirmation = document.body.querySelector<HTMLInputElement>('input[aria-label="完整登录账号确认"]');
    expect(reason).not.toBeNull();
    expect(confirmation).not.toBeNull();
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')?.set?.call(reason, '账号退役');
      reason?.dispatchEvent(new Event('input', { bubbles: true }));
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(confirmation, 'alice');
      confirmation?.dispatchEvent(new Event('input', { bubbles: true }));
    });
    const confirm = Array.from(document.body.querySelectorAll('button')).find((button) =>
      button.textContent?.replace(/\s+/g, '') === '确认');
    await act(async () => confirm?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await vi.waitFor(() => expect(fetchMock.mock.calls.some(([input]) => String(input).endsWith('/User_2/delete'))).toBe(true));

    const deleteCall = fetchMock.mock.calls.find(([input]) => String(input).endsWith('/User_2/delete'));
    expect(deleteCall?.[1]?.method).toBe('POST');
    expect(JSON.parse(String(deleteCall?.[1]?.body))).toEqual({ expectedRevision: 7, reason: '账号退役' });
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'DELETE')).toBe(false);
  });

  it('runs the same credential reset action from the complete detail surface', async () => {
    const account = {
      eid: 'User_2', ecode: 'alice', ename: 'Alice', status: 'Enabled', revision: 7,
      isLocalAdministrator: false, isPersonnelBound: false, personnelDisableHold: false,
      createdBy: 'User_1', createdAt: '2026-08-01T00:00:00Z', updatedBy: 'User_1', updatedAt: '2026-08-02T00:00:00Z',
    };
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input);
      if (url.endsWith('/User_2/credential-reset') && init?.method === 'POST') {
        return new Response(JSON.stringify({
          account: { ...account, revision: 8 }, temporaryCredential: 'detail-reset-only-once',
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }
      if (url.includes('/security-events')) {
        return new Response(JSON.stringify({ items: [], page: 1, pageSize: 20, totalCount: 0 }), {
          status: 200, headers: { 'Content-Type': 'application/json' },
        });
      }
      return new Response(JSON.stringify(account), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    await act(async () => root.render(
      <MemoryRouter initialEntries={['/candidate/User_2']}>
        <LocalizationProvider
          defaultLanguage="zhs"
          supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }, { code: 'en', displayName: 'English' }]}
          catalogs={accountCatalogs}
        >
          <SwitchLanguage /><LocaleQueries>
            <RuntimeMetadataProvider value={runtimeMetadata}>
              <Routes>
                <Route path="/candidate/:id" element={<IdentityAccountDetailCandidate registration={registration} />} />
              </Routes>
            </RuntimeMetadataProvider>
          </LocaleQueries>
        </LocalizationProvider>
      </MemoryRouter>,
    ));
    await waitForBodyText('重置凭据');
    const reset = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === '重置凭据');
    await act(async () => reset?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    const reason = document.body.querySelector<HTMLTextAreaElement>('textarea[aria-label="操作原因"]');
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')?.set?.call(reason, '详情重置');
      reason?.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await act(async () => Array.from(document.body.querySelectorAll('button')).find(button => button.textContent === 'English test')?.click());
    await waitForBodyText('Operation reason');
    await flushRequests();
    expect(document.body.querySelector('textarea[aria-label="Operation reason"]')).toBe(reason);
    expect(reason?.value).toBe('详情重置');
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(0);
    const confirm = Array.from(document.body.querySelectorAll('button')).find((button) =>
      button.textContent?.replace(/\s+/g, '') === 'Confirm');
    await act(async () => confirm?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await waitForBodyText('detail-reset-only-once');

    await act(async () => Array.from(document.body.querySelectorAll('button')).find(button => button.textContent === 'Chinese test')?.click());
    await waitForBodyText('一次性临时密码');
    expect(document.body.textContent).toContain('detail-reset-only-once');
    expect(document.querySelector('[role="dialog"] [role="combobox"]')).toBeNull();
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1);
    expect(window.location.href).not.toContain('detail-reset-only-once');
  });

  it.each(['detail', 'drawer'] as const)('keeps the %s edit revision through a locale reread until explicit conflict reload', async presentation => {
    const stale = {
      eid: 'User_2', ecode: 'alice', ename: 'Alice', status: 'Enabled', revision: 7,
      isLocalAdministrator: false, isPersonnelBound: false, personnelDisableHold: false,
      createdBy: 'User_1', createdAt: '2026-08-01T00:00:00Z', updatedBy: 'User_1', updatedAt: '2026-08-02T00:00:00Z',
    };
    const latest = { ...stale, ename: 'Alice Server', revision: 8 };
    let snapshot = stale;
    let snapshotLoads = 0;
    const updateBodies: Record<string, unknown>[] = [];
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input);
      if (url.endsWith('/User_2/snapshot')) {
        snapshotLoads += 1;
        return new Response(JSON.stringify(snapshot), { status: 200 });
      }
      if (url.includes('/security-events')) {
        return new Response(JSON.stringify({ items: [], page: 1, pageSize: 20, totalCount: 0 }), { status: 200 });
      }
      if (init?.method === 'PUT') {
        const body = JSON.parse(String(init.body)) as Record<string, unknown>;
        updateBodies.push(body);
        // 模拟正式版本检查，不能按请求次数强制冲突，否则会掩盖新版本号覆盖旧草稿。
        if (body.expectedRevision !== snapshot.revision) {
          return new Response(JSON.stringify({ code: 'IDENTITY_REVISION_CONFLICT', current: snapshot }), { status: 409 });
        }
        snapshot = { ...snapshot, ename: String(body.ename), revision: snapshot.revision + 1 };
        return new Response(JSON.stringify(snapshot), { status: 200 });
      }
      return new Response(JSON.stringify({ items: [snapshot], total: 1, totalCount: 1, page: 1, pageSize: 20 }), { status: 200 });
    });
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    await act(async () => root.render(
      <MemoryRouter initialEntries={[presentation === 'detail' ? '/candidate/User_2' : '/identity/accounts']}>
        <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }, { code: 'en', displayName: 'English' }]} catalogs={accountCatalogs}>
          <SwitchLanguage /><LocaleQueries><RuntimeMetadataProvider value={runtimeMetadata}>
            {presentation === 'detail'
              ? <Routes><Route path="/candidate/:id" element={<IdentityAccountDetailCandidate registration={registration} />} /></Routes>
              : <StandardCrudListPageHost viewKey="identity.account.list" registration={registration} />}
          </RuntimeMetadataProvider></LocaleQueries>
        </LocalizationProvider>
      </MemoryRouter>,
    ));
    await vi.waitFor(() => expect(Array.from(container.querySelectorAll('button')).some(button => button.textContent?.trim() === '编辑')).toBe(true));
    const edit = Array.from(container.querySelectorAll('button')).find(button => button.textContent?.trim() === '编辑');
    await act(async () => edit?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await vi.waitFor(() => expect(document.body.querySelector<HTMLInputElement>('#ENAME')?.value).toBe('Alice'));
    const changeName = async (value: string) => {
      await act(async () => {
        const input = document.body.querySelector<HTMLInputElement>('#ENAME');
        Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(input, value);
        input?.dispatchEvent(new Event('input', { bubbles: true }));
      });
    };
    await changeName('Alice Draft');
    snapshot = latest;
    const english = Array.from(container.querySelectorAll('button')).find(button => button.textContent === 'English test');
    await act(async () => english?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await vi.waitFor(() => expect(snapshotLoads).toBe(2));
    await flushRequests();
    expect(updateBodies).toHaveLength(0);
    expect(document.body.querySelector<HTMLInputElement>('#ENAME')?.value).toBe('Alice Draft');
    const clickSave = async () => {
      const save = Array.from(document.body.querySelectorAll('button')).find(button => button.textContent?.trim() === accountCatalogs.en['Platform.SaveChanges']);
      expect(save).toBeDefined();
      await act(async () => save?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    };
    await clickSave();
    expect(updateBodies).toEqual([{ ename: 'Alice Draft', expectedRevision: 7 }]);
    expect(snapshot).toEqual(latest);
    expect(snapshotLoads).toBe(2);
    expect(document.body.querySelector<HTMLInputElement>('#ENAME')?.value).toBe('Alice Draft');
    const reload = Array.from(document.body.querySelectorAll('button')).find(button => button.textContent?.trim() === accountCatalogs.en['Platform.ReloadData']);
    expect(reload).toBeDefined();
    await act(async () => reload?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await vi.waitFor(() => expect(document.body.querySelector<HTMLInputElement>('#ENAME')?.value).toBe('Alice Server'));
    expect(snapshotLoads).toBe(3);
    await changeName('Alice Revised');
    await clickSave();
    expect(updateBodies).toEqual([
      { ename: 'Alice Draft', expectedRevision: 7 },
      { ename: 'Alice Revised', expectedRevision: 8 },
    ]);
    await vi.waitFor(() => expect(document.body.querySelector<HTMLInputElement>('#ENAME')).toBeNull());
  });

  it('reloads a conflicted full-detail edit only after an explicit refresh and rebuilds from the latest snapshot', async () => {
    const stale = {
      eid: 'User_2', ecode: 'alice', ename: 'Alice', status: 'Enabled', revision: 7,
      isLocalAdministrator: false, isPersonnelBound: false, personnelDisableHold: false,
      createdBy: 'User_1', createdAt: '2026-08-01T00:00:00Z', updatedBy: 'User_1', updatedAt: '2026-08-02T00:00:00Z',
    };
    const latest = { ...stale, ename: 'Alice Server', revision: 8 };
    let snapshot = stale;
    let snapshotLoads = 0;
    const updateBodies: Record<string, unknown>[] = [];
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input);
      if (url.endsWith('/User_2/snapshot')) {
        snapshotLoads += 1;
        return new Response(JSON.stringify(snapshot), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }
      if (url.includes('/security-events')) {
        return new Response(JSON.stringify({ items: [], page: 1, pageSize: 20, totalCount: 0 }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      if (init?.method === 'PUT') {
        const body = JSON.parse(String(init.body)) as Record<string, unknown>;
        updateBodies.push(body);
        if (updateBodies.length === 1) {
          snapshot = latest;
          return new Response(JSON.stringify({ code: 'IDENTITY_REVISION_CONFLICT', current: latest }), {
            status: 409,
            headers: { 'Content-Type': 'application/problem+json' },
          });
        }
        snapshot = { ...latest, ename: 'Alice Draft', revision: 9 };
        return new Response(JSON.stringify(snapshot), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      throw new Error(`Unexpected Identity detail request: ${url}`);
    });
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);

    await act(async () => root.render(
      <MemoryRouter initialEntries={['/candidate/User_2']}>
        <LocalizationProvider
          defaultLanguage="zhs"
          supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
          catalogs={{ zhs: { ...platformTestCatalogs.zhs, ...accountListCatalog } }}
        >
          <PlatformQueryProvider scopeKey="user-1">
            <RuntimeMetadataProvider value={runtimeMetadata}>
              <Routes>
                <Route path="/candidate/:id" element={<IdentityAccountDetailCandidate registration={registration} />} />
              </Routes>
            </RuntimeMetadataProvider>
          </PlatformQueryProvider>
        </LocalizationProvider>
      </MemoryRouter>,
    ));
    await waitForBodyText('重置凭据');
    const edit = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === '编辑');
    await act(async () => edit?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await waitForBodyText('保存修改');

    const nameInput = document.body.querySelector<HTMLInputElement>('#ENAME');
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(nameInput, 'Alice Draft');
      nameInput?.dispatchEvent(new Event('input', { bubbles: true }));
    });
    const save = Array.from(document.body.querySelectorAll('button')).find((button) =>
      button.textContent?.trim() === '保存修改');
    await act(async () => save?.dispatchEvent(new MouseEvent('click', { bubbles: true })));

    expect(updateBodies).toEqual([{ ename: 'Alice Draft', expectedRevision: 7 }]);
    expect(snapshotLoads).toBe(1);
    expect(document.body.querySelector<HTMLInputElement>('#ENAME')?.value).toBe('Alice Draft');

    const reload = Array.from(document.body.querySelectorAll('button')).find((button) =>
      button.textContent?.trim() === '重新加载当前数据');
    await act(async () => reload?.dispatchEvent(new MouseEvent('click', { bubbles: true })));

    await vi.waitFor(() => expect(snapshotLoads).toBe(2));
    expect(document.body.querySelector<HTMLInputElement>('#ENAME')?.value).toBe('Alice Server');

    const reloadedNameInput = document.body.querySelector<HTMLInputElement>('#ENAME');
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(reloadedNameInput, 'Alice Draft');
      reloadedNameInput?.dispatchEvent(new Event('input', { bubbles: true }));
    });

    const retry = Array.from(document.body.querySelectorAll('button')).find((button) =>
      button.textContent?.trim() === '保存修改');
    await act(async () => retry?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await vi.waitFor(() => expect(updateBodies).toHaveLength(2));

    expect(updateBodies[1]).toEqual({ ename: 'Alice Draft', expectedRevision: 8 });
    await vi.waitFor(() => expect(document.body.querySelector<HTMLInputElement>('#ENAME')).toBeNull());
    expect(snapshotLoads).toBe(3);
  });

  it('refreshes to the latest security state after a list action 409 without replaying the command', async () => {
    const stale = {
      eid: 'User_2', ecode: 'alice', ename: 'Alice', status: 'Enabled', revision: 7,
      isLocalAdministrator: false, isPersonnelBound: false, personnelDisableHold: false,
    };
    const latest = { ...stale, status: 'Disabled', revision: 8, personnelDisableHold: true };
    let current = stale;
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      if (String(input).endsWith('/User_2/status/disable') && init?.method === 'PUT') {
        current = latest;
        return new Response(JSON.stringify({ code: 'IDENTITY_REVISION_CONFLICT', current: latest }), {
          status: 409, headers: { 'Content-Type': 'application/problem+json' },
        });
      }
      return new Response(JSON.stringify({ items: [current], total: 1, totalCount: 1, page: 1, pageSize: 20 }), {
        status: 200, headers: { 'Content-Type': 'application/json' },
      });
    });
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    await act(async () => root.render(
      <MemoryRouter initialEntries={['/identity/accounts']}>
        <LocalizationProvider
          defaultLanguage="zhs"
          supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
          catalogs={{ zhs: { ...platformTestCatalogs.zhs, ...accountListCatalog } }}
        >
          <RuntimeMetadataProvider value={runtimeMetadata}>
            <PlatformQueryProvider scopeKey="user-1">
              <StandardCrudListPageHost viewKey="identity.account.list" registration={registration} />
            </PlatformQueryProvider>
          </RuntimeMetadataProvider>
        </LocalizationProvider>
      </MemoryRouter>,
    ));
    await waitForBodyText('alice');
    const more = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === '更多');
    await act(async () => more?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    const disable = Array.from(document.body.querySelectorAll('[role="menuitem"]')).find((item) =>
      item.textContent?.includes('停用账号'));
    await act(async () => disable?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await waitForBodyText('确认停用当前账号并撤销全部会话？');
    const reason = document.body.querySelector<HTMLTextAreaElement>('textarea[aria-label="操作原因"]');
    expect(reason).not.toBeNull();
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')?.set?.call(reason, '并发处置');
      reason?.dispatchEvent(new Event('input', { bubbles: true }));
    });
    const confirm = Array.from(document.body.querySelectorAll('button')).find((button) =>
      button.textContent?.replace(/\s+/g, '') === '确认');
    await act(async () => confirm?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await waitForBodyText('账号已发生变化：最新状态 Disabled，Revision 8。请查看最新安全状态并重新确认。');

    expect(fetchMock.mock.calls.filter(([input]) => String(input).endsWith('/User_2/status/disable'))).toHaveLength(1);
    expect(document.body.querySelector('textarea[aria-label="操作原因"]')).toBeNull();
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'GET').length).toBeGreaterThanOrEqual(2);
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
            catalogs={{ zhs: { ...platformTestCatalogs.zhs, ...accountListCatalog } }}
          >
            <RuntimeMetadataProvider value={runtimeMetadata}>
              <PlatformQueryProvider scopeKey="user-1">
                <StandardCrudListPageHost viewKey="identity.account.list" registration={registration} />
              </PlatformQueryProvider>
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
