// @vitest-environment jsdom
import { act } from 'react';
import { createLocalizedRoot as createRoot } from './localizedTestRoot';
import { createMemoryRouter, Outlet, RouterProvider } from 'react-router-dom';
import { expect, it, vi } from 'vitest';
import { ModuleRegistry } from '@icp-platform/sdk';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { EntryNavigationProvider } from '../navigation/EntryNavigationContext';
import { PageEntry } from '../composition/PageOutlet';
import { useStandardCrudActionExecution } from '../standard-crud/useStandardCrudActionExecution';

it('resolves a declared entry and passes selection without refreshing the source or replacing canonical inputs', async () => {
  const refresh = vi.fn();
  function Source() {
    const action = useStandardCrudActionExecution({ refresh });
    return <button onClick={() => void action.execute({ key: 'open', context: {}, handler: () => ({ operation: 'action', payload: null,
      navigation: { entryKey: 'entry', parameters: { roleId: 'selected', featurePosition: 'borrowed' } } }) })}>open</button>;
  }
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'test', displayName: 'test', version: '1' }, pages: [{ pageKey: 'target',
    load: async () => ({ default: ({ input }) => <p>{String(input.parameters.roleId)}:{String(input.parameters.featurePosition)}</p> }) }] }], 'test');
  const page = { pageKey: 'target', parameters: { featurePosition: 'canonical' } };
  const router = createMemoryRouter([{ element: <RuntimeMetadataProvider value={{ manifest: { modules: [] }, modelDocuments: [], viewDocuments: [],
    navDocuments: [{ type: 'nav', navigation: { items: [{ key: 'entry', path: '/declared', page }] } }] }}>
    <EntryNavigationProvider><Outlet /></EntryNavigationProvider></RuntimeMetadataProvider>, children: [
    { path: '/', element: <Source /> }, { path: '/declared', element: <PageEntry page={page} /> },
  ] }]);
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(<RouterProvider router={router} />));
    await act(async () => container.querySelector('button')!.click());
    expect(router.state.location.pathname).toBe('/declared');
    expect(container.textContent).toContain('selected:canonical');
    expect(refresh).not.toHaveBeenCalled();
  } finally { await act(async () => root.unmount()); router.dispose(); container.remove(); }
});
