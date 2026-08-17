import { describe, expect, it } from 'vitest';
import identityFrontendBindings from '../index';

describe('Identity frontend capability bindings', () => {
  it('publishes only lazy standard account list and detail capabilities', async () => {
    expect(identityFrontendBindings.customPages).toBeUndefined();
    expect(identityFrontendBindings.routeCapabilities).toBeUndefined();
    expect(identityFrontendBindings.standardCrudCapabilities?.map(({ kind, targetKey }) => ({ kind, targetKey }))).toEqual([
      { kind: 'standard-list', targetKey: 'identity.account.list' },
      { kind: 'standard-crud-detail', targetKey: 'identity.account.detail' },
    ]);

    const loaded = await identityFrontendBindings.standardCrudCapabilities?.[0]?.load();
    const loadedDetail = await identityFrontendBindings.standardCrudCapabilities?.[1]?.load();
    expect(loaded?.default).toMatchObject({
      resourceKey: 'identity.account',
      apiRoute: '/api/identity/accounts',
      listViewKey: 'identity.account.list',
      detailViewKey: 'identity.account.detail',
    });
    expect(loadedDetail?.default).toBe(loaded?.default);
  });
});
