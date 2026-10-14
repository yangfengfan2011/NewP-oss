// @vitest-environment jsdom
import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, expect, it, vi } from 'vitest';
import { invalidatePlatformAntiforgeryToken, LocalizationProvider } from '@icp-platform/core';
import WorkspaceDocumentCreateInput from '../WorkspaceDocumentCreateInput';
import resource from '../workspaceRootResource';
import { cancelDocumentStage, stageDocumentFile, type DocumentStage, type WorkspaceDocumentDraft } from '../workspaceDocumentUpload';

vi.mock('@icp-platform/core', async importOriginal => await importOriginal<typeof import('@icp-platform/core')>());
vi.mock('../workspaceDocumentUpload', async importOriginal => ({
  ...await importOriginal<typeof import('../workspaceDocumentUpload')>(),
  stageDocumentFile: vi.fn(), cancelDocumentStage: vi.fn().mockResolvedValue(undefined),
}));
const scope = { spaceId: 'space', rootFolderId: 'root', linkIds: '[]' };
const roots: { root: Root; element: HTMLElement }[] = [];
let currentDraft: WorkspaceDocumentDraft;
const state = vi.fn();
function Harness({ queryScope = scope }: { queryScope?: Record<string, unknown> }) {
  const [value, change] = useState<WorkspaceDocumentDraft>({ ECODE: 'COLLECTION', ENAME: '集合图文档' }); currentDraft = value;
  return <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{}}>
    <WorkspaceDocumentCreateInput action={{ key: 'createDocument', labelKey: 'Create', inputKey: 'createObject' }}
      value={value} onChange={next => change(next as WorkspaceDocumentDraft)} queryScope={queryScope}
      featurePosition="create-position" onDraftStateChange={state} />
  </LocalizationProvider>;
}
async function mount() {
  const element = document.createElement('div'); document.body.append(element); const root = createRoot(element); roots.push({ root, element });
  await act(async () => root.render(<Harness />)); return { root, element };
}
async function select(element: HTMLElement, files: File[], attachments = false) {
  const input = element.querySelector(`input[type=file]${attachments ? '[multiple]' : ':not([multiple])'}`) as HTMLInputElement;
  expect(input).not.toBeNull(); expect(input.hidden).toBe(true);
  Object.defineProperty(input, 'files', { configurable: true, value: files });
  await act(async () => input.dispatchEvent(new Event('change', { bubbles: true })));
}
const file = (name: string) => new File(['file'], name, { type: 'application/pdf' });
const stage = (name: string, suffix: string): DocumentStage => ({ uploadId: `FileUploadLease_${suffix.repeat(32)}`, fileId: `file-${suffix}`,
  fileName: name, length: 4, contentType: 'application/pdf', expiresAt: '2026-10-15T00:00:00Z' });
afterEach(async () => { for (const { root, element } of roots.splice(0)) { await act(async () => root.unmount()); element.remove(); } vi.clearAllMocks(); vi.unstubAllGlobals(); invalidatePlatformAntiforgeryToken(); });

it('uploads main and selected attachments one at a time and publishes their selection order', async () => {
  const pending: ((value: DocumentStage) => void)[] = [];
  vi.mocked(stageDocumentFile).mockImplementation(() => new Promise(resolve => pending.push(resolve)));
  const { element } = await mount();
  await select(element, [file('main.pdf')]);
  await select(element, [file('first.pdf'), file('second.pdf')], true);
  expect(stageDocumentFile).toHaveBeenCalledTimes(1);
  expect(state).toHaveBeenLastCalledWith({ dirty: true, ready: false });
  await act(async () => pending[0](stage('main.pdf', '1')));
  expect(stageDocumentFile).toHaveBeenCalledTimes(2);
  expect(currentDraft.mainUploadId).toBe(stage('main.pdf', '1').uploadId);
  expect(state).toHaveBeenLastCalledWith({ dirty: true, ready: false });
  const firstProgress = vi.mocked(stageDocumentFile).mock.calls[1][5];
  await act(async () => firstProgress(1, 4));
  expect(element.querySelector('[role=group][aria-label="附件 first.pdf"]')?.textContent).toContain('25%');
  await act(async () => pending[1](stage('first.pdf', '2')));
  expect(stageDocumentFile).toHaveBeenCalledTimes(3);
  await act(async () => pending[2](stage('second.pdf', '3')));
  expect(currentDraft.attachmentUploadIds).toEqual([stage('first.pdf', '2').uploadId, stage('second.pdf', '3').uploadId]);
  expect(state).toHaveBeenLastCalledWith({ dirty: true, ready: true });
  expect(element.querySelector('[role=group][aria-label="附件 second.pdf"]')?.textContent).toContain('附件已上传，保存后生效。');
});

