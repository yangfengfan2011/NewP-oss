// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest';
import { StandardCrudRequestError } from '@icp-platform/sdk';
import { WorkspaceDocumentSession } from '../workspaceDocumentSession';
import { cancelDocumentStage, saveDocument, stageDocumentFile, type DocumentStage } from '../workspaceDocumentUpload';

vi.mock('../workspaceDocumentUpload', async importOriginal => ({
  ...await importOriginal<typeof import('../workspaceDocumentUpload')>(),
  stageDocumentFile: vi.fn(), cancelDocumentStage: vi.fn(), saveDocument: vi.fn(),
}));
const scope = { spaceId: 'public', rootFolderId: 'root', linkIds: '[]' };
const fields = { ECODE: 'DOC', ENAME: '图文档' };
const staged = (name: string): DocumentStage => ({ uploadId: `upload-${name}`, fileId: `file-${name}`, fileName: name,
  length: 1, contentType: 'application/pdf', expiresAt: '2026-10-15T00:00:00Z' });
function session() { return new WorkspaceDocumentSession(scope, 'create-position', () => fields); }
async function ready(target: WorkspaceDocumentSession) {
  vi.mocked(stageDocumentFile).mockResolvedValueOnce(staged('main.pdf'));
  target.choose([new File(['1'], 'main.pdf')], 'main');
  await vi.waitFor(() => expect(target.canSubmit()).toBe(true));
}
afterEach(() => { vi.resetAllMocks(); });

it('rejects missing readiness, changed position or feature, and a closed session before saving', async () => {
  const target = session();
  await expect(target.submit(fields, scope, 'create-position')).rejects.toThrow('完成主文件');
  await ready(target);
  await expect(target.submit(fields, { ...scope, rootFolderId: 'elsewhere' }, 'create-position')).rejects.toThrow('上传会话');
  await expect(target.submit(fields, scope, 'other-feature')).rejects.toThrow('上传会话');
  target.close(); target.close();
  await expect(target.submit(fields, scope, 'create-position')).rejects.toThrow('上传会话');
  expect(saveDocument).not.toHaveBeenCalled();
  expect(cancelDocumentStage).toHaveBeenCalledTimes(1);
});

it('owns tokens and submits a business-field snapshot without serializing the session or forged tokens', async () => {
  const target = session(); await ready(target);
  vi.mocked(saveDocument).mockResolvedValue({ succeeded: true });
  const values = { ...fields, session: target, mainUploadId: 'forged' };
  await expect(target.submit(values, scope, 'create-position')).resolves.toEqual({ succeeded: true });
  expect(saveDocument).toHaveBeenCalledTimes(1);
  expect(saveDocument).toHaveBeenCalledWith(fields, { mainUploadId: 'upload-main.pdf', attachmentUploadIds: [] }, scope, 'create-position');
  expect(target.getSnapshot().phase).toBe('committed');
  target.close(); expect(cancelDocumentStage).not.toHaveBeenCalled();
});

it('keeps staged uploads reusable after a known failure and captures corrected fields on the next explicit save', async () => {
  const target = session(); await ready(target);
  const duplicate = new StandardCrudRequestError('duplicate', 409, {}, { code: 'Document.CodeDuplicated' });
  vi.mocked(saveDocument).mockRejectedValueOnce(duplicate).mockResolvedValueOnce({ succeeded: true });
  await expect(target.submit(fields, scope, 'create-position')).rejects.toBe(duplicate);
  expect(target.canSubmit()).toBe(true);
  await target.submit({ ...fields, ECODE: 'CORRECTED' }, scope, 'create-position');
  expect(saveDocument).toHaveBeenLastCalledWith({ ...fields, ECODE: 'CORRECTED' },
    { mainUploadId: 'upload-main.pdf', attachmentUploadIds: [] }, scope, 'create-position');
  expect(stageDocumentFile).toHaveBeenCalledTimes(1);
  expect(cancelDocumentStage).not.toHaveBeenCalled();
});

it.each(['success', 'known-failure', 'Platform.MutationOutcomeUnknown', 'Persistence.CommitOutcomeUnknown'])(
  'isolates a pending save from closing and a new session when its late result is %s', async outcome => {
    const old = session(); await ready(old);
    let finish!: (value: unknown) => void; let fail!: (error: unknown) => void;
    vi.mocked(saveDocument).mockImplementationOnce(() => new Promise((resolve, reject) => { finish = resolve; fail = reject; }));
    const changed = vi.fn(); old.subscribe(changed);
    const saving = old.submit(fields, scope, 'create-position');
    await expect(old.submit(fields, scope, 'create-position')).rejects.toThrow('本次保存已经开始');
    old.close(); old.close();
    const notifications = changed.mock.calls.length;
    const next = session(); await ready(next);
    if (outcome === 'success') { finish({ succeeded: true }); await saving; }
    else {
      const error = new StandardCrudRequestError('failed', 409, {}, { code: outcome });
      fail(error); await expect(saving).rejects.toBe(error);
    }
    expect(old.getSnapshot()).toMatchObject({ closed: true, phase: outcome === 'success' ? 'committed' : outcome === 'known-failure' ? 'idle' : 'unknown' });
    await expect(old.submit(fields, scope, 'create-position')).rejects.toThrow('上传会话');
    expect(next.canSubmit()).toBe(true);
    expect(changed).toHaveBeenCalledTimes(notifications);
    expect(saveDocument).toHaveBeenCalledTimes(1);
    expect(cancelDocumentStage).not.toHaveBeenCalled();
  });

it('closes once and cancels late upload tokens without publishing into the next session', async () => {
  const old = session(); await ready(old);
  let finish!: (value: DocumentStage) => void;
  vi.mocked(stageDocumentFile).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
  old.choose([new File(['2'], 'attachment.pdf')], 'attachment');
  const signal = vi.mocked(stageDocumentFile).mock.calls[1][4];
  old.close(); old.close(); expect(signal.aborted).toBe(true);
  const next = session(); await ready(next);
  finish(staged('attachment.pdf'));
  await vi.waitFor(() => expect(cancelDocumentStage).toHaveBeenCalledTimes(2));
  expect(vi.mocked(cancelDocumentStage).mock.calls.map(call => call[0])).toEqual(['upload-main.pdf', 'upload-attachment.pdf']);
  expect(next.getSnapshot().choices).toHaveLength(1); expect(next.canSubmit()).toBe(true);
});
