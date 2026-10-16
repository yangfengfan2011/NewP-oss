// @vitest-environment jsdom
import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, expect, it, vi } from 'vitest';
import { LocalizationProvider, platformFetch } from '@icp-platform/core';
import type { StandardCrudActionInstance } from '@icp-platform/sdk';
import WorkspaceObjectCopyInput from '../WorkspaceObjectCopyInput';
import { workspaceClipboard, type ObjectCopyDraft } from '../workspaceClipboard';
import workspaceDictionary from '../../metadata/dictionaries/Workspace.dic.json';
import partDictionary from '../../../part/metadata/dictionaries/Part.dic.json';

vi.mock('@icp-platform/core', async original => ({ ...await original<typeof import('@icp-platform/core')>(), platformFetch: vi.fn() }));
const roots: { root: Root; element: HTMLElement }[] = [];
const source = { spaceId: 'public', rootFolderId: 'root', linkIds: ['source'] };
const target = { spaceId: 'public', rootFolderId: 'root', linkIds: '["target"]' };
const catalogs = { zhs: Object.fromEntries([
  ...Object.entries(workspaceDictionary.Workspace).map(([key, value]) => [`Workspace.${key}`, value.zhs]),
  ...Object.entries(partDictionary.Part).map(([key, value]) => [`Part.${key}`, value.zhs]),
]) };
afterEach(async () => { for (const { root, element } of roots.splice(0)) { await act(async () => root.unmount()); element.remove(); } vi.resetAllMocks(); });

async function mount(preparation?: Promise<Response>) {
  const entries = new Map<symbol, unknown>();
  let active = true;
  const instance: StandardCrudActionInstance = { get active() { return active; }, changed: vi.fn(), getOrCreate: (key, create) => {
    if (!entries.has(key)) entries.set(key, create()); return entries.get(key) as ReturnType<typeof create>;
  } };
  const session = workspaceClipboard(instance);
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ items: [{ linkId: 'part', modelKey: 'Part' }] })));
  await session.copy(source, ['part'], 'copy');
  vi.mocked(platformFetch).mockImplementation(async () => preparation ?? new Response(JSON.stringify({ operationId: 'first', values: { ECODE: '', ENAME: '原名称', Description: '原说明' }, sourcePath: ['公共空间', '源'], targetPath: ['公共空间', '目标'] })));
  let draft: { draft: ObjectCopyDraft; values: Record<string, unknown> } | undefined;
  const draftState = vi.fn();
  function Harness({ scope = target }: { scope?: Record<string, unknown> }) {
    const [value, change] = useState<unknown>(); draft = value as typeof draft;
    const [ready, setReady] = useState(false);
    return <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={catalogs}>
      <WorkspaceObjectCopyInput instance={instance} action={{ key: 'pasteObjectCopy', labelKey: 'Workspace.PasteObjectCopy' }}
        featurePosition="object-copy" queryScope={scope} value={value} onChange={change} onDraftStateChange={state => { draftState(state); setReady(state.ready); }} />
      <button disabled={!ready} onClick={() => { void session.pasteObjectCopy(draft!.draft, draft!.values, { ...source, linkIds: ['target'] }, 'object-copy'); }}>提交</button>
    </LocalizationProvider>;
  }
  const element = document.createElement('div'); document.body.append(element); const root = createRoot(element); roots.push({ element, root });
  await act(async () => root.render(<Harness />));
  return { element, root, session, instance, Harness, draftState, draft: () => draft!, end: () => { active = false; session.close(); } };
}
async function fill(element: HTMLElement, label: string, value: string) {
  const input = [...element.querySelectorAll('input,textarea')].find(node => node.getAttribute('aria-label') === label
    || element.querySelector(`label[for="${node.id}"]`)?.textContent?.includes(label)) as HTMLInputElement | HTMLTextAreaElement;
  expect(input).toBeDefined();
  const prototype = input.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  await act(async () => { Object.getOwnPropertyDescriptor(prototype, 'value')!.set!.call(input, value); input.dispatchEvent(new Event('input', { bubbles: true })); });
}

it('requires a new code, shows the verified paths and preserves user edits while reconfirming changed source defaults', async () => {
  const view = await mount();
  expect(view.draft().values).toEqual({ ECODE: '', ENAME: '原名称', Description: '原说明' });
  expect(view.draftState).toHaveBeenLastCalledWith({ dirty: false, ready: false });
  expect(view.element.textContent).toContain('公共空间 / 源'); expect(view.element.textContent).toContain('公共空间 / 目标');
  await fill(view.element, '编码', 'NEW'); await fill(view.element, '名称', '主动编辑');
  expect(view.draftState).toHaveBeenLastCalledWith({ dirty: true, ready: true });
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ operationId: 'second', values: { ECODE: '', ENAME: '后台新名称', Description: '后台新说明' }, sourcePath: ['公共空间', '源'], targetPath: ['公共空间', '目标'] })));
  await act(async () => [...view.element.querySelectorAll('button')].find(button => button.textContent === '重新核对来源')!.click());
  expect(view.draft().values).toEqual({ ECODE: 'NEW', ENAME: '主动编辑', Description: '后台新说明' });
});