it('keeps a failed attachment in its chosen position and permits retry without re-uploading successful files', async () => {
  vi.mocked(stageDocumentFile).mockResolvedValueOnce(stage('main.pdf', '1')).mockRejectedValueOnce(new Error('first upload failed'))
    .mockResolvedValueOnce(stage('second.pdf', '3')).mockResolvedValueOnce(stage('first.pdf', '2'));
  const { element } = await mount(); await select(element, [file('main.pdf')]);
  await select(element, [file('first.pdf'), file('second.pdf')], true);
  expect(state).toHaveBeenLastCalledWith({ dirty: true, ready: false });
  const first = element.querySelector('[role=group][aria-label="附件 first.pdf"]')!;
  expect(first.querySelector('.ant-alert')).not.toBeNull();
  const retry = first.querySelector('button[aria-label="重试上传 first.pdf"]') as HTMLButtonElement;
  await act(async () => retry.click());
  expect(stageDocumentFile).toHaveBeenCalledTimes(4);
  expect(vi.mocked(stageDocumentFile).mock.calls.map(call => call[0].name)).toEqual(['main.pdf', 'first.pdf', 'second.pdf', 'first.pdf']);
  expect(currentDraft.attachmentUploadIds).toEqual([stage('first.pdf', '2').uploadId, stage('second.pdf', '3').uploadId]);
  expect(state).toHaveBeenLastCalledWith({ dirty: true, ready: true });
});

it('removes a pending attachment, cancels its late token and continues the next file without reusing the removed choice', async () => {
  let complete!: (value: DocumentStage) => void;
  vi.mocked(stageDocumentFile).mockResolvedValueOnce(stage('main.pdf', '1'))
    .mockImplementationOnce(() => new Promise(resolve => { complete = resolve; })).mockResolvedValueOnce(stage('second.pdf', '3'));
  const { root, element } = await mount(); await select(element, [file('main.pdf')]);
  await select(element, [file('first.pdf'), file('second.pdf')], true);
  const signal = vi.mocked(stageDocumentFile).mock.calls[1][4];
  await act(async () => (element.querySelector('button[aria-label="移除附件 first.pdf"]') as HTMLButtonElement).click());
  expect(signal.aborted).toBe(true); expect(stageDocumentFile).toHaveBeenCalledTimes(2);
  await act(async () => complete(stage('first.pdf', '2')));
  expect(stageDocumentFile).toHaveBeenCalledTimes(3);
  expect(currentDraft.attachmentUploadIds).toEqual([stage('second.pdf', '3').uploadId]);
  expect(cancelDocumentStage).toHaveBeenCalledWith(stage('first.pdf', '2').uploadId, 'create-position');
  expect(state).toHaveBeenLastCalledWith({ dirty: true, ready: true });
  await act(async () => root.unmount()); roots.splice(0);
  expect(vi.mocked(cancelDocumentStage).mock.calls.map(call => call[0])).toEqual([
    stage('first.pdf', '2').uploadId, stage('main.pdf', '1').uploadId, stage('second.pdf', '3').uploadId,
  ]);
});

