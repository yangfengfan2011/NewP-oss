// @vitest-environment jsdom
import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, expect, it, vi } from 'vitest';
import { LocalizationProvider, platformFetch } from '@icp-platform/core';
import type { ActionDataAccessRequirement, StandardCrudActionInstance } from '@icp-platform/sdk';
import resource from '../workspaceRootResource';
import WorkspaceFolderCopyInput from '../WorkspaceFolderCopyInput';
import WorkspaceObjectEditInput from '../WorkspaceObjectEditInput';
import { workspaceClipboard } from '../workspaceClipboard';
import workspaceDictionary from '../../metadata/dictionaries/Workspace.dic.json';
import folderList from '../../metadata/views/WorkspaceFolderList.view.json';
import compiledWorkspace from '../../../../../../tests/Kernel.Tests/Fixtures/WorkspaceClipboardDataAccess.contract.json';

// 与既有模块组件测试一致，冷转换在收集期完成，交互断言只观察真实挂载结果。
await import('../WorkspaceFolderCreateInput');

vi.mock('@icp-platform/core', async original => ({ ...await original<typeof import('@icp-platform/core')>(), platformFetch: vi.fn() }));
const roots: { root: Root; element: HTMLElement }[] = [];
const target = { spaceId: 'public', rootFolderId: 'root', linkIds: '["target"]' };
const catalogs = { zhs: Object.fromEntries(Object.entries(workspaceDictionary.Workspace).map(([key, value]) => [`Workspace.${key}`, value.zhs])) };
afterEach(async () => { for (const { root, element } of roots.splice(0)) { await act(async () => root.unmount()); element.remove(); } vi.resetAllMocks(); });

function host() {
  const element = document.createElement('div'); document.body.append(element);
  const root = createRoot(element); roots.push({ root, element }); return { root, element };
}
function localized(content: React.ReactNode) {
  return <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={catalogs}>{content}</LocalizationProvider>;
}
async function fill(element: HTMLElement, value: string) {
  const input = element.querySelector<HTMLInputElement>('input[type="text"]')!;
  await act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, value); input.dispatchEvent(new Event('input', { bubbles: true })); });
}
function lastBody() {
  const calls = vi.mocked(platformFetch).mock.calls;
  return JSON.parse(String(calls[calls.length - 1][1]?.body));
}
function createContext() {
  const dataAccess = (compiledWorkspace.declarations as ActionDataAccessRequirement[]).find(item =>
    item.execution === 'workspace.folder.create' && item.surface === 'pageActions' && item.actionKey === 'create');
  if (!dataAccess) throw new Error('正式Workspace创建fixture缺失');
  const featurePosition = dataAccess.featurePositions.find(position => position.endsWith('/surface:list.pageActions/action:create'));
  if (!featurePosition) throw new Error('正式Workspace创建位置缺失');
  return { featurePosition, dataAccess, action: { key: 'create', labelKey: 'Workspace.CreateFolder', dataAccess } };
}

it('keeps the actual create binding and option outside model fields, rejecting an unbound adapter', async () => {
  const action = folderList.view.pageActions.find(item => item.key === 'create')!;
  expect(action).toMatchObject({ key: 'create', handlerKey: 'create', inputKey: 'createFolder' });
  const renderer = resource.actionInputRenderers!.createFolder;
  expect(renderer).toBeTypeOf('function');
  let input: unknown;
  function Harness() {
    const [value, change] = useState<unknown>(); input = value;
    return localized(renderer({ action: { key: 'create', labelKey: 'Workspace.CreateFolder' }, queryScope: target, value, onChange: change }));
  }
  const view = host();
  await act(async () => view.root.render(<Harness />));
  const checkbox = view.element.querySelector<HTMLInputElement>('input[type="checkbox"]')!;
  expect(checkbox.checked).toBe(true);
  await fill(view.element, '新文件夹');
  vi.mocked(platformFetch).mockImplementation(async () => new Response('{"succeeded":true}'));
  const create = resource.actionHandlers![action.handlerKey!];
  const context = createContext();
  const result = await create({ input, queryScope: target, ...context });
  expect(platformFetch).toHaveBeenLastCalledWith('/api/workspace/content/folders', expect.objectContaining({ headers: expect.objectContaining({
    'X-NewP-Feature': context.featurePosition, 'X-NewP-Data-Declaration': context.dataAccess.id,
  }) }));
  expect(result).toMatchObject({ change: { operation: 'create', id: '' } });
  let body = lastBody();
  expect(body).toEqual({ position: { spaceId: 'public', rootFolderId: 'root', linkIds: ['target'] }, values: { ENAME: '新文件夹' }, copyParentAuthorization: true });
  await act(async () => checkbox.click());
  await create({ input, queryScope: target, ...context });
  body = lastBody();
  expect(body.copyParentAuthorization).toBe(false);
  expect(body.values).not.toHaveProperty('copyParentAuthorization');
  await expect(resource.dataAdapter.create!({ WorkspacePosition: body.position, ENAME: '旧入口' })).rejects.toThrow('创建声明');
  await resource.dataAdapter.create!({ WorkspacePosition: body.position, ENAME: '旧入口' }, undefined, context);
  body = lastBody();
  expect(body).toEqual({ position: { spaceId: 'public', rootFolderId: 'root', linkIds: ['target'] }, values: { ENAME: '旧入口' } });
});

