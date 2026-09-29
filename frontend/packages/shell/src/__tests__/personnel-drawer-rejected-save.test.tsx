import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter, useNavigate } from 'react-router-dom';
import { beforeAll, expect, it, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, useLocalization, invalidatePlatformAntiforgeryToken } from '@icp-platform/core';
import { AuthProvider } from '../contexts/AuthContext';
import { CurrentPermissionScope } from '../components/CurrentPermissionScope';
import { FeaturePlacementContext } from '../authorization/FeaturePlacementContext';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { PlatformRequestLocaleBridge } from '../localization/PlatformRequestLocaleBridge';
import StandardCrudListPageHost from '../pages/StandardCrudListPageHost';
import { personStandardCrudResource } from '../../../../modules/organization/src/standard-crud/personStandardCrudResource';
import runtimeModels from '../../../../../tests/Kernel.Tests/Fixtures/OrganizationRuntimeModels.contract.json';
import personList from '../../../../../modules/Organization/metadata/views/PersonList.view.json';
import personDetail from '../../../../../modules/Organization/metadata/views/PersonDetail.view.json';
import { platformTestCatalogs } from './localizationTestCatalogs';

vi.unmock('@icp-platform/core');
beforeAll(async () => { await Promise.all([import('../pages/StandardCrudListDrawerRuntime'), import('@ant-design/pro-table'), import('@ant-design/pro-descriptions')]); });

it('keeps a denied save result through permission-scope refresh, view downgrade and successful detail reread', async () => {
  const fields = { EID: 'Person_1', PersonNo: 'P1', ENAME: 'Person', EmploymentStatus: 'FullTime', IsEnabled: true };
  const user = { id: 'writer', ecode: 'writer', ename: 'Writer', revision: 1, mustChangePassword: false,
    isLocalAdministrator: false, timeZoneId: 'Asia/Taipei', featurePositions: ['read', 'edit'] };
  let writes = 0;
  let viewReadsAfterFailure = 0;
  let editRejections = 0;
  let setLanguage: (value: string) => void = () => undefined;
  let navigate: ReturnType<typeof useNavigate>;
  const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    const path = new URL(request.url).pathname;
    if (path.endsWith('antiforgery-token')) return json({ requestToken: 'test' });
    if (path.endsWith('current-user')) return json(user);
    if (request.method === 'PUT') { writes++; return json({ code: 'AUTH_FORBIDDEN', traceId: 'write-trace' }, 403); }
    if (path.includes('/options') || path.includes('/person-lookups/')) return json({ items: [], currentItems: [], hasMore: false });
    if (path.endsWith('/Person_2')) return json({ fields: { ...fields, EID: 'Person_2', ENAME: 'Other person' }, summaries: {} });
    if (path.endsWith('/Person_1')) {
      if (writes && request.headers.get('X-NewP-Feature') === 'edit') { editRejections++; return json({ code: 'AUTH_FORBIDDEN' }, 403); }
      if (writes) viewReadsAfterFailure++;
      return json({ fields, summaries: {} });
    }
    return json({ items: [fields], total: 1, page: 1, pageSize: 20 });
  }));
  function Consumer() {
    setLanguage = useLocalization().setLanguage;
    navigate = useNavigate();
    return <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'Organization', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features: [
      { key: 'edit', kind: 'action', position: 'edit', parentPosition: 'read', actionScope: 'list.rowActions' },
    ] }] }, navDocuments: [], viewDocuments: [personList, personDetail], modelDocuments: [runtimeModels.person] }}>
      <FeaturePlacementContext.Provider value={{ entryPosition: 'read' }}><StandardCrudListPageHost viewKey="organization.person.list" registration={personStandardCrudResource} /></FeaturePlacementContext.Provider>
    </RuntimeMetadataProvider>;
  }
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  try {
    await act(async () => root.render(<MemoryRouter initialEntries={['/organization/persons?drawer=edit&id=Person_1']}><LocalizationProvider
      defaultLanguage="zhs" initialLanguage="zhs" catalogs={platformTestCatalogs} supportedLanguages={[{ code: 'zhs', displayName: '中文', locale: 'zh-CN' }, { code: 'en', displayName: 'English', locale: 'en-US' }]}>
      <PlatformRequestLocaleBridge><AuthProvider initialUser={user}><CurrentPermissionScope><AuthorizationProvider featurePositions={user.featurePositions}><Consumer /></AuthorizationProvider></CurrentPermissionScope></AuthProvider></PlatformRequestLocaleBridge>
    </LocalizationProvider></MemoryRouter>));
    await vi.waitFor(() => expect(document.querySelector('form.np-standard-crud-form')).not.toBeNull());
    await vi.waitFor(() => expect(document.querySelector<HTMLInputElement>('input#ENAME')?.value).toBe('Person'));
    await act(async () => document.querySelector('form.np-standard-crud-form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
    await vi.waitFor(() => expect(viewReadsAfterFailure).toBeGreaterThan(0));
    expect(editRejections).toBeGreaterThan(0);
    expect(document.querySelector('form.np-standard-crud-form')).toBeNull();
    expect(document.querySelector('.ant-drawer')?.textContent).toContain('当前操作已被拒绝，请核对当前权限。');
    expect(document.querySelector('.ant-drawer')?.textContent).toContain('write-trace');
    await act(async () => setLanguage('en'));
    await vi.waitFor(() => expect(document.querySelector('.ant-drawer')?.textContent).toContain('This operation was denied. Check your permissions.'));
    expect(writes).toBe(1);
    await act(async () => document.querySelector<HTMLButtonElement>('.ant-drawer-close')!.click());
    await vi.waitFor(() => expect(document.querySelector('.ant-drawer')).toBeNull());
    expect(document.body.textContent).not.toContain('write-trace');
    await act(async () => navigate('/organization/persons?drawer=view&id=Person_2'));
    await vi.waitFor(() => expect(document.querySelector('.ant-drawer')?.textContent).toContain('Other person'));
    expect(document.querySelector('.ant-drawer')?.textContent).not.toContain('write-trace');
    expect(writes).toBe(1);
  } finally { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); invalidatePlatformAntiforgeryToken(); }
});
