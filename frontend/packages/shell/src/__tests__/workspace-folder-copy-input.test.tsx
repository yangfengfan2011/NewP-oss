// @vitest-environment jsdom
import { act, useState } from 'react';
import { expect, it, vi } from 'vitest';
import type { ActionDataAccessRequirement, StandardCrudResourceRegistration } from '@icp-platform/sdk';
import * as core from '@icp-platform/core';
import { createLocalizedRoot as createRoot } from './localizedTestRoot';
import { loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import { StandardCrudActionReviewDialog } from '../standard-crud/StandardCrudActionReviewDialog';
import { ActionInstanceScope } from '../composition/ActionInstanceScope';

vi.unmock('@icp-platform/core');
const { default: resource } = await loadComponentSource<{ default: StandardCrudResourceRegistration }>('Workspace', 'workspaceRootResource.ts');
await loadComponentSource('Workspace', 'WorkspaceFolderCopyInput.tsx');
import compiled from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceClipboardDataAccess.contract.json';
function copyContext(actionKey: string) {
  const dataAccess = (compiled.declarations as ActionDataAccessRequirement[]).find(item => item.viewKey === 'workspace.folder.list' && item.surface === 'pageActions' && item.actionKey === actionKey)!;
  return { action: { key: actionKey, labelKey: '粘贴文件夹副本', dataAccess }, featurePosition: dataAccess.featurePositions.find(position => position.includes('/surface:list.pageActions/'))! };
}
// 共享选择使用真实行复制按钮；副本输入继续消费自己的独立旧来源。
function selectionContext() {
  const declaration = (compiled.declarations as ActionDataAccessRequirement[]).find(item => item.viewKey === 'workspace.folder.list' && item.surface === 'rowActions' && item.actionKey === 'copy')!;
  const action = readComponentJson('Workspace', 'metadata/views/WorkspaceFolderList.view.json').view.rowActions.find((item: { key: string }) => item.key === 'copy')!;
  return { action: { ...action, buttonId: declaration.id }, buttonId: declaration.id,
    featurePosition: declaration.featurePositions.find(position => position.includes('/surface:list.rowActions/'))! };
}
const source = { spaceId: 'public', rootFolderId: 'root', linkIds: '[]' };
const target = { ...source, linkIds: '["target-link"]' };
const dictionary = readComponentJson('Workspace', 'metadata/dictionaries/Workspace.dic.json').Workspace as Record<string, { zhs: string }>;
const catalogs = { zhs: Object.fromEntries(Object.entries(dictionary).map(([key, value]) => [`Workspace.${key}`, value.zhs])) };
const copyInput = (context: import('@icp-platform/sdk').StandardCrudActionInputRenderContext) =>
  <core.LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={catalogs}>
    {resource.actionInputRenderers!.copyFolder(context)}
  </core.LocalizationProvider>;

it.each((['pasteFolderCopy', 'pasteFolderStructure'] as const).flatMap(action => (['position', 'snapshot', 'account'] as const).map(change => [action, change] as const)))('invalidates the real named copy input for %s on %s changes without sending a stale write', async (actionKey, change) => {
  const request = vi.spyOn(core, 'platformFetch').mockImplementation(async path => new Response(JSON.stringify(String(path).endsWith('/prepare')
    ? { sourceName: '来源', sourcePath: ['公共空间', '来源'], targetPath: ['公共空间', '目标'] }
    : { items: [{ linkId: 'source-link', modelKey: 'WorkspaceFolder' }] })));
  const instance = new ActionInstanceScope(); const dispose = instance.activate(); const cancel = vi.fn(); let draft: unknown;
  await resource.actionHandlers!.copy({ instance, id: 'source-link', queryScope: source, ...selectionContext() });
  function Harness({ scope = target }: { scope?: Record<string, unknown> }) {
    const [value, setValue] = useState<unknown>(); draft = value;
    return <StandardCrudActionReviewDialog instance={instance} {...copyContext(actionKey)} title="粘贴文件夹副本"
      queryScope={scope} value={value} onChange={setValue} inputRenderer={copyInput}
      pending={false} onCancel={cancel} onConfirm={() => undefined} />;
  }
  const element = document.createElement('div'); document.body.append(element); const root = createRoot(element);
  try {
    await act(async () => root.render(<Harness />));
    expect(document.querySelector('#workspace-copy-name')).not.toBeNull(); const old = draft as Record<string, unknown>;
    expect(document.querySelector<HTMLInputElement>('#workspace-copy-name')!.value).toBe('来源');
    expect(document.body.textContent).toContain('公共空间 / 来源'); expect(document.body.textContent).toContain('公共空间 / 目标');
    expect(document.body.textContent).toContain('提交时会重新读取来源内容');
    expect(document.body.textContent).toContain(actionKey === 'pasteFolderStructure' ? '仅复制文件夹，保留副本内部共享关系' : '全部直接子内容仍共享原对象');
    if (change === 'position') await act(async () => root.render(<Harness scope={{ ...target, linkIds: '["elsewhere"]' }} />));
    else if (change === 'snapshot') await act(async () => resource.actionHandlers!.copy({ instance, id: 'source-link', queryScope: source, ...selectionContext() }));
    else await act(async () => dispose());
    if (change !== 'position') expect(document.querySelector<HTMLInputElement>('#workspace-copy-name')!.disabled).toBe(true);
    await expect(resource.actionHandlers![actionKey]({ instance, input: { ...old, name: 'stale' }, queryScope: target, ...copyContext(actionKey) })).rejects.toThrow();
    expect(request.mock.calls.filter(([path]) => String(path).endsWith(actionKey === 'pasteFolderStructure' ? '/folders/copy-structure' : '/folders/copy'))).toHaveLength(0);
  } finally { await act(async () => root.unmount()); element.remove(); dispose(); request.mockRestore(); }
});

it.each(['pasteFolderCopy', 'pasteFolderStructure'] as const)('locks the actual %s input while pending and lets its late success settle once', async actionKey => {
  const request = vi.spyOn(core, 'platformFetch').mockResolvedValueOnce(new Response(JSON.stringify({ items: [{ linkId: 'source-link', modelKey: 'WorkspaceFolder' }] })))
    .mockResolvedValueOnce(new Response(JSON.stringify({ sourceName: '来源', sourcePath: ['公共空间', '来源'], targetPath: ['公共空间', '目标'] })));
  const instance = new ActionInstanceScope(); const dispose = instance.activate(); let draft: unknown;
  await resource.actionHandlers!.copy({ instance, id: 'source-link', queryScope: source, ...selectionContext() });
  function Harness({ pending }: { pending: boolean }) {
    const [value, setValue] = useState<unknown>(); draft = value;
    return <StandardCrudActionReviewDialog instance={instance} {...copyContext(actionKey)} title="粘贴文件夹副本"
      queryScope={target} value={value} onChange={setValue} inputRenderer={copyInput}
      pending={pending} onCancel={() => undefined} onConfirm={() => undefined} />;
  }
  const element = document.createElement('div'); document.body.append(element); const root = createRoot(element);
  try {
    await act(async () => root.render(<Harness pending={false} />));
    let finish!: (response: Response) => void; request.mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
    const input = { ...(draft as Record<string, unknown>), name: 'Named copy' };
    let write: unknown;
    await act(async () => { write = resource.actionHandlers![actionKey]({ instance, input, queryScope: target, ...copyContext(actionKey) }); });
    await act(async () => root.render(<Harness pending />));
    expect(document.querySelector<HTMLInputElement>('#workspace-copy-name')!.disabled).toBe(true);
    await expect(resource.actionHandlers![actionKey]({ instance, input, queryScope: target, ...copyContext(actionKey) })).rejects.toThrow();
    await act(async () => { finish(new Response('{"succeeded":true}')); await write; });
    expect(request.mock.calls.filter(([path]) => String(path).endsWith(actionKey === 'pasteFolderStructure' ? '/folders/copy-structure' : '/folders/copy'))).toHaveLength(1);
  } finally { await act(async () => root.unmount()); element.remove(); dispose(); request.mockRestore(); }
});
