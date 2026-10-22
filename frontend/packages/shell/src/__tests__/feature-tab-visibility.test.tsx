import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, it, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry, type PageProps } from '@icp-platform/sdk';
import { PageEntry } from '../composition/PageOutlet';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';

it('默认空容器不加载后代，选择可用内容，刷新授权后移除旧内容', async () => {
  globalThis.IS_REACT_ACT_ENVIRONMENT = true;
  const reads = vi.fn();
  function Content({ input }: PageProps) { reads(input.parameters.name); return <p data-content={String(input.parameters.name)}>{String(input.parameters.name)}</p>; }
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'test', displayName: 'test', version: '1' }, pages: [{ pageKey: 'content', load: async () => ({ default: Content }) }] }], 'feature-tabs');
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  const router = createMemoryRouter([{ path: '/', element: <PageEntry page={{ pageKey: 'platform.tabs', parameters: { viewKey: 'outer', featurePosition: 'root' } }} /> }]);
  const features = [
    { key: 'root', position: 'root', kind: 'navigation', container: true },
    { key: 'A', position: 'A', parentPosition: 'root', kind: 'tab', container: true },
    { key: 'B', position: 'B', parentPosition: 'root', kind: 'tab', container: false },
    { key: 'X', position: 'X', parentPosition: 'A', kind: 'tab', container: false },
  ];
  const content = (name: string) => ({ pageKey: 'content', parameters: { name } });
  const render = async (grants: string[]) => act(async () => root.render(
    <AuthorizationProvider permissions={[]} featurePositions={grants}>
      <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}>
        <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'test', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features }] }, navDocuments: [], modelDocuments: [], viewDocuments: [
          { type: 'view', view: { key: 'outer', mode: 'tab', dicKey: 'test', defaultTabKey: 'A', tabs: [
            { key: 'A', titleKey: 'A', pageRef: { pageKey: 'platform.tabs', parameters: { viewKey: 'inner' } } }, { key: 'B', titleKey: 'B', pageRef: content('B') }] } },
          { type: 'view', view: { key: 'inner', mode: 'tab', dicKey: 'test', defaultTabKey: 'X', tabs: [{ key: 'X', titleKey: 'X', pageRef: content('X') }] } },
        ] }}><RouterProvider router={router} /></RuntimeMetadataProvider>
      </LocalizationProvider>
    </AuthorizationProvider>,
  ));
  try {
    await import('../composition/StandardTabPage');
    await render(['root', 'A', 'B']);
    expect(container.querySelector('[role=tab][aria-selected=true]')?.textContent).toBe('B');
    expect(container.querySelector('[data-content=B]')).not.toBeNull();
    expect(reads).not.toHaveBeenCalledWith('X');
    await render(['root', 'A', 'X']);
    expect(container.querySelector('[data-content=B]')).toBeNull();
    expect(container.querySelector('[data-content=X]')).not.toBeNull();
  } finally { await act(async () => root.unmount()); router.dispose(); container.remove(); ModuleRegistry.replaceAll([], 'feature-tabs-cleanup'); }
});
