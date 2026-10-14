// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, expect, it, vi } from 'vitest';
import { LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { DocumentFilesPanel } from '../DocumentFilesPanel';

const roots: { root: Root; element: HTMLElement }[] = [];
const files = { documentId: 'doc-1', versionId: 'version-1', iterationId: 'iteration-1', downloadAllowed: true,
  files: [{ fileLinkId: 'link-1', role: 'main', ordinal: 0, fileName: 'main.pdf', length: 4, contentType: 'application/pdf' }] };
async function mount(downloadPosition?: string) {
  const element = document.createElement('div'); document.body.append(element); const root = createRoot(element); roots.push({ root, element });
  await act(async () => root.render(<LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{}}>
    <PlatformQueryProvider scopeKey="files-test"><DocumentFilesPanel documentId="doc-1" featurePosition="read-position" downloadPosition={downloadPosition} title="文件" /></PlatformQueryProvider>
  </LocalizationProvider>));
  return { root, element };
}
afterEach(async () => { for (const { root, element } of roots.splice(0)) { await act(async () => root.unmount()); element.remove(); } vi.restoreAllMocks(); vi.unstubAllGlobals(); });

it.each([false, true])('reads safe metadata using read position while the absent feature or denied data download stays disabled, dataAllowed=%s', async dataAllowed => {
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ ...files, downloadAllowed: dataAllowed })));
  vi.stubGlobal('fetch', fetch);
  const { element } = await mount(dataAllowed ? undefined : 'download-position');
  expect(element.textContent).toContain('main.pdf');
  expect(fetch).toHaveBeenCalledTimes(1);
  expect(fetch.mock.calls[0][0]).toBe('/api/document/documents/doc-1/files');
  expect(fetch.mock.calls[0][1].headers).toEqual({ 'X-NewP-Feature': 'read-position' });
  const button = [...element.querySelectorAll('button')].find(button => button.textContent === '下载')!;
  expect(button.disabled).toBe(true);
});

it('downloads through the actual Document link route with download feature and releases the response URL', async () => {
  const fetch = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(files))).mockResolvedValueOnce(new Response('file'));
  vi.stubGlobal('fetch', fetch);
  const createUrl = vi.fn().mockReturnValue('blob:controlled-response'); const revoke = vi.fn();
  Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: createUrl });
  Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: revoke });
  const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
  const { element } = await mount('download-position');
  const button = [...element.querySelectorAll('button')].find(button => button.textContent === '下载')!;
  await act(async () => button.click());
  expect(fetch).toHaveBeenCalledTimes(2);
  expect(fetch.mock.calls[1][0]).toBe('/api/document/documents/doc-1/files/link-1/content');
  expect(fetch.mock.calls[1][1].headers).toEqual({ 'X-NewP-Feature': 'download-position' });
  expect(click).toHaveBeenCalledTimes(1); expect(revoke).toHaveBeenCalledWith('blob:controlled-response');
});
