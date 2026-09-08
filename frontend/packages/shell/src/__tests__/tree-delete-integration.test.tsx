// @vitest-environment jsdom
import { act, StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, it, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry, type StandardCrudActionContext } from '@icp-platform/sdk';
import organizationBindings from '../../../../modules/organization/src';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { PageEntry } from '../composition/PageOutlet';
import list from '../../../../../modules/Organization/metadata/views/DepartmentList.view.json';
import detail from '../../../../../modules/Organization/metadata/views/DepartmentDetail.view.json';
import treeContract from '../../../../../tests/Kernel.Tests/Fixtures/OrganizationTreeActions.contract.json';
import runtimeModels from '../../../../../tests/Kernel.Tests/Fixtures/OrganizationRuntimeModels.contract.json';
vi.mock('@icp-platform/core', async original => ({ ...await original<typeof import('@icp-platform/core')>(), platformFetch: (...args: Parameters<typeof fetch>) => fetch(...args) }));
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
it.each(['standard', 'stable', 'handler', 'input', 'denied', 'disabled', 'create', 'pending-cancel', 'pending-close', 'pending-escape', 'read-cancel'])('consumes compiled tree actions through the real Organization adapter (%s)', async scenario => {
  const fields = { EID: 'd', CompanyId: 'c', CompanyId_DisplayValue: 'Company', ParentDepartmentId: null, ParentDepartmentId_DisplayValue: null, ECODE: 'D', ENAME: 'Department', IsEnabled: true, SortOrder: '001', UpdatedAt: '2026-09-08T00:00:00Z' };
  const requests: string[] = [];
  const pending = scenario.startsWith('pending-');
  const readCancelled = scenario === 'read-cancel';
  let readSignal: AbortSignal | null | undefined;
  let releaseRead!: () => void;
  const readResponse = new Promise<void>(resolve => { releaseRead = resolve; });
  let releaseDelete!: () => void;
  const deleteResponse = new Promise<void>(resolve => { releaseDelete = resolve; });
  const fetcher = vi.spyOn(globalThis, 'fetch').mockImplementation(async (url, init) => {
    requests.push(`${init?.method ?? 'GET'} ${String(url)}`);
    if (init?.method === 'DELETE') { if (pending) await deleteResponse; return new Response(null, { status: 204 }); }
    if (String(url).endsWith('/departments/d')) { if (readCancelled) { readSignal = init?.signal; await readResponse; } return new Response(JSON.stringify({ fields, summaries: {} })); }
    return new Response(JSON.stringify({ items: [], currentItems: [], page: 1, pageSize: 20, hasMore: false }));
  });
  const listDocument = structuredClone(list);
  const action = listDocument.view.rowActions.find(item => item.standardAction === 'delete')! as Record<string, unknown>;
  const handler = vi.fn();
  if (scenario === 'handler' || scenario === 'input') { delete action.standardAction; action.handlerKey = 'delete'; }
  if (scenario === 'input') action.inputKey = 'review';
  const capabilities = organizationBindings.standardCrudCapabilities!.map(binding => binding.targetKey !== 'organization.department.list' ? binding : { ...binding, load: async () => {
    const loaded = await binding.load();
    return { default: { ...loaded.default,
      actionHandlers: { delete: async (context: StandardCrudActionContext) => {
        handler(context); return loaded.default.dataAdapter.remove!(context.id!, context.row as Record<string, unknown>);
      } },
      actionInputRenderers: { review: ({ onChange }: { onChange: (value: unknown) => void }) => <button onClick={() => onChange({ reason: 'confirmed' })}>Existing action input</button> },
      ...(scenario === 'disabled' ? { actionStateResolver: () => ({ visible: true, available: false, reason: 'Unavailable' }) } : {}),
    } };
  } });
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'test', displayName: 'Test', version: '1' },
    standardCrudCapabilities: capabilities,
    pages: [{ pageKey: 'content', load: async () => ({ default: () => <p>node content</p> }) }],
    pageInputResolvers: [ { key: 'scope', pageKeys: ['platform.tree'], resolve: parameters => ({ parameters, treeScope: {} }) }, { key: 'node', pageKeys: ['platform.list', 'platform.detail'], resolve: (parameters, context) => ({ parameters, context, recordId: String(context!.context!.id) }) } ],
    treeProviders: [{ key: 'tree', roots: async () => [{ nodeKey: 'd', nodeType: 'department', label: 'Department', hasChildren: false, context: { id: 'd' } }, ...(pending || readCancelled ? [{ nodeKey: 'other', nodeType: 'department', label: 'Other', hasChildren: false, context: { id: 'other' } }] : [])], children: async () => [], path: async () => [], search: vi.fn(), resolveChange: async () => ({ kind: 'unrelated' }) }],
  }], 'test');
  const actionReference = { ...treeContract.view.nodeTypes[1].contextMenu[1] } as Record<string, unknown>;
  if (scenario === 'standard') { delete actionReference.actionKey; actionReference.standardAction = 'delete'; }
  actionReference.inputResolverKey = 'node';
  const tree = { type: 'view', view: { key: 'tree', mode: 'tree', dicKey: 'Tree', providerKey: 'tree', treeActions: treeContract.view.treeActions,
    nodeTypes: [{ key: 'department', titleKey: 'Department', pageRef: { pageKey: 'content' }, contextMenu: [actionReference] }] } };

  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const router = createMemoryRouter([{ path: '/', element: <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}><AuthorizationProvider permissions={scenario === 'denied' ? ['Organization.Department.View'] : ['Organization.Department.View', 'Organization.Department.Delete', 'Organization.Department.Create']}><PlatformQueryProvider scopeKey="test"><RuntimeMetadataProvider value={{ manifest: { modules: [] }, navDocuments: [], modelDocuments: [runtimeModels.department], viewDocuments: [listDocument, detail, tree] }}><PageEntry page={{ pageKey: 'platform.tree', parameters: { viewKey: 'tree' }, inputResolverKey: 'scope' }} /></RuntimeMetadataProvider></PlatformQueryProvider></AuthorizationProvider></LocalizationProvider> }]);
  const waitFor = async (condition: () => boolean) => { for (let i = 0; i < 100 && !condition(); i++) await act(async () => { await new Promise(resolve => setTimeout(resolve, 10)); }); expect(condition(), document.body.textContent ?? '').toBe(true); };
  try {
    await act(async () => root.render(<StrictMode><RouterProvider router={router} /></StrictMode>));
    await waitFor(() => Boolean(container.querySelector('.ant-tree-title')));
    expect([...container.querySelectorAll('aside > button')].map(button => button.textContent?.replace(/\s/g, ''))).toEqual(['CreateCompany', 'Department']);
    if (scenario === 'create') {
      await act(async () => (container.querySelectorAll('aside > button')[1] as HTMLElement).click());
      await waitFor(() => Boolean(document.querySelector('.ant-drawer')));
      expect(document.body.textContent).toContain('Action_Create');
      expect(requests.filter(item => item.startsWith('DELETE'))).toHaveLength(0);
      return;
    }
    await act(async () => container.querySelector('.ant-tree-title')!.firstElementChild!.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true })));
    await waitFor(() => Boolean(document.querySelector('[role=menuitem]')));
    await act(async () => (document.querySelector('[role=menuitem]') as HTMLElement).click());
    if (readCancelled) {
      await waitFor(() => requests.some(item => item === 'GET /api/organization/departments/d'));
      expect(readSignal).toBeInstanceOf(AbortSignal);
      await act(async () => [...container.querySelectorAll('.ant-tree-title')].find(item => item.textContent === 'Other')!.dispatchEvent(new MouseEvent('click', { bubbles: true })));
      await waitFor(() => readSignal?.aborted === true);
      await act(async () => { releaseRead(); await readResponse; });
      expect(container.querySelector('.ant-tree-node-selected')?.textContent).toBe('Other');
      expect(document.querySelector('[role=dialog]')).toBeNull();
      expect(requests.filter(item => item.startsWith('DELETE'))).toHaveLength(0);
      return;
    }
    if (scenario === 'denied' || scenario === 'disabled') {
      await waitFor(() => Boolean(document.querySelector('[role=alert]')));
      expect(requests.filter(item => item.startsWith('DELETE'))).toHaveLength(0);
      expect(document.querySelector('[role=dialog]')).toBeNull();
      return;
    }
    await waitFor(() => Boolean(document.querySelector('[role=dialog]')));
    if (scenario === 'input') {
      const inputButton = [...document.querySelectorAll('button')].find(button => button.textContent === 'Existing action input')!;
      await act(async () => inputButton.click());
      expect(document.querySelectorAll('[role=dialog]')).toHaveLength(1);
    }
    expect(requests.filter(item => item.startsWith('DELETE'))).toHaveLength(0);
    const confirm = [...document.querySelectorAll('[role=dialog] button')].find(item => item.textContent?.replace(/\s/g, '') === '确认') as HTMLElement;
    await act(async () => confirm.click());
    expect(requests.filter(item => item.startsWith('DELETE'))).toEqual(['DELETE /api/organization/departments/d']);
    if (pending) {
      const dialog = document.querySelector('[role=dialog]')!;
      await act(async () => {
        if (scenario === 'pending-cancel') ([...dialog.querySelectorAll('button')].find(button => button.textContent?.replace(/\s/g, '') === '取消') as HTMLElement).click();
        else if (scenario === 'pending-close') (dialog.querySelector('.ant-modal-close') as HTMLElement).click();
        else dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', keyCode: 27, bubbles: true }));
      });
      expect(document.querySelector('[role=dialog]'), 'pending confirmation must retain its editor').not.toBeNull();
      await act(async () => [...container.querySelectorAll('.ant-tree-title')].find(item => item.textContent === 'Other')!.dispatchEvent(new MouseEvent('click', { bubbles: true })));
      expect(container.querySelector('.ant-tree-node-selected')?.textContent).toBe('Department');
      await act(async () => confirm.click());
      expect(requests.filter(item => item.startsWith('DELETE'))).toHaveLength(1);
      await act(async () => { releaseDelete(); await deleteResponse; });
      await waitFor(() => document.querySelector('[role=dialog]') === null);
    }
    expect(handler).toHaveBeenCalledTimes(1);
    expect(handler.mock.calls[0][0]).toMatchObject({ id: 'd', row: { EID: 'd' } });
    if (scenario === 'input') expect(handler.mock.calls[0][0].input).toEqual({ reason: 'confirmed' });
  } finally { releaseRead(); releaseDelete(); await act(async () => root.unmount()); router.dispose(); container.remove(); fetcher.mockRestore(); }
});
