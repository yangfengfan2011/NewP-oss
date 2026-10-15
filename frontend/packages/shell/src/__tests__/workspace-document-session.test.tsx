// @vitest-environment jsdom
import { act, useState } from 'react';
import { expect, it, vi } from 'vitest';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';
import * as core from '@icp-platform/core';
import { createLocalizedRoot as createRoot } from './localizedTestRoot';
import { loadComponentSource } from '../../test-support/componentTestInputs';
import { StandardCrudActionReviewDialog } from '../standard-crud/StandardCrudActionReviewDialog';
import { useStandardCrudActionExecution } from '../standard-crud/useStandardCrudActionExecution';

vi.unmock('@icp-platform/core');
const { default: resource } = await loadComponentSource<{ default: StandardCrudResourceRegistration }>('Workspace', 'workspaceRootResource.ts');
await loadComponentSource('Workspace', 'WorkspaceDocumentCreateInput.tsx');
await import('@icp-platform/ui/crud-form');

it.each(['committed', 'unknown'] as const)('unmounts the real input on authority change while its pending save settles as %s', async outcome => {
  const scope = { spaceId: 'public', rootFolderId: 'root', linkIds: '["content-folder-link"]' };
  const uploads = vi.spyOn(core, 'platformUpload').mockResolvedValue(new Response(JSON.stringify({
    uploadId: 'FileUploadLease_11111111111111111111111111111111', fileName: 'main.pdf', fileId: 'private', length: 1,
    contentType: 'application/pdf', expiresAt: '2026-10-15T00:00:00Z',
  })));
  const refresh = vi.fn(); const cancel = vi.fn();
  let finish!: (response: Response) => void;
  const request = vi.fn(async (input: RequestInfo | URL) => {
    const path = input instanceof Request ? new URL(input.url).pathname : String(input);
    if (path.includes('antiforgery-token')) return new Response(JSON.stringify({ requestToken: 'token' }));
    return new Promise<Response>(resolve => { finish = resolve; });
  });
  vi.stubGlobal('fetch', request);
  let draft: unknown; let saving: Promise<unknown> | undefined;
  function Harness({ feature }: { feature: string }) {
    const [value, setValue] = useState<unknown>({ ECODE: 'DOC', ENAME: '图文档' }); draft = value;
    const execution = useStandardCrudActionExecution({ refresh });
    return <StandardCrudActionReviewDialog action={{ key: 'createDocument', labelKey: '新建图文档', inputKey: 'createObject' }}
      title="新建图文档" featurePosition={feature} queryScope={scope} value={value} onChange={setValue}
      inputRenderer={resource.actionInputRenderers!.createObject} pending={execution.pendingKey !== null} onCancel={cancel}
      onConfirm={() => { saving = execution.execute({ key: 'createDocument', handler: resource.actionHandlers!.createDocument,
        context: { input: value, queryScope: scope, featurePosition: feature } }); }} />;
  }
  const element = document.createElement('div'); document.body.append(element); const root = createRoot(element);
  try {
    await act(async () => root.render(<Harness feature="create-position" />));
    const input = document.querySelector<HTMLInputElement>('.ant-drawer input[type=file]:not([multiple])')!;
    expect(input).not.toBeNull();
    Object.defineProperty(input, 'files', { configurable: true, value: [new File(['1'], 'main.pdf')] });
    await act(async () => input.dispatchEvent(new Event('change', { bubbles: true })));
    const save = document.querySelector<HTMLButtonElement>('.ant-drawer-footer .ant-btn-primary')!;
    expect(save.disabled).toBe(false);
    await act(async () => save.click());
    expect(finish).toBeTypeOf('function');
    const oldDraft = draft;
    await act(async () => root.render(<Harness feature="changed-position" />));
    expect(document.querySelector('.ant-drawer input[type=file]')).toBeNull();
    expect(cancel).not.toHaveBeenCalled();
    await expect(resource.actionHandlers!.createDocument({ input: oldDraft, queryScope: scope, featurePosition: 'create-position' })).rejects.toThrow('上传会话');
    await act(async () => { finish(new Response(outcome === 'committed' ? '{"succeeded":true}' : '{"succeeded":', { status: 200 })); await saving; });
    expect(cancel).toHaveBeenCalledTimes(1);
    expect(refresh).toHaveBeenCalledTimes(outcome === 'committed' ? 1 : 0);
    const writes = request.mock.calls.filter(([input]) => String(input instanceof Request ? input.url : input).includes('/api/workspace/content/'));
    expect(writes).toHaveLength(1);
    expect((writes[0][0] as Request).method).toBe('POST');
    expect(uploads).toHaveBeenCalledTimes(1);
  } finally {
    await act(async () => root.unmount()); element.remove(); uploads.mockRestore(); vi.unstubAllGlobals(); core.invalidatePlatformAntiforgeryToken();
  }
});
