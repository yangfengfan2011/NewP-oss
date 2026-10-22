// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry, type FrontendBehaviorBindings } from '@icp-platform/sdk';
import { loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import documentProjection from '../../../../../tests/Kernel.Tests/Fixtures/DocumentFileDataAccess.contract.json';
import model from '../../../../../tests/Kernel.Tests/Fixtures/DocumentRuntime.contract.json';
import type { RuntimeMetadataModuleEntry } from '../metadata/runtimeMetadataManifest';
import { DocumentFilesPanel } from '../../../../modules/infor-center/document/src/DocumentFilesPanel';
import { bindActionDataAccess, loadRuntimeMetadata } from '../metadata/runtimeMetadataLoader';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { EntryNavigationProvider } from '../navigation/EntryNavigationContext';
import { buildPageEntriesFromNavDocuments } from '../navigation/pageEntryAdapter';
import { compileStandardCrudRouteProjection } from '../navigation/standardCrudRouteAdapter';
import { renderPageEntry } from '../navigation/pageRouteRenderer';
import { RouteAccess } from '../App';
import { AuthProvider } from '../contexts/AuthContext';
import '../pages/StandardCrudDetailPageHost';
import '../pages/StandardCrudListPageHost';
import '@ant-design/pro-descriptions';

const { default: bindings } = await loadComponentSource<{ default: FrontendBehaviorBindings }>('Document', 'index.ts');
await loadComponentSource('Document', 'documentResource.ts');
await loadComponentSource('Document', 'DocumentFilesPanel.tsx');
const projection = documentProjection as unknown as RuntimeMetadataModuleEntry;
const dictionary = readComponentJson('Document', 'metadata/dictionaries/Document.dic.json') as { Document: Record<string, { zhs: string }> };
vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });

