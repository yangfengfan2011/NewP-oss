// @vitest-environment jsdom
import { act, useState } from 'react';
import { expect, it, vi } from 'vitest';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';
import * as core from '@icp-platform/core';
import { createLocalizedRoot as createRoot } from './localizedTestRoot';
import { loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import { StandardCrudActionReviewDialog } from '../standard-crud/StandardCrudActionReviewDialog';
import { ActionInstanceScope } from '../composition/ActionInstanceScope';

vi.unmock('@icp-platform/core');
const { default: resource } = await loadComponentSource<{ default: StandardCrudResourceRegistration }>('Workspace', 'workspaceRootResource.ts');
await loadComponentSource('Workspace', 'WorkspaceFolderCopyInput.tsx');
const source = { spaceId: 'public', rootFolderId: 'root', linkIds: '[]' };
const target = { ...source, linkIds: '["target-link"]' };
const dictionary = readComponentJson('Workspace', 'metadata/dictionaries/Workspace.dic.json').Workspace as Record<string, { zhs: string }>;
const catalogs = { zhs: Object.fromEntries(Object.entries(dictionary).map(([key, value]) => [`Workspace.${key}`, value.zhs])) };
const copyInput = (context: import('@icp-platform/sdk').StandardCrudActionInputRenderContext) =>
  <core.LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={catalogs}>
    {resource.actionInputRenderers!.copyFolder(context)}
  </core.LocalizationProvider>;

it.each(['position', 'snapshot', 'account'] as const)('invalidates the real named copy input on %s changes without sending a stale write', async change => {
  const request = vi.spyOn(core, 'platformFetch').mockImplementation(async path => new Response(JSON.stringify(String(path).endsWith('/prepare')
    ? { sourceName: '来源', sourcePath: ['公共空间', '来源'], targetPath: ['公共空间', '目标'] }
    : { items: [{ linkId: 'source-link', modelKey: 'WorkspaceFolder' }] })));
  const instance = new ActionInstanceScope(); const dispose = instance.activate(); const cancel = vi.fn(); let draft: unknown;
  await resource.actionHandlers!.copy({ instance, id: 'source-link', queryScope: source, featurePosition: 'copy' });
  function Harness({ scope = target }: { scope?: Record<string, unknown> }) {
    const [value, setValue] = useState<unknown>(); draft = value;
    return <StandardCrudActionReviewDialog instance={instance} action={{ key: 'pasteFolderCopy', labelKey: '粘贴文件夹副本' }} title="粘贴文件夹副本"
      featurePosition="named-copy" queryScope={scope} value={value} onChange={setValue} inputRenderer={copyInput}
      pending={false} onCancel={cancel} onConfirm={() => undefined} />;
  }
  const element = document.createElement('div'); document.body.append(element); const root = createRoot(element);
  try {
    await act(async () => root.render(<Harness />));
    expect(document.querySelector('#workspace-copy-name')).not.toBeNull(); const old = draft as Record<string, unknown>;
    expect(document.querySelector<HTMLInputElement>('#workspace-copy-name')!.value).toBe('来源');
    expect(document.body.textContent).toContain('公共空间 / 来源'); expect(document.body.textContent).toContain('公共空间 / 目标');
    expect(document.body.textContent).toContain('提交时会重新读取来源内容');
    if (change === 'position') await act(async () => root.render(<Harness scope={{ ...target, linkIds: '["elsewhere"]' }} />));
    else if (change === 'snapshot') await act(async () => resource.actionHandlers!.copy({ instance, id: 'source-link', queryScope: source, featurePosition: 'copy' }));
    else await act(async () => dispose());
    if (change !== 'position') expect(document.querySelector<HTMLInputElement>('#workspace-copy-name')!.disabled).toBe(true);
    await expect(resource.actionHandlers!.pasteFolderCopy({ instance, input: { ...old, name: 'stale' }, queryScope: target, featurePosition: 'named-copy' })).rejects.toThrow();
    expect(request.mock.calls.filter(([path]) => String(path).endsWith('/folders/copy'))).toHaveLength(0);
  } finally { await act(async () => root.unmount()); element.remove(); dispose(); request.mockRestore(); }
});

it('locks the actual input while a named copy is pending and lets its late success settle once', async () => {
  const request = vi.spyOn(core, 'platformFetch').mockResolvedValueOnce(new Response(JSON.stringify({ items: [{ linkId: 'source-link', modelKey: 'WorkspaceFolder' }] })))
    .mockResolvedValueOnce(new Response(JSON.stringify({ sourceName: '来源', sourcePath: ['公共空间', '来源'], targetPath: ['公共空间', '目标'] })));
  const instance = new ActionInstanceScope(); const dispose = instance.activate(); let draft: unknown;
  await resource.actionHandlers!.copy({ instance, id: 'source-link', queryScope: source, featurePosition: 'copy' });
  function Harness({ pending }: { pending: boolean }) {
    const [value, setValue] = useState<unknown>(); draft = value;
    return <StandardCrudActionReviewDialog instance={instance} action={{ key: 'pasteFolderCopy', labelKey: '粘贴文件夹副本' }} title="粘贴文件夹副本"
      featurePosition="named-copy" queryScope={target} value={value} onChange={setValue} inputRenderer={copyInput}
      pending={pending} onCancel={() => undefined} onConfirm={() => undefined} />;
  }
  const element = document.createElement('div'); document.body.append(element); const root = createRoot(element);
  try {
    await act(async () => root.render(<Harness pending={false} />));
    let finish!: (response: Response) => void; request.mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
    const input = { ...(draft as Record<string, unknown>), name: 'Named copy' };
    let write: unknown;
    await act(async () => { write = resource.actionHandlers!.pasteFolderCopy({ instance, input, queryScope: target, featurePosition: 'named-copy' }); });
    await act(async () => root.render(<Harness pending />));
    expect(document.querySelector<HTMLInputElement>('#workspace-copy-name')!.disabled).toBe(true);
    await expect(resource.actionHandlers!.pasteFolderCopy({ instance, input, queryScope: target, featurePosition: 'named-copy' })).rejects.toThrow();
    await act(async () => { finish(new Response('{"succeeded":true}')); await write; });
    expect(request.mock.calls.filter(([path]) => String(path).endsWith('/folders/copy'))).toHaveLength(1);
  } finally { await act(async () => root.unmount()); element.remove(); dispose(); request.mockRestore(); }
});
