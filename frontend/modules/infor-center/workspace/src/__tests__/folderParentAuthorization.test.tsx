// @vitest-environment jsdom
import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, expect, it, vi } from 'vitest';
import { LocalizationProvider, platformFetch } from '@icp-platform/core';
import type { StandardCrudActionInstance } from '@icp-platform/sdk';
import resource from '../workspaceRootResource';
import WorkspaceFolderCopyInput from '../WorkspaceFolderCopyInput';
import { workspaceClipboard } from '../workspaceClipboard';
import workspaceDictionary from '../../metadata/dictionaries/Workspace.dic.json';
import folderList from '../../metadata/views/WorkspaceFolderList.view.json';

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

it('keeps the create feature identity and sends the checked or opted-out choice outside model fields, preserving the old adapter', async () => {
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
  const result = await create({ input, queryScope: target, featurePosition: 'folder-create' });
  expect(result).toMatchObject({ change: { operation: 'create', id: '' } });
  let body = lastBody();
  expect(body).toEqual({ position: { spaceId: 'public', rootFolderId: 'root', linkIds: ['target'] }, values: { ENAME: '新文件夹' }, copyParentAuthorization: true });
  await act(async () => checkbox.click());
  await create({ input, queryScope: target, featurePosition: 'folder-create' });
  body = lastBody();
  expect(body.copyParentAuthorization).toBe(false);
  expect(body.values).not.toHaveProperty('copyParentAuthorization');
  await resource.dataAdapter.create!({ WorkspacePosition: body.position, ENAME: '旧入口' });
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
  await resource.actionHandlers![action.handlerKey!]({ input, queryScope: rootScope });
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
