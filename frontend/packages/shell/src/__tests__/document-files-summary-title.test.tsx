// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
import { LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';
import model from '../../../../../tests/Kernel.Tests/Fixtures/DocumentRuntime.contract.json';
import { loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import { RuntimeMetadataProvider } from './formalRuntimeMetadataProvider';
import StandardCrudDetailPageHost from '../pages/StandardCrudDetailPageHost';
// 本例核对真实标题接线；详情控件的冷装载留在收集阶段。
import '@ant-design/pro-descriptions';

const { documentResource } = await loadComponentSource<{ documentResource: StandardCrudResourceRegistration }>('Document', 'documentResource.ts');
await loadComponentSource('Document', 'DocumentFilesPanel.tsx');
const detail = readComponentJson('Document', 'metadata/views/DocumentDetail.view.json');
const dictionary = readComponentJson('Document', 'metadata/dictionaries/Document.dic.json') as {
  Document: Record<string, { zhs: string; en: string }>;
};

it.each([['zhs', '文件'], ['en', 'Files']] as const)('localizes the actual Document summary title in the detail Host for %s', async (language, title) => {
  const element = document.createElement('div'); document.body.append(element);
  const root = createRoot(element);
  onTestFinished(async () => { await act(async () => root.unmount()); element.remove(); vi.unstubAllGlobals(); });
  const fields = { EID: 'doc-1', ECODE: 'DOC-1', ENAME: 'Document', Description: '', LatestVersionId: 'version-1',
    CreatedBy: 'actor', CreatedBy_DisplayValue: 'Creator', UpdatedBy: 'actor', UpdatedBy_DisplayValue: 'Creator',
    CreatedAt: '2026-10-13T00:00:00Z', CreatedAt_DisplayValue: '2026/10/13 08:00',
    UpdatedAt: '2026-10-13T00:00:00Z', UpdatedAt_DisplayValue: '2026/10/13 08:00' };
  // 正式 provider 只交接目标及读取位置，标题由实际 schema/dicKey → Host summary slot 解析。
  const summaries = { files: { status: 'ready' as const, data: { documentId: 'doc-1', featurePosition: 'document-read' } } };
  const read = vi.fn(async () => ({ normalized: { fields, summaries }, value: { fields, summaries } }));
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ documentId: 'doc-1', versionId: 'version-1',
    iterationId: 'iteration-1', downloadAllowed: false, files: [{ fileLinkId: 'link-1', role: 'main', ordinal: 0,
      fileName: 'main.pdf', length: 4, contentType: 'application/pdf' }] })));
  vi.stubGlobal('fetch', fetch);
  const catalog = Object.fromEntries(Object.entries(dictionary.Document).map(([key, value]) => [`Document.${key}`, value[language]]));
  await act(async () => root.render(<MemoryRouter><LocalizationProvider defaultLanguage={language}
    supportedLanguages={[{ code: 'zhs', displayName: '中文' }, { code: 'en', displayName: 'English' }]} catalogs={{ [language]: catalog }}>
    <PlatformQueryProvider scopeKey={`document-title-${language}`}><RuntimeMetadataProvider
      value={{ manifest: { modules: [] }, modelDocuments: [model], viewDocuments: [detail], navDocuments: [] }}>
      <StandardCrudDetailPageHost viewKey="document.document.detail" id="doc-1"
        registration={{ ...documentResource, dataAdapter: { ...documentResource.dataAdapter, get: read } }} />
    </RuntimeMetadataProvider></PlatformQueryProvider>
  </LocalizationProvider></MemoryRouter>));
  await vi.waitFor(async () => { await act(async () => undefined); expect(element.textContent).toContain('main.pdf'); });
  expect(element.querySelector(`section[aria-label="${title}"]`)).not.toBeNull();
  expect(element.querySelector('section[aria-label="files"]')).toBeNull();
  expect(fetch).toHaveBeenCalledTimes(1);
  expect(fetch.mock.calls[0][0]).toBe('/api/document/documents/doc-1/files');
  expect(fetch.mock.calls[0][1].headers).toEqual({ 'X-NewP-Feature': 'document-read' });
});
