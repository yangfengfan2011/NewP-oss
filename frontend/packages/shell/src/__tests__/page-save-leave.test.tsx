// @vitest-environment jsdom
import { act, useEffect } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, it, vi } from 'vitest';
import { ModuleRegistry, type PageProps } from '@icp-platform/sdk';
import { PageEntry } from '../composition/PageOutlet';

it('keeps failed saves on the current page and prevents duplicate save-and-leave', async () => {
  const save = vi.fn<[], Promise<boolean>>();
  let finish!: (value: boolean) => void;
  save.mockImplementation(() => new Promise(resolve => { finish = resolve; }));
  function Draft({ reportState }: PageProps) {
    useEffect(() => reportState({ dirty: true, submitting: false }, undefined, save), [reportState]);
    return <p>draft</p>;
  }
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'save', displayName: 'save', version: '1' },
    pages: [{ pageKey: 'save', load: async () => ({ default: Draft }) }] }], 'save');
  const router = createMemoryRouter([{ path: '/', element: <PageEntry page={{ pageKey: 'save' }} /> },
    { path: '/next', element: <p>next</p> }]);
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  const click = async () => {
    const button = [...document.querySelectorAll('button')].find(item => item.textContent?.replace(/\s/g, '') === '保存并离开');
    expect(button).toBeDefined();
    await act(async () => button!.click());
  };
  try {
    await act(async () => root.render(<RouterProvider router={router} />));
    await act(async () => router.navigate('/next'));
    await click(); await click();
    expect(save).toHaveBeenCalledTimes(1);
    await act(async () => finish(false));
    expect(router.state.location.pathname).toBe('/');
    expect(container.textContent).toContain('draft');
    await click();
    await act(async () => finish(true));
    expect(router.state.location.pathname).toBe('/next');
  } finally { await act(async () => root.unmount()); router.dispose(); container.remove(); }
});
