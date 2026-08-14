import { describe, expect, it } from 'vitest';
import identityFrontendBindings from '../index';

describe('Identity frontend capability bindings', () => {
  it('publishes Accounts as a lazy route capability descriptor without loading it during registration', () => {
    expect(identityFrontendBindings.customPages).toBeUndefined();
    expect(identityFrontendBindings.routeCapabilities?.map(({ kind, targetKey }) => ({ kind, targetKey }))).toEqual([
      { kind: 'custom', targetKey: 'identity.accounts.page' },
    ]);
    expect(identityFrontendBindings.routeCapabilities?.[0]?.load).toEqual(expect.any(Function));
  });
});