it('isolates the whole selection and a late attachment after its position changes', async () => {
  let complete!: (value: DocumentStage) => void;
  vi.mocked(stageDocumentFile).mockResolvedValueOnce(stage('main.pdf', '1'))
    .mockImplementationOnce(() => new Promise(resolve => { complete = resolve; }));
  const { root, element } = await mount(); await select(element, [file('main.pdf')]);
  await select(element, [file('first.pdf'), file('never-started.pdf')], true);
  const signal = vi.mocked(stageDocumentFile).mock.calls[1][4];
  await act(async () => root.render(<Harness queryScope={{ ...scope, rootFolderId: 'other-root' }} />));
  expect(signal.aborted).toBe(true); expect(currentDraft.mainUploadId).toBeUndefined(); expect(currentDraft.attachmentUploadIds).toEqual([]);
  await act(async () => complete(stage('first.pdf', '2')));
  expect(stageDocumentFile).toHaveBeenCalledTimes(2); expect(element.textContent).not.toContain('first.pdf');
  expect(cancelDocumentStage).toHaveBeenCalledWith(stage('main.pdf', '1').uploadId, 'create-position');
  expect(cancelDocumentStage).toHaveBeenCalledWith(stage('first.pdf', '2').uploadId, 'create-position');
  expect(state).toHaveBeenLastCalledWith({ dirty: true, ready: false });
});

it('rejects an oversized selection before starting any of its uploads', async () => {
  const { element } = await mount();
  await select(element, Array.from({ length: 101 }, (_, index) => file(`${index}.pdf`)), true);
  expect(stageDocumentFile).not.toHaveBeenCalled(); expect(element.textContent).toContain('最多可选择100个附件');
  expect(currentDraft.attachmentUploadIds).toEqual([]); expect(state).toHaveBeenLastCalledWith({ dirty: true, ready: false });
});

it.each(['committed', 'unknown'] as const)('posts the selected collection once and preserves all leases on %s close', async outcome => {
  vi.mocked(stageDocumentFile).mockResolvedValueOnce(stage('main.pdf', '1')).mockResolvedValueOnce(stage('first.pdf', '2')).mockResolvedValueOnce(stage('second.pdf', '3'));
  const { root, element } = await mount(); await select(element, [file('main.pdf')]); await select(element, [file('first.pdf'), file('second.pdf')], true);
  let finish!: (response: Response) => void;
  const request = vi.fn(async (input: RequestInfo | URL) => {
    if (String(input instanceof Request ? input.url : input).includes('antiforgery-token')) return new Response(JSON.stringify({ requestToken: 'token' }));
    return new Promise<Response>(resolve => { finish = resolve; });
  }); vi.stubGlobal('fetch', request);
  let saved!: Promise<unknown>;
  await act(async () => { saved = Promise.resolve(resource.actionHandlers!.createDocument({ input: currentDraft, queryScope: scope, featurePosition: 'create-position' })); });
  await expect(resource.actionHandlers!.createDocument({ input: currentDraft, queryScope: scope, featurePosition: 'create-position' })).rejects.toThrow('本次保存已经开始');
  await act(async () => {
    finish(new Response(outcome === 'committed' ? '{"succeeded":true}' : '{"succeeded":', { status: 200 }));
    if (outcome === 'committed') await expect(saved).resolves.toMatchObject({ operation: 'action', change: { operation: 'update' } });
    else await expect(saved).rejects.toMatchObject({ code: 'Platform.MutationOutcomeUnknown', status: 200 });
  });
  const posts = request.mock.calls.filter(([input]) => String(input instanceof Request ? input.url : input).includes('/api/workspace/content/documents'));
  expect(posts).toHaveLength(1);
  expect((await (posts[0][0] as Request).clone().json()).uploads).toEqual({ mainUploadId: stage('main.pdf', '1').uploadId,
    attachmentUploadIds: [stage('first.pdf', '2').uploadId, stage('second.pdf', '3').uploadId] });
  if (outcome === 'unknown') {
    expect(state).toHaveBeenLastCalledWith({ dirty: true, ready: false });
    expect((element.querySelector('button[aria-label="移除附件 first.pdf"]') as HTMLButtonElement).disabled).toBe(true);
  }
  await act(async () => root.unmount()); roots.splice(0); expect(cancelDocumentStage).not.toHaveBeenCalled();
});
