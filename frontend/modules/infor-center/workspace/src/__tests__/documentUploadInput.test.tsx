// @vitest-environment jsdom
import { act, StrictMode, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, expect, it, vi } from 'vitest';
import type { ActionDataAccessRequirement } from '@icp-platform/sdk';
import compiled from '../../../../../../tests/Kernel.Tests/Fixtures/WorkspaceClipboardDataAccess.contract.json';
import { invalidatePlatformAntiforgeryToken, LocalizationProvider } from '@icp-platform/core';
import type { StandardCrudActionInputRenderContext } from '@icp-platform/sdk';
import WorkspaceDocumentCreateInput from '../WorkspaceDocumentCreateInput';
import resource from '../workspaceRootResource';
import { cancelDocumentStage, stageDocumentFile, type DocumentStage, type WorkspaceDocumentDraft } from '../workspaceDocumentUpload';

vi.mock('../workspaceDocumentUpload', async importOriginal => ({
  ...await importOriginal<typeof import('../workspaceDocumentUpload')>(),
  stageDocumentFile: vi.fn(), cancelDocumentStage: vi.fn().mockResolvedValue(undefined),
}));
const scope = { spaceId: 'space', rootFolderId: 'root', linkIds: '["content-folder-link"]' };
// 本文件只测输入/会话生命周期；声明身份取正式编译fixture，实际Provider透传由PageEntry消费者证明。
const source = compiled.declarations.find(item => item.viewKey === 'workspace.folder.list' && item.surface === 'pageActions' && item.actionKey === 'createDocument') as unknown as ActionDataAccessRequirement;
const createAction = { key: 'createDocument', labelKey: 'Create', inputKey: 'createObject', buttonId: source.id, dataAccess: source };
const staged: DocumentStage = { uploadId: 'FileUploadLease_11111111111111111111111111111111', fileId: 'private', fileName: 'main.pdf', length: 4, contentType: 'application/pdf', expiresAt: '2026-10-15T00:00:00Z' };
const roots: { root: Root; element: HTMLElement }[] = [];
let currentDraft: WorkspaceDocumentDraft;
const state = vi.fn();
function Harness({ queryScope = scope, buttonId = source.id }: { queryScope?: Record<string, unknown>; buttonId?: string }) {
  const [value, change] = useState<WorkspaceDocumentDraft>({ ECODE: 'FILE', ENAME: '图文档' }); currentDraft = value;
  const context: StandardCrudActionInputRenderContext = { action: { ...createAction, buttonId, dataAccess: { ...source, id: buttonId } }, buttonId,
    value, onChange: next => change(next as WorkspaceDocumentDraft), queryScope, featurePosition: 'create-position', onDraftStateChange: state };
  return <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{}}><WorkspaceDocumentCreateInput {...context} /></LocalizationProvider>;
}
async function mount() {
  const element = document.createElement('div'); document.body.append(element); const root = createRoot(element); roots.push({ element, root });
  await act(async () => root.render(<Harness />)); return { root, element };
}
async function choose(element: HTMLElement) {
  const input = element.querySelector('input[type=file]') as HTMLInputElement;
  Object.defineProperty(input, 'files', { configurable: true, value: [new File(['file'], 'main.pdf', { type: 'application/pdf' })] });
  await act(async () => input.dispatchEvent(new Event('change', { bubbles: true })));
}
afterEach(async () => { for (const { root, element } of roots.splice(0)) { await act(async () => root.unmount()); element.remove(); } vi.clearAllMocks(); vi.unstubAllGlobals(); invalidatePlatformAntiforgeryToken(); });

it('uses the current design button and hides the browser file input', async () => {
  const { element } = await mount();
  const input = element.querySelector('input[type=file]') as HTMLInputElement;
  expect(input.hidden).toBe(true);
  const select = [...element.querySelectorAll('button')].find(button => button.textContent === '选择主文件')!;
  expect(select).toBeDefined(); expect(select.classList.contains('ant-btn')).toBe(true);
  const open = vi.spyOn(input, 'click').mockImplementation(() => undefined);
  await act(async () => select.click()); expect(open).toHaveBeenCalledOnce();
  expect(element.querySelector('progress')).toBeNull();
});

it('reports real progress, keeps save disabled until staged and cancels an uncommitted file on close', async () => {
  let finish!: (value: DocumentStage) => void;
  vi.mocked(stageDocumentFile).mockImplementation(() => new Promise(resolve => { finish = resolve; }));
  const { root, element } = await mount();
  expect(state).toHaveBeenLastCalledWith({ dirty: true, ready: false });
  await choose(element);
  expect([...element.querySelectorAll('button')].find(button => button.textContent === '选择主文件')?.disabled).toBe(true);
  const progress = vi.mocked(stageDocumentFile).mock.calls[0][5];
  await act(async () => progress(2, 4)); expect(element.textContent).toContain('50%');
  await act(async () => progress(3, undefined));
  expect(element.textContent).toContain('正在上传'); expect(element.textContent).not.toContain('50%');
  expect(element.querySelector('[role=progressbar]')).toBeNull();
  expect(state).toHaveBeenLastCalledWith({ dirty: true, ready: false });
  await act(async () => finish(staged));
  expect(element.textContent).toContain('保存后生效'); expect(currentDraft.session?.getSnapshot().choices.find(choice => choice.role === 'main')?.staged?.uploadId).toBe(staged.uploadId);
  expect(state).toHaveBeenLastCalledWith({ dirty: true, ready: true });
  await act(async () => root.unmount()); roots.splice(0);
  expect(cancelDocumentStage).toHaveBeenCalledWith(staged.uploadId, 'create-position');
});