it('explains the space-root boundary and submits no parent initialization there', async () => {
  const renderer = resource.actionInputRenderers!.createFolder;
  expect(renderer).toBeTypeOf('function');
  let input: unknown;
  const rootScope = { ...target, linkIds: '[]' };
  function Harness() {
    const [value, change] = useState<unknown>(); input = value;
    return localized(renderer({ action: { key: 'create', labelKey: 'Workspace.CreateFolder' }, queryScope: rootScope, value, onChange: change }));
  }
  const view = host();
  await act(async () => view.root.render(<Harness />));
  expect(view.element.textContent).toContain('空间根没有普通父文件夹授权可复制');
  const checkbox = view.element.querySelector<HTMLInputElement>('input[type="checkbox"]');
  if (checkbox) expect(checkbox.disabled).toBe(true);
  await fill(view.element, '根下文件夹');
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response('{"succeeded":true}'));
  const action = folderList.view.pageActions.find(item => item.key === 'create')!;
  await resource.actionHandlers![action.handlerKey!]({ input, queryScope: rootScope, ...createContext() });
  expect(JSON.parse(String(vi.mocked(platformFetch).mock.calls[0][1]?.body)).copyParentAuthorization).toBe(false);
});

it.each(['pasteFolderCopy', 'pasteFolderStructure'] as const)('uses the existing %s input and single submit for the default and opted-out parent choice', async actionKey => {
  const entries = new Map<symbol, unknown>();
  const instance: StandardCrudActionInstance = { active: true, changed: vi.fn(), getOrCreate: (key, create) => {
    if (!entries.has(key)) entries.set(key, create()); return entries.get(key) as ReturnType<typeof create>;
  } };
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response('{"items":[{"linkId":"source","modelKey":"WorkspaceFolder"}]}'));
  await workspaceClipboard(instance).copy({ spaceId: 'public', rootFolderId: 'root', linkIds: [] }, ['source'], 'copy');
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response('{"sourceName":"来源","sourcePath":["公共空间","来源"],"targetPath":["公共空间","目标"]}'));
  let input: unknown;
  function Harness() {
    const [value, change] = useState<unknown>(); input = value;
    return localized(<WorkspaceFolderCopyInput instance={instance} action={{ key: actionKey, labelKey: 'Workspace.PasteFolderCopy' }}
      featurePosition="named-copy" queryScope={target} value={value} onChange={change} />);
  }
  const view = host();
  await act(async () => view.root.render(<Harness />));
  const checkbox = view.element.querySelector<HTMLInputElement>('input[type="checkbox"]')!;
  expect(checkbox).not.toBeNull(); expect(checkbox.checked).toBe(true);
  if (actionKey === 'pasteFolderStructure') expect(view.element.textContent).toContain('新根从目标父文件夹初始化');
  await act(async () => checkbox.click());
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response('{"succeeded":true,"noChange":false}'));
  await act(async () => { await resource.actionHandlers![actionKey]({ instance, input, queryScope: target, featurePosition: 'named-copy' }); });
  const writes = vi.mocked(platformFetch).mock.calls.filter(([path]) => String(path).endsWith(actionKey === 'pasteFolderCopy' ? '/folders/copy' : '/folders/copy-structure'));
  expect(writes).toHaveLength(1);
  expect(JSON.parse(String(writes[0][1]?.body))).toEqual({ mode: 'copy', sourcePosition: { spaceId: 'public', rootFolderId: 'root', linkIds: [] },
    linkIds: ['source'], targetPosition: { spaceId: 'public', rootFolderId: 'root', linkIds: ['target'] }, name: '来源', copyParentAuthorization: false });
});


it('discards edit preparation when the current declaration disappears and never publishes the old fields', async () => {
  const declaration = (compiledWorkspace.declarations as ActionDataAccessRequirement[]).find(item => item.execution === 'workspace.content.edit')!;
  let finish!: (response: Response) => void;
  let signal: AbortSignal | undefined;
  vi.mocked(platformFetch).mockImplementation((_url, init) => { signal = init?.signal as AbortSignal; return new Promise(resolve => { finish = resolve; }); });
  const change = vi.fn(); const draft = vi.fn(); const view = host();
  const render = (dataAccess?: ActionDataAccessRequirement) => localized(<WorkspaceObjectEditInput
    row={{ LID: 'edit-link', ObjectModelKey: 'WorkspaceFolder' }} queryScope={target}
    featurePosition={declaration.featurePositions[0]} action={{ key: 'edit', labelKey: 'Edit', dataAccess }}
    value={null} onChange={change} onDraftStateChange={draft} />);
  await act(async () => view.root.render(render(declaration)));
  expect(vi.mocked(platformFetch)).toHaveBeenCalledTimes(1);
  expect(new Headers(vi.mocked(platformFetch).mock.calls[0][1]?.headers).get('X-NewP-Data-Declaration')).toBe(declaration.id);
  await act(async () => view.root.render(render()));
  expect(signal?.aborted).toBe(true);
  await act(async () => finish(new Response(JSON.stringify({ fields: { ENAME: 'stale-edit', Description: 'must not publish' } }))));
  expect(change).not.toHaveBeenCalled();
  expect(view.element.querySelector('[role="alert"]')).not.toBeNull();
  expect(view.element.textContent).not.toContain('stale-edit');
  expect(draft).toHaveBeenLastCalledWith({ dirty: false, ready: false });
  expect(vi.mocked(platformFetch)).toHaveBeenCalledTimes(1);
});
