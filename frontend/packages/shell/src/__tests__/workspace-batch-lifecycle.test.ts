// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest';
import { ActionInstanceScope } from '../composition/ActionInstanceScope';
import { batchSubmission, saveBatch, type BatchDraft } from '../../../../modules/infor-center/workspace/src/workspaceBatchAuthorization';
import positions from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceBatchPermissionFeatures.contract.json';

vi.unmock('@icp-platform/core');

const draft = (): BatchDraft => ({ catalogs: [{ resourceKey: 'part.part', actions: [{ key: 'read', title: 'Read', enabled: true }], subjectTypes: [{ key: 'all', title: 'All', requiresId: false }] }], mode: 'U', confirmClear: false,
  items: [{ resourceKey: 'part.part', objectId: 'part', name: 'Part', code: null, manageable: true,
    source: { position: { spaceId: 'public', rootFolderId: 'root', linkIds: ['container'] }, linkIds: ['occurrence'] } }],
  resources: [{ resourceKey: 'part.part', subjects: [{ id: 'ui-only', type: 'all', subjectId: null, priority: 4, actions: { read: 'allow' }, expiresAt: null }] }] });
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
afterEach(() => vi.unstubAllGlobals());

it('freezes a real commit-unknown response and rejects replay in the same live instance', async () => {
  const instance = new ActionInstanceScope(); const deactivate = instance.activate();
  let saves = 0;
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const path = new URL(input instanceof Request ? input.url : String(input), window.location.origin).pathname;
    if (path.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (path.endsWith('/permissions/batch')) { saves++; return json({ code: 'Persistence.CommitOutcomeUnknown' }, 409); }
    throw new Error(`Unexpected request ${path}`);
  }));
  const value = draft();
  try {
    await expect(saveBatch({ instance, featurePosition: positions.batchPosition, input: value })).rejects.toMatchObject({ status: 409, code: 'Persistence.CommitOutcomeUnknown' });
    const state = batchSubmission(instance)!;
    expect(state.getSnapshot()).toBe(true);
    value.items[0].name = 'mutated';
    expect(state.lastDraft?.items[0].name).toBe('Part');
    await expect(saveBatch({ instance, featurePosition: positions.batchPosition, input: draft() })).rejects.toMatchObject({ code: 'Workspace.BatchUnknown' });
    expect(saves).toBe(1);
  } finally { deactivate(); }
});

it('keeps one immutable in-flight payload, rejects double submit and discards a late reply after instance disposal', async () => {
  const instance = new ActionInstanceScope(); const deactivate = instance.activate();
  let release!: (response: Response) => void;
  let request: Request | undefined;
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const path = new URL(input instanceof Request ? input.url : String(input), window.location.origin).pathname;
    if (path.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (path.endsWith('/permissions/batch')) { request = input as Request; return new Promise<Response>(resolve => { release = resolve; }); }
    throw new Error(`Unexpected request ${path}`);
  }));
  const value = draft();
  const state = batchSubmission(instance)!;
  const operation = saveBatch({ instance, featurePosition: positions.batchPosition, input: value });
  await vi.waitFor(() => expect(request).toBeDefined());
  value.resources[0].subjects[0].actions.read = 'deny';
  await expect(saveBatch({ instance, featurePosition: positions.batchPosition, input: draft() })).rejects.toMatchObject({ status: 409 });
  expect((await request!.clone().json()).resources[0].subjects[0].actions.read).toBe('allow');
  deactivate();
  expect(request!.signal.aborted).toBe(true);
  expect(state.lastDraft).toBeUndefined();
  const rejected = expect(operation).rejects.toMatchObject({ name: 'AbortError' });
  release(json({ succeeded: true, targets: 1, changed: 1 }));
  await rejected;
  expect(batchSubmission(instance)).toBeUndefined();
  expect(state.lastDraft).toBeUndefined();
  expect(state.getSnapshot()).toBe(false);
});

it.each([400, 403, 404])('preserves a known %s refusal without falsely claiming an unknown commit', async status => {
  const instance = new ActionInstanceScope(); const deactivate = instance.activate();
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const path = new URL(input instanceof Request ? input.url : String(input), window.location.origin).pathname;
    if (path.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    return json({ code: 'REJECTED' }, status);
  }));
  try {
    await expect(saveBatch({ instance, featurePosition: positions.batchPosition, input: draft() })).rejects.toMatchObject({ status });
    expect(batchSubmission(instance)?.getSnapshot()).toBe(false);
  } finally { deactivate(); }
});
