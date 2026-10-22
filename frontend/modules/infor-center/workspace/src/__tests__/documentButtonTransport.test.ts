// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest';
import * as core from '@icp-platform/core';
import { cancelDocumentStage, saveDocument, stageDocumentFile } from '../workspaceDocumentUpload';

vi.mock('@icp-platform/core', async original => ({ ...await original<typeof core>(), platformFetch: vi.fn(), platformUpload: vi.fn() }));
const scope = { spaceId: 'space', rootFolderId: 'root', linkIds: '["folder-link"]' };
const values = { ECODE: 'DOCUMENT-333', ENAME: '正式图文档', FileUsageId: 'untrusted' };
afterEach(() => { vi.clearAllMocks(); });

it('stages using only the current create button while values contain business fields only', async () => {
  vi.mocked(core.platformUpload).mockResolvedValue(new Response(JSON.stringify({ uploadId: 'upload', fileName: 'main.pdf' })));
  await stageDocumentFile(new File(['file'], 'main.pdf'), values, scope, 'create-position', new AbortController().signal, vi.fn(), 'create-button');
  const [path, body, options] = vi.mocked(core.platformUpload).mock.calls[0];
  expect(path).toBe('/api/workspace/content/document-uploads');
  expect(options?.headers).toEqual({ 'X-NewP-Feature': 'create-position', 'X-NewP-Button': 'create-button' });
  expect(JSON.parse(String(body.get('values')))).toEqual({ ECODE: values.ECODE, ENAME: values.ENAME });
  expect(JSON.parse(String(body.get('position')))).toEqual({ spaceId: 'space', rootFolderId: 'root', linkIds: ['folder-link'] });
});

it('saves using the same actual button and never mixes a legacy declaration', async () => {
  vi.mocked(core.platformFetch).mockResolvedValue(new Response('{"succeeded":true}'));
  await saveDocument(values, { mainUploadId: 'main-upload', attachmentUploadIds: [] }, scope, 'create-position', 'create-button');
  const [path, options] = vi.mocked(core.platformFetch).mock.calls[0];
  expect(path).toBe('/api/workspace/content/documents');
  expect(options?.headers).toEqual({ 'Content-Type': 'application/json', 'X-NewP-Feature': 'create-position', 'X-NewP-Button': 'create-button' });
  expect(JSON.parse(String(options?.body))).toEqual({ position: { spaceId: 'space', rootFolderId: 'root', linkIds: ['folder-link'] },
    values: { ECODE: values.ECODE, ENAME: values.ENAME }, uploads: { mainUploadId: 'main-upload' } });
});

it('keeps actor-owned stage cancellation as the original feature-only pure API', async () => {
  vi.mocked(core.platformFetch).mockResolvedValue(new Response(null, { status: 204 }));
  await cancelDocumentStage('owned-upload', 'create-position');
  expect(core.platformFetch).toHaveBeenCalledWith('/api/workspace/content/document-uploads/owned-upload', {
    method: 'DELETE', headers: { 'X-NewP-Feature': 'create-position' },
  });
});