it('downloads through the actual Document route entry, metadata Provider and summary binding without a new precheck', async () => {
  const metadata = await loadRuntimeMetadata(vi.fn(async (input: RequestInfo | URL) => {
    const path = String(input);
    if (path.endsWith('metadata-runtime-manifest.json')) return json({ modules: [projection] });
    const relative = path.slice(projection.metadataBasePath.length + 1);
    return json(relative === 'models/Document.model.json' ? model : readComponentJson('Document', 'metadata/' + relative));
  }));
  const reads = projection.dataAccess!.filter(item => item.execution === 'document.document.read');
  const downloads = projection.dataAccess!.filter(item => item.businessBinding === 'document.document.download');
  expect(reads).toHaveLength(1); expect(downloads).toHaveLength(1);
  const [read] = reads; const [download] = downloads;
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Document', displayName: '图文档', version: '1' }, ...bindings }], 'document-file-execution');
  onTestFinished(() => ModuleRegistry.replaceAll([], 'document-file-cleanup'));
  const entries = compileStandardCrudRouteProjection(ModuleRegistry.getAllModules(), metadata.navDocuments, metadata.viewDocuments, []).detailEntries
    .filter(entry => entry.path === '/documents/:id').map(entry => ({ ...entry, entryType: 'standard-crud-detail' as const, targetKey: entry.viewKey }));
  expect(entries).toHaveLength(1);
  const [entry] = entries;
  expect(read.featurePositions).toContain(entry.featurePosition);
  expect(download.featurePositions).toHaveLength(1);
  const [downloadPosition] = download.featurePositions;
  const user = { id: 'file-reader', ecode: 'file-reader', ename: '文件读者', revision: 1, mustChangePassword: false,
    isLocalAdministrator: false, timeZoneId: 'UTC', featurePositions: [entry.featurePosition!, downloadPosition] };
  const fields = { EID: 'document-one', ECODE: 'D-313', ENAME: '文件声明', Description: '', LatestVersionId: 'version-one',
    CreatedBy: 'file-reader', UpdatedBy: 'file-reader', CreatedAt: '2026-10-20T00:00:00Z', UpdatedAt: '2026-10-20T00:00:00Z' };
  const requests: Request[] = [];
  let denied = false;
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    const path = new URL(request.url).pathname;
    if (path.endsWith('/current-user')) return json(user);
    requests.push(request);
    const isDownload = path.endsWith('/content');
    const expected = isDownload ? download : read;
    if (request.headers.get(isDownload ? 'X-NewP-Button' : 'X-NewP-Data-Declaration') !== expected.id
        || request.headers.has(isDownload ? 'X-NewP-Data-Declaration' : 'X-NewP-Button')
        || request.headers.get('X-NewP-Feature') !== (isDownload ? downloadPosition : entry.featurePosition))
      return json({ code: 'ActionDataAccess.DeclarationInvalid' }, 400);
    if (path === '/api/document/documents/document-one') return json({ fields,
      summaries: { files: { status: 'ready', data: { documentId: fields.EID, featurePosition: entry.featurePosition } } } });
    if (path === '/api/document/documents/document-one/files') return json({ documentId: fields.EID, versionId: 'version-one',
      iterationId: 'iteration-one', downloadAllowed: true, files: [{ fileLinkId: 'file-link-one', role: 'main', ordinal: 0,
        fileName: 'main.pdf', length: 3, contentType: 'application/pdf' }] });
    if (path === '/api/document/documents/document-one/files/file-link-one/content') return denied
      ? json({ code: 'Document.DownloadDenied', reasonKey: 'Document.DownloadDenied' }, 403) : new Response(new Uint8Array([1, 2, 3]));
    throw new Error(`Unexpected document file request ${request.method} ${path}`);
  }));
  const anchor = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
  const createUrl = vi.fn(() => 'blob:document-file'); const revokeUrl = vi.fn();
  const previousCreate = Object.getOwnPropertyDescriptor(URL, 'createObjectURL');
  const previousRevoke = Object.getOwnPropertyDescriptor(URL, 'revokeObjectURL');
  onTestFinished(() => {
    if (previousCreate) Object.defineProperty(URL, 'createObjectURL', previousCreate); else Reflect.deleteProperty(URL, 'createObjectURL');
    if (previousRevoke) Object.defineProperty(URL, 'revokeObjectURL', previousRevoke); else Reflect.deleteProperty(URL, 'revokeObjectURL');
  });
  Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: createUrl });
  Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: revokeUrl });
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const words = Object.fromEntries(Object.entries(dictionary.Document).map(([key, value]) => ['Document.' + key, value.zhs]));
  const router = createMemoryRouter([{ path: entry.path, element: <LocalizationProvider defaultLanguage="zhs"
    supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: words }}><AuthProvider initialUser={user}>
    <AuthorizationProvider featurePositions={user.featurePositions}><PlatformQueryProvider scopeKey="document-file-execution">
      <RuntimeMetadataProvider value={metadata}><EntryNavigationProvider><RouteAccess featurePosition={entry.featurePosition}>
        {renderPageEntry(entry)}
      </RouteAccess></EntryNavigationProvider></RuntimeMetadataProvider>
    </PlatformQueryProvider></AuthorizationProvider></AuthProvider></LocalizationProvider> }], { initialEntries: ['/documents/document-one'] });
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); anchor.mockRestore();
    vi.unstubAllGlobals(); });
  await act(async () => root.render(<RouterProvider router={router} />));
  await vi.waitFor(() => expect(requests.some(request => new URL(request.url).pathname.endsWith('/files'))).toBe(true));
  expect(requests.find(request => new URL(request.url).pathname.endsWith('/files'))!.headers.get('X-NewP-Data-Declaration')).toBe(read.id);
  const button = () => document.querySelector<HTMLButtonElement>('section button[aria-label="下载"]');
  await vi.waitFor(() => expect(button()).not.toBeNull());
  expect(button()!.disabled).toBe(false);
  await act(async () => button()!.click());
  await vi.waitFor(() => expect(anchor).toHaveBeenCalledTimes(1));
  const content = requests.filter(request => new URL(request.url).pathname.endsWith('/content'));
  expect(content).toHaveLength(1);
  expect(content[0].headers.get('X-NewP-Feature')).toBe(downloadPosition);
  expect(content[0].headers.get('X-NewP-Button')).toBe(download.id);
  expect(content[0].headers.has('X-NewP-Data-Declaration')).toBe(false);
  expect(createUrl).toHaveBeenCalledTimes(1); expect(revokeUrl).toHaveBeenCalledWith('blob:document-file');
  denied = true;
  await act(async () => button()!.click());
  await vi.waitFor(() => expect(document.querySelector('section .ant-alert')).not.toBeNull());
  expect(document.body.textContent).toContain('main.pdf');
  expect(anchor).toHaveBeenCalledTimes(1);
  expect(requests.every(request => request.method === 'GET')).toBe(true);
});

it.each(['missing-projection', 'duplicate-projection', 'missing-authoring'] as const)('rejects a featureAction binding with %s', mode => {
  const document = readComponentJson('Document', 'metadata/views/DocumentDetail.view.json');
  const download = projection.dataAccess!.filter(item => item.businessBinding === 'document.document.download');
  expect(download).toHaveLength(1);
  if (mode === 'missing-authoring') delete document.view.featureActions[0].dataAccess;
  const declarations = mode === 'missing-projection' ? projection.dataAccess!.filter(item => item.id !== download[0].id)
    : mode === 'duplicate-projection' ? [...projection.dataAccess!, download[0]] : projection.dataAccess!;
  expect(() => bindActionDataAccess(document, declarations)).toThrow('数据声明');
});

