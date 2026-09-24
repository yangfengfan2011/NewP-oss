import { expect, it, vi } from 'vitest';
import { createExampleRecordStandardCrudResource } from '../standard-crud/exampleRecordStandardCrudResource';

it('binds both buttons, preserves feature identity, saves a fixed target and never writes when closing inspection', async () => {
  const fetcher = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
  const resource = createExampleRecordStandardCrudResource(fetcher);
  expect(resource.actionInputRenderers?.objectAuthorization).toBeTypeOf('function');
  expect(resource.actionInputRenderers?.objectAuthorizationDetails).toBeTypeOf('function');
  for (const key of ['authorize', 'viewAuthorization']) {
    const signal = new AbortController().signal;
    expect(await resource.actionEligibilityResolvers?.[key]?.({ id: 'record/1', featurePosition: key,
      signal, action: { key, labelKey: key } })).toEqual({ visible: true, available: true });
    expect(fetcher).toHaveBeenLastCalledWith('/api/authorization/data-scope/objects/example.record/record%2F1/eligibility',
      { headers: { 'X-NewP-Feature': key }, signal });
  }
  fetcher.mockClear();
  expect(resource.actionInputRenderers?.objectAuthorizationDetails.readOnly).toBe(true);
  expect(fetcher).not.toHaveBeenCalled();
  const input = { resourceKey: 'example.record', objectId: 'record/1', subjects: [] };
  await resource.actionHandlers?.authorize({ id: 'record/1', featurePosition: 'manage', input });
  expect(fetcher).toHaveBeenLastCalledWith('/api/authorization/data-scope/objects/example.record/record%2F1',
    { method: 'PUT', headers: { 'Content-Type': 'application/json', 'X-NewP-Feature': 'manage' }, body: JSON.stringify(input) });
  await expect(resource.actionHandlers?.authorize({ id: 'wrong', featurePosition: 'manage', input })).rejects.toThrow('目标已变化');
});

it('hides denied object actions and surfaces eligibility failures', async () => {
  const fetcher = vi.fn().mockResolvedValue(new Response(null, { status: 403 }));
  const resource = createExampleRecordStandardCrudResource(fetcher);
  const context = { id: 'record', featurePosition: 'manage', signal: new AbortController().signal,
    action: { key: 'authorize', labelKey: '授权' } };
  expect(await resource.actionEligibilityResolvers?.authorize?.(context)).toEqual({ visible: false, available: false });
  fetcher.mockResolvedValue(new Response(null, { status: 500 }));
  await expect(resource.actionEligibilityResolvers?.authorize?.(context)).rejects.toThrow('资格读取失败');
});
