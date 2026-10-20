// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { createObjectOwnershipTransferActions } from '../../../ui/src/objectOwnershipTransferActions';
import { ownershipTransferSource } from '../../../ui/src/ObjectOwnershipTransferInput';
import { ActionInstanceScope } from '../composition/ActionInstanceScope';
import type { ActionDataAccessRequirement } from '@icp-platform/sdk';
import declarations from '../../../../../tests/Kernel.Tests/Fixtures/OwnershipDataAccess.contract.json';

let instance: ActionInstanceScope;
let deactivate: () => void;
const target = { savePath: '/api/workspace/ownership/transfer', eligibilityPath: '/api/workspace/ownership/transfer/eligibility',
  accountsPath: '/api/workspace/ownership/transfer/accounts', body: { position: { spaceId: 'space', rootFolderId: 'root', linkIds: [] }, linkIds: ['reference'] } };
beforeEach(() => { instance = new ActionInstanceScope(); deactivate = instance.activate(); });
afterEach(() => { deactivate(); vi.restoreAllMocks(); });
it('sends only the fixed source and selected recipient and accepts a safe no-change success', async () => {
  const fetcher = vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify({ succeeded: true, changed: 0 })));
  const actions = createObjectOwnershipTransferActions(() => target, fetcher);
  const result = await actions.actionHandlers.transfer({ instance, featurePosition: 'transfer',
    input: { source: ownershipTransferSource(target, 'transfer', instance), recipientId: 'recipient', OwnerId: 'forged' } });
  expect(fetcher).toHaveBeenCalledTimes(1);
  const [url, init] = fetcher.mock.calls[0]!;
  expect(url).toBe(target.savePath); expect(init?.method).toBe('POST');
  expect(JSON.parse(String(init?.body))).toEqual({ ...target.body, recipientId: 'recipient' });
  expect(result).toMatchObject({ noChange: true, payload: { succeeded: true, changed: 0 } });
});
it('rejects an old input when the selected source changes without sending a request', async () => {
  const fetcher = vi.fn<typeof fetch>();
  const actions = createObjectOwnershipTransferActions(() => ({ ...target, body: { ...target.body, linkIds: ['different-reference'] } }), fetcher);
  await expect(actions.actionHandlers.transfer({ instance, featurePosition: 'transfer',
    input: { source: ownershipTransferSource(target, 'transfer', instance), recipientId: 'recipient' } })).rejects.toMatchObject({ status: 409 });
  expect(fetcher).not.toHaveBeenCalled();
});

it('does not replay an unknown submission even before the input can render the failure', async () => {
  const fetcher = vi.fn<typeof fetch>().mockRejectedValue(new Error('connection ended after send'));
  const actions = createObjectOwnershipTransferActions(() => target, fetcher);
  const context = { instance, featurePosition: 'transfer', input: { source: ownershipTransferSource(target, 'transfer', instance), recipientId: 'recipient' } };
  await expect(actions.actionHandlers.transfer(context)).rejects.toThrow('connection ended after send');
  await expect(actions.actionHandlers.transfer(context)).rejects.toMatchObject({ code: 'Platform.MutationOutcomeUnknown' });
  expect(fetcher).toHaveBeenCalledTimes(1);
});

it('isolates the draft and submission by the bound declaration even when the target and feature stay the same', async () => {
  const selected = ["Part", "Document"].map(moduleId => (declarations as ActionDataAccessRequirement[]).filter(item => item.moduleId === moduleId && item.provider === "entityOwnership.transfer" && item.surface === "rowActions" && item.actionKey === "transfer"));
  selected.forEach(items => expect(items).toHaveLength(1));
  const [original, changed] = selected.map(items => items[0]);
  expect(original.id).not.toBe(changed.id);
  const fetcher = vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify({ succeeded: true, changed: 0 })));
  const actions = createObjectOwnershipTransferActions(() => target, fetcher);
  const draft = { recipientId: 'recipient', source: ownershipTransferSource(target, 'transfer', instance, original) };
  await expect(actions.actionHandlers.transfer({ instance, featurePosition: 'transfer', action: { key: 'transfer', labelKey: '转交', dataAccess: changed },
    input: draft })).rejects.toMatchObject({ status: 409 });
  expect(fetcher).not.toHaveBeenCalled();
  await actions.actionHandlers.transfer({ instance, featurePosition: 'transfer', action: { key: 'transfer', labelKey: '转交', dataAccess: original }, input: draft });
  expect(new Headers(fetcher.mock.calls[0][1]?.headers).get('X-NewP-Data-Declaration')).toBe(original.id);
});