it('cancels a pending download when its declaration disappears at the same object and feature position', async () => {
  const reads = projection.dataAccess!.filter(item => item.execution === 'document.document.read');
  const downloads = projection.dataAccess!.filter(item => item.businessBinding === 'document.document.download');
  expect(reads).toHaveLength(1); expect(downloads).toHaveLength(1);
  const [read] = reads; const [download] = downloads;
  expect(download.featurePositions).toHaveLength(1); const [downloadPosition] = download.featurePositions;
  const readPosition = read.featurePositions.filter(position => projection.features!.some(point => point.position === position && point.kind === 'navigation'));
  expect(readPosition).toHaveLength(1);
  let pending: Request | undefined; let finish!: (response: Response) => void;
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    if (new URL(request.url).pathname.endsWith('/content')) { pending = request; return await new Promise<Response>(resolve => { finish = resolve; }); }
    expect(request.headers.get('X-NewP-Data-Declaration')).toBe(read.id);
    return json({ documentId: 'document-one', versionId: 'version', iterationId: 'iteration', downloadAllowed: true,
      files: [{ fileLinkId: 'link', role: 'main', ordinal: 0, fileName: 'main.pdf', length: 3, contentType: 'application/pdf' }] });
  }));
  const anchor = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const render = (enabled: boolean) => <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]}
    catalogs={{ zhs: {} }}><PlatformQueryProvider scopeKey="document-file-cancellation"><DocumentFilesPanel documentId="document-one"
      featurePosition={readPosition[0]} dataAccess={read} downloadPosition={downloadPosition} downloadButtonId={enabled ? download.id : undefined} title="文件" />
    </PlatformQueryProvider></LocalizationProvider>;
  onTestFinished(async () => { await act(async () => root.unmount()); container.remove(); anchor.mockRestore(); vi.unstubAllGlobals(); });
  await act(async () => root.render(render(true)));
  const button = () => container.querySelector<HTMLButtonElement>('button[aria-label="下载"]')!;
  await vi.waitFor(() => expect(button()).not.toBeNull());
  await act(async () => button().click());
  await vi.waitFor(() => expect(pending).toBeDefined());
  expect(pending!.headers.get('X-NewP-Button')).toBe(download.id);
  expect(pending!.headers.has('X-NewP-Data-Declaration')).toBe(false);
  await act(async () => root.render(render(false)));
  expect(pending!.signal.aborted).toBe(true);
  expect(button().disabled).toBe(true);
  await act(async () => finish(new Response(new Uint8Array([1, 2, 3]))));
  expect(anchor).not.toHaveBeenCalled();
  expect(container.textContent).toContain('main.pdf');
});

it('retires a pending metadata query when its declaration disappears without changing the object or feature', async () => {
  const reads = projection.dataAccess!.filter(item => item.execution === 'document.document.read');
  expect(reads).toHaveLength(1); const [read] = reads;
  const positions = read.featurePositions.filter(position => projection.features!.some(point => point.position === position && point.kind === 'navigation'));
  expect(positions).toHaveLength(1);
  let original: Request | undefined; let finish!: (response: Response) => void;
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    if (!request.headers.has('X-NewP-Data-Declaration')) return json({ code: 'ActionDataAccess.DeclarationInvalid' }, 400);
    original = request;
    return await new Promise<Response>(resolve => { finish = resolve; });
  }));
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const render = (enabled: boolean) => <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]}
    catalogs={{ zhs: {} }}><PlatformQueryProvider scopeKey="document-metadata-cancellation"><DocumentFilesPanel documentId="document-one"
      featurePosition={positions[0]} dataAccess={enabled ? read : undefined} title="文件" /></PlatformQueryProvider></LocalizationProvider>;
  onTestFinished(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); });
  await act(async () => root.render(render(true)));
  await vi.waitFor(() => expect(original).toBeDefined());
  await act(async () => root.render(render(false)));
  await vi.waitFor(() => expect(original!.signal.aborted).toBe(true));
  await vi.waitFor(() => expect(container.querySelector('.ant-alert')).not.toBeNull());
  await act(async () => finish(json({ documentId: 'document-one', versionId: 'version', iterationId: 'iteration', downloadAllowed: true,
    files: [{ fileLinkId: 'link', role: 'main', ordinal: 0, fileName: 'stale.pdf', length: 3, contentType: 'application/pdf' }] })));
  expect(container.textContent).not.toContain('stale.pdf');
});

