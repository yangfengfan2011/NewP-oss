// @vitest-environment jsdom
import { act, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, vi } from 'vitest';
import { LocalizationProvider, platformFetch } from '@icp-platform/core';
import { StandardCrudRequestError, type ActionDataAccessRequirement, type StandardCrudActionInstance } from '@icp-platform/sdk';
import resource from '../workspaceRootResource';
import WorkspaceObjectCopyInput from '../WorkspaceObjectCopyInput';
import { workspaceClipboard, type ObjectCopyDraft } from '../workspaceClipboard';
import workspaceDictionary from '../../metadata/dictionaries/Workspace.dic.json';
import documentDictionary from '../../../document/metadata/dictionaries/Document.dic.json';
import compiled from '../../../../../../tests/Kernel.Tests/Fixtures/WorkspaceClipboardDataAccess.contract.json';
const declaration = compiled.declarations.find(item => item.viewKey === 'workspace.folder.list' && item.surface === 'pageActions' && item.actionKey === 'pasteObjectCopy') as ActionDataAccessRequirement;

vi.mock('@icp-platform/core', async original => ({ ...await original<typeof import('@icp-platform/core')>(), platformFetch: vi.fn() }));

it('confirms every Document row, preserves row values and locates a later-row code error without replaying the batch', async () => {
  const source = { spaceId: 'public', rootFolderId: 'root', linkIds: ['source'] };
  const target = { ...source, linkIds: ['target'] };
  const values = { items: ['a', 'b'].map(sourceLinkId => ({ sourceLinkId, values: { ECODE: '', ENAME: '来源 ' + sourceLinkId, Description: '说明' } })) };
  const items = values.items.map(item => ({ ...item, sourcePath: ['公共空间', item.values.ENAME], fileCount: 2, totalBytes: 8 }));
  const entries = new Map<symbol, unknown>(); const instance: StandardCrudActionInstance = { active: true, changed: vi.fn(), getOrCreate: (key, create) => { if (!entries.has(key)) entries.set(key, create()); return entries.get(key) as ReturnType<typeof create>; } };
  const session = workspaceClipboard(instance);
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ items: ['a', 'b'].map(linkId => ({ linkId, modelKey: 'Document' })) })));
  await session.copy(source, ['a', 'b'], 'copy', 'copy-button');
  vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ operationId: 'batch', modelKey: 'Document', values, items, sourcePath: ['公共空间'], targetPath: ['公共空间', '目标'], fileCount: 4, totalBytes: 16 })));
  const draftState = vi.fn(); let draft: { draft: ObjectCopyDraft; values: Record<string, unknown> }; let pending: Promise<unknown> | undefined;
  function Harness({ currentInstance = instance }: { currentInstance?: StandardCrudActionInstance }) {
    const [value, change] = useState<unknown>(); draft = value as typeof draft;
    const [ready, setReady] = useState(false);
    const catalogs = { zhs: Object.fromEntries([...Object.entries(workspaceDictionary.Workspace).map(([key, entry]) => [`Workspace.${key}`, entry.zhs]), ...Object.entries(documentDictionary.Document).map(([key, entry]) => [`Document.${key}`, entry.zhs])]) };
    return <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={catalogs}>
      <WorkspaceObjectCopyInput instance={currentInstance} action={{ key: 'pasteObjectCopy', labelKey: 'Workspace.PasteObjectCopy' }} dataAccess={declaration} featurePosition="copy" queryScope={{ ...target, linkIds: JSON.stringify(target.linkIds) }} value={value} onChange={change} onDraftStateChange={state => { draftState(state); setReady(state.ready); }} />
      <button disabled={!ready} onClick={() => { pending = session.pasteObjectCopy(draft.draft, draft.values, target, 'copy', declaration.id).catch(error => error); }}>提交集合</button>
    </LocalizationProvider>;
  }
  const element = document.createElement('div'); document.body.append(element); const root = createRoot(element);
  try {
    await act(async () => root.render(<Harness />));
    const inputs = () => [...element.querySelectorAll<HTMLInputElement>('input')];
    expect(inputs()).toHaveLength(4); expect(draftState).toHaveBeenLastCalledWith({ dirty: false, ready: false });
    const controls = [...element.querySelectorAll<HTMLInputElement | HTMLTextAreaElement>('input,textarea')];
    expect(new Set(controls.map(control => control.id)).size).toBe(controls.length);
    for (const region of element.querySelectorAll('section[aria-label]')) for (const label of region.querySelectorAll<HTMLLabelElement>('label[for]'))
      expect(region.contains(document.getElementById(label.htmlFor))).toBe(true);
    const fill = async (index: number, text: string) => { await act(async () => { const input = inputs()[index]; Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, text); input.dispatchEvent(new Event('input', { bubbles: true })); }); };
    await fill(0, 'NEW-A'); expect(draftState).toHaveBeenLastCalledWith({ dirty: true, ready: false });
    await fill(2, 'NEW-B'); await fill(3, '用户修改 B'); expect(draftState).toHaveBeenLastCalledWith({ dirty: true, ready: true });
    vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ code: 'Document.CodeDuplicated', fieldErrors: [{ field: 'items.b.ECODE', message: '第二行编码已存在' }] }), { status: 409 }));
    await act(async () => { [...element.querySelectorAll('button')].find(button => button.textContent === '提交集合')!.click(); await pending; });
    expect(element.textContent).toContain('第二行编码已存在'); expect(inputs()[0].value).toBe('NEW-A'); expect(inputs()[2].value).toBe('NEW-B'); expect(inputs()[3].value).toBe('用户修改 B');
    const submitted = JSON.parse(String(vi.mocked(platformFetch).mock.calls[vi.mocked(platformFetch).mock.calls.length - 1][1]!.body)) as { values: unknown };
    expect(submitted.values).toEqual({ items: [{ sourceLinkId: 'a', values: { ECODE: 'NEW-A', ENAME: '来源 a', Description: '说明' } }, { sourceLinkId: 'b', values: { ECODE: 'NEW-B', ENAME: '用户修改 B', Description: '说明' } }] });
    expect(vi.mocked(platformFetch).mock.calls.filter(([url]) => String(url).endsWith('/objects/copy'))).toHaveLength(1);
    const oldDraft = draft!; const otherEntries = new Map<symbol, unknown>();
    const otherInstance: StandardCrudActionInstance = { active: true, changed: vi.fn(), getOrCreate: (key, create) => { if (!otherEntries.has(key)) otherEntries.set(key, create()); return otherEntries.get(key) as ReturnType<typeof create>; } };
    vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ items: ['a', 'b'].map(linkId => ({ linkId, modelKey: 'Document' })) })));
    await workspaceClipboard(otherInstance).copy(source, ['a', 'b'], 'copy', 'copy-button');
    vi.mocked(platformFetch).mockResolvedValueOnce(new Response(JSON.stringify({ operationId: 'another-actor', modelKey: 'Document', values, items, sourcePath: ['公共空间'], targetPath: ['公共空间', '目标'] })));
    await act(async () => root.render(<Harness currentInstance={otherInstance} />));
    expect(inputs()[0].value).toBe(''); expect(inputs()[2].value).toBe(''); expect(inputs()[3].value).toBe('来源 b');
    await expect(session.pasteObjectCopy(oldDraft.draft, oldDraft.values, target, 'copy', declaration.id)).rejects.toThrow();
  } finally { await act(async () => root.unmount()); element.remove(); vi.resetAllMocks(); }
});
it('keeps a real row validation error correctable while server failures remain unknown', () => {
  const handler = resource.actionHandlers!.pasteObjectCopy;
  expect(handler.isCorrectableInputError?.(new StandardCrudRequestError('说明过长', 400, { 'items.b.Description': '说明最多500字' }, { code: 'StandardCrud.ValidationFailed' }))).toBe(true);
  expect(handler.isCorrectableInputError?.(new StandardCrudRequestError('未知提交', 500, { 'items.b.Description': '未知' }, { code: 'Persistence.CommitOutcomeUnknown' }))).toBe(false);
});