it('aborts a cancelled writer and disposes its late token without enabling save', async () => {
  let finish!: (value: DocumentStage) => void;
  vi.mocked(stageDocumentFile).mockImplementation(() => new Promise(resolve => { finish = resolve; }));
  const { element } = await mount(); await choose(element);
  const signal = vi.mocked(stageDocumentFile).mock.calls[0][4];
  const cancel = [...element.querySelectorAll('button')].find(button => button.textContent === '取消上传')!;
  await act(async () => cancel.click()); expect(signal.aborted).toBe(true);
  await act(async () => finish(staged)); expect(currentDraft.session?.getSnapshot().choices.find(choice => choice.role === 'main')?.staged?.uploadId).toBeUndefined();
  expect(cancelDocumentStage).toHaveBeenCalledWith(staged.uploadId, 'create-position');
  expect(state).toHaveBeenLastCalledWith({ dirty: true, ready: false });
});

it.each(['position', 'declaration'] as const)('isolates late response after %s change and clears the old upload choice', async changed => {
  let finish!: (value: DocumentStage) => void;
  vi.mocked(stageDocumentFile).mockImplementation(() => new Promise(resolve => { finish = resolve; }));
  const { root, element } = await mount(); await choose(element);
  const signal = vi.mocked(stageDocumentFile).mock.calls[0][4];
  await act(async () => root.render(changed === 'position' ? <Harness queryScope={{ ...scope, rootFolderId: 'other' }} /> : <Harness buttonId="other-declaration" />));
  expect(signal.aborted).toBe(true);
  await act(async () => finish(staged)); expect(currentDraft.session?.getSnapshot().choices.find(choice => choice.role === 'main')?.staged?.uploadId).toBeUndefined();
  expect(state).toHaveBeenLastCalledWith({ dirty: true, ready: false });
});

it.each(['before-response', 'body-read', 'truncated-json'] as const)('retains unknown business submission after %s and prevents re-save or upload cancellation on unmount', async failure => {
  vi.mocked(stageDocumentFile).mockResolvedValue(staged);
  const { root, element } = await mount(); await choose(element);
  const cause = new TypeError('Connection lost while receiving the save receipt.');
  const request = vi.fn(async (input: RequestInfo | URL) => {
    if (String(input instanceof Request ? input.url : input).includes('antiforgery-token')) return new Response(JSON.stringify({ requestToken: 'token' }));
    if (failure === 'before-response') throw cause;
    const body = failure === 'body-read' ? new ReadableStream({ start(controller) { controller.error(cause); } }) : '{"receipt":';
    return new Response(body, { status: 200, headers: { 'Content-Type': 'application/json' } });
  });
  vi.stubGlobal('fetch', request);
  await act(async () => {
    await expect(resource.actionHandlers!.createDocument({ input: currentDraft, queryScope: scope, featurePosition: 'create-position', action: createAction, buttonId: createAction.buttonId }))
      .rejects.toMatchObject({ code: 'Platform.MutationOutcomeUnknown', ...(failure === 'before-response' ? {} : { status: 200 }),
        cause: failure === 'truncated-json' ? expect.any(SyntaxError) : cause });
  });
  const saves = request.mock.calls.filter(([input]) => String(input instanceof Request ? input.url : input).includes('/api/workspace/content/documents'));
  expect(saves).toHaveLength(1);
  expect(await (saves[0][0] as Request).clone().json()).toEqual({ position: { spaceId: 'space', rootFolderId: 'root', linkIds: ['content-folder-link'] },
    values: { ECODE: 'FILE', ENAME: '图文档' }, uploads: { mainUploadId: staged.uploadId } });
  expect(element.textContent).toContain('保存结果尚未确认');
  expect([...element.querySelectorAll('button')].find(button => button.textContent === '选择主文件')?.disabled).toBe(true);
  expect([...element.querySelectorAll('button')].find(button => button.textContent === '取消上传')?.disabled).toBe(true);
  expect(state).toHaveBeenLastCalledWith({ dirty: true, ready: false });
  await act(async () => root.unmount()); roots.splice(0);
  expect(cancelDocumentStage).not.toHaveBeenCalled();
});

it('shows upload failure and keeps the real save unavailable', async () => {
  vi.mocked(stageDocumentFile).mockRejectedValue(new Error('upload failed'));
  const { element } = await mount(); await choose(element);
  expect(element.querySelector('[role=alert],.ant-alert')).not.toBeNull();
  expect(currentDraft.session?.getSnapshot().choices.find(choice => choice.role === 'main')?.staged?.uploadId).toBeUndefined(); expect(state).toHaveBeenLastCalledWith({ dirty: true, ready: false });
});

it('keeps the live session across rerenders after StrictMode replay and rejects its handle after closing', async () => {
  vi.mocked(stageDocumentFile).mockResolvedValue(staged);
  const { root, element } = await mount();
  await act(async () => root.render(<StrictMode><Harness /></StrictMode>));
  const session = currentDraft.session!;
  expect(session.getSnapshot().closed).toBe(false);
  await choose(element);
  await act(async () => root.render(<StrictMode><Harness /></StrictMode>));
  expect(currentDraft.session).toBe(session);
  expect(session.canSubmit()).toBe(true);
  const request = vi.fn(); vi.stubGlobal('fetch', request);
  await act(async () => root.unmount()); roots.splice(0);
  await expect(resource.actionHandlers!.createDocument({ input: currentDraft, queryScope: scope, featurePosition: 'create-position', action: createAction, buttonId: createAction.buttonId })).rejects.toThrow('上传会话');
  expect(request).not.toHaveBeenCalled();
  expect(cancelDocumentStage).toHaveBeenCalledTimes(1);
});