it('creates through the actual Document list PageEntry and its compiled candidate declaration', async () => {
  const metadata = await loadRuntimeMetadata(vi.fn(async (input: RequestInfo | URL) => {
    const path = String(input);
    if (path.endsWith('metadata-runtime-manifest.json')) return json({ modules: [projection] });
    const relative = path.slice(projection.metadataBasePath.length + 1);
    return json(relative === 'models/Document.model.json' ? model : readComponentJson('Document', 'metadata/' + relative));
  }));
  const creates = projection.dataAccess!.filter(item => item.businessBinding === 'document.document.create');
  const lists = projection.dataAccess!.filter(item => item.execution === 'document.document.list');
  expect(creates).toHaveLength(1); expect(lists).toHaveLength(1);
  const [create] = creates; const [list] = lists;
  expect(create.featurePositions).toHaveLength(1);
  const [createPosition] = create.featurePositions;
  const entries = buildPageEntriesFromNavDocuments(metadata.navDocuments).filter(entry => entry.path === '/documents');
  expect(entries).toHaveLength(1);
  const [entry] = entries;
  expect(list.featurePositions).toContain(entry.featurePosition);
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Document', displayName: '图文档', version: '1' }, ...bindings }], 'document-create-execution');
  onTestFinished(() => ModuleRegistry.replaceAll([], 'document-create-cleanup'));
  const user = { id: 'document-creator', ecode: 'document-creator', ename: '图文档创建主体', revision: 1, mustChangePassword: false,
    isLocalAdministrator: false, timeZoneId: 'UTC', featurePositions: [entry.featurePosition!, createPosition] };
  const requests: Request[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    const path = new URL(request.url).pathname;
    if (path.endsWith('/current-user')) return json(user);
    if (path.endsWith('/antiforgery-token')) return json({ requestToken: 'token' });
    requests.push(request);
    const expected = request.method === 'POST' ? create : list;
    if (request.headers.get(request.method === 'POST' ? 'X-NewP-Button' : 'X-NewP-Data-Declaration') !== expected.id
        || request.headers.has(request.method === 'POST' ? 'X-NewP-Data-Declaration' : 'X-NewP-Button')
        || request.headers.get('X-NewP-Feature') !== (request.method === 'POST' ? createPosition : entry.featurePosition))
      return json({ code: 'ActionDataAccess.DeclarationInvalid' }, 400);
    if (path === '/api/document/documents' && request.method === 'POST') return json({ succeeded: true });
    if (path === '/api/document/documents' && request.method === 'GET') return json({ items: [], total: 0, page: 1, pageSize: 20 });
    throw new Error(`Unexpected Document create request ${request.method} ${path}`);
  }));
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const words = Object.fromEntries(Object.entries(dictionary.Document).map(([key, value]) => ['Document.' + key, value.zhs]));
  const router = createMemoryRouter([{ path: entry.path, element: <LocalizationProvider defaultLanguage="zhs"
    supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: words }}><AuthProvider initialUser={user}>
    <AuthorizationProvider featurePositions={user.featurePositions}><PlatformQueryProvider scopeKey="document-create-execution">
      <RuntimeMetadataProvider value={metadata}><EntryNavigationProvider><RouteAccess featurePosition={entry.featurePosition}>
        {renderPageEntry(entry)}
      </RouteAccess></EntryNavigationProvider></RuntimeMetadataProvider>
    </PlatformQueryProvider></AuthorizationProvider></AuthProvider></LocalizationProvider> }], { initialEntries: ['/documents'] });
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); vi.unstubAllGlobals(); });
  await act(async () => root.render(<RouterProvider router={router} />));
  await vi.waitFor(() => expect(container.textContent).toContain('当前数据集为空'));
  await act(async () => [...document.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.replace(/\s/g, '') === '新建图文档')!.click());
  await vi.waitFor(() => expect(document.querySelector('.ant-drawer form')).not.toBeNull());
  for (const [label, value] of [['编码', 'D-CREATE-313'], ['名称', '真实声明创建']]) {
    const field = [...document.querySelectorAll<HTMLLabelElement>('.ant-drawer label')].find(field => field.textContent?.includes(label))!;
    const input = document.getElementById(field.htmlFor) as HTMLInputElement;
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, value);
      input.dispatchEvent(new Event('input', { bubbles: true }));
    });
  }
  await act(async () => [...document.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.replace(/\s/g, '') === '保存并关闭')!.click());
  await vi.waitFor(() => expect(requests.filter(request => request.method === 'POST')).toHaveLength(1));
  await vi.waitFor(() => expect(document.querySelector('.ant-drawer-open')).toBeNull());
  const submitted = requests.filter(request => request.method === 'POST');
  expect(submitted[0].headers.get('X-NewP-Feature')).toBe(createPosition);
  expect(submitted[0].headers.get('X-NewP-Button')).toBe(create.id);
  expect(submitted[0].headers.has('X-NewP-Data-Declaration')).toBe(false);
  expect(await submitted[0].clone().json()).toEqual({ ECODE: 'D-CREATE-313', ENAME: '真实声明创建', Description: null });
  await vi.waitFor(() => expect(requests.filter(request => request.method === 'GET')).toHaveLength(2));
  expect(requests.every(request => new URL(request.url).pathname === '/api/document/documents')).toBe(true);
});
