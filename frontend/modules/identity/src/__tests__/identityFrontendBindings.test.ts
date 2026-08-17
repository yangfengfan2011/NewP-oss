import { describe, expect, it } from 'vitest';
import identityFrontendBindings from '../index';

describe('Identity frontend capability bindings', () => {
  it('keeps the production custom route and publishes a lazy standard account list candidate', async () => {
    expect(identityFrontendBindings.customPages).toBeUndefined();
    expect(identityFrontendBindings.routeCapabilities?.map(({ kind, targetKey }) => ({ kind, targetKey }))).toEqual([
      { kind: 'custom', targetKey: 'identity.accounts.page' },
    ]);
    expect(identityFrontendBindings.routeCapabilities?.[0]?.load).toEqual(expect.any(Function));
    expect(identityFrontendBindings.standardCrudCapabilities?.map(({ kind, targetKey }) => ({ kind, targetKey }))).toEqual([
      { kind: 'standard-list', targetKey: 'identity.account.list' },
    ]);

    const loaded = await identityFrontendBindings.standardCrudCapabilities?.[0]?.load();
    expect(loaded?.default).toMatchObject({
      resourceKey: 'identity.account',
      apiRoute: '/api/identity/accounts',
      listViewKey: 'identity.account.list',
    });
  });
});