it('invalidates the old draft and clears editable inputs when the target or subject changes', async () => {
  const view = await mount(); await fill(view.element, '编码', 'OLD-TARGET');
  const old = view.draft();
  await act(async () => view.root.render(<view.Harness scope={{ ...target, linkIds: '["other"]' }} />));
  expect(view.draft().values.ECODE).toBe('');
  await expect(view.session.pasteObjectCopy(old.draft, old.values, { ...source, linkIds: ['target'] }, 'object-copy')).rejects.toThrow();
  await act(async () => { view.end(); view.root.render(<view.Harness />); });
  expect(view.element.textContent).toContain('来源、目标或账号已变化');
});

it('does not expose editable fields until the server preparation is loaded', async () => {
  let resolve!: (response: Response) => void;
  const preparation = new Promise<Response>(done => { resolve = done; });
  const view = await mount(preparation);
  expect(view.element.querySelector('input')).toBeNull();
  expect(view.draftState).toHaveBeenLastCalledWith({ dirty: false, ready: false });
  await act(async () => resolve(new Response(JSON.stringify({ operationId: 'loaded', values: { ECODE: '', ENAME: '来源', Description: null }, sourcePath: ['源'], targetPath: ['目标'] }))));
  await fill(view.element, '编码', 'AFTER-LOAD');
  expect(view.draft().values.ECODE).toBe('AFTER-LOAD');
  expect(view.draftState).toHaveBeenLastCalledWith({ dirty: true, ready: true });
});
it('enables submit after preparation and edits, and submits exactly the current values once', async () => {
  let resolve!: (response: Response) => void;
  const view = await mount(new Promise<Response>(done => { resolve = done; }));
  const submit = [...view.element.querySelectorAll('button')].find(button => button.textContent === '提交')!;
  expect(submit.disabled).toBe(true);
  await act(async () => resolve(new Response(JSON.stringify({ operationId: 'submit-ready', values: { ECODE: '', ENAME: '源预填', Description: null }, sourcePath: ['源'], targetPath: ['目标'] }))));
  expect(submit.disabled).toBe(true);
  await fill(view.element, '编码', 'NEW-CODE'); await fill(view.element, '名称', '主动名称');
  expect(submit.disabled).toBe(false);
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ succeeded: true, state: 'committed' })));
  await act(async () => submit.click());
  const writes = vi.mocked(platformFetch).mock.calls.filter(([url]) => String(url).endsWith('/objects/copy'));
  expect(writes).toHaveLength(1);
  expect(JSON.parse(String(writes[0][1]?.body))).toEqual({ operationId: 'submit-ready', values: { ECODE: 'NEW-CODE', ENAME: '主动名称', Description: null } });
  expect(submit.disabled).toBe(true);
});

it('keeps failed preparation non-submittable and aborts closed preparation without late writes', async () => {
  let reject!: (reason: Error) => void;
  const failed = await mount(new Promise<Response>((_, done) => { reject = done; }));
  await act(async () => reject(new Error('prepare unavailable')));
  expect(failed.element.querySelector('input')).toBeNull();
  const submit = [...failed.element.querySelectorAll('button')].find(button => button.textContent === '提交')!;
  expect(submit.disabled).toBe(true); await act(async () => submit.click());
  expect(vi.mocked(platformFetch).mock.calls.some(([url]) => String(url).endsWith('/objects/copy'))).toBe(false);
  let resolve!: (response: Response) => void;
  const cancelled = await mount(new Promise<Response>(done => { resolve = done; }));
  const prepare = [...vi.mocked(platformFetch).mock.calls].reverse().find(([url]) => String(url).endsWith('/objects/copy/prepare'))!;
  await act(async () => cancelled.root.render(null));
  expect(prepare[1]?.signal?.aborted).toBe(true);
  await act(async () => resolve(new Response(JSON.stringify({ operationId: 'late', values: { ECODE: '', ENAME: '不显示', Description: null }, sourcePath: ['源'], targetPath: ['目标'] }))));
  expect(cancelled.element.querySelector('input')).toBeNull();
  expect(vi.mocked(platformFetch).mock.calls.some(([url]) => String(url).endsWith('/objects/copy'))).toBe(false);
});