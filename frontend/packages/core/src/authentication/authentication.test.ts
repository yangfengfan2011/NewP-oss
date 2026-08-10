import { describe, expect, it } from 'vitest';
import { resolveAuthenticationDestination } from './index';

describe('restricted authentication routing', () => {
  it('keeps a restricted session inside the first-password-change route', () => {
    expect(resolveAuthenticationDestination(true, '/identity/users')).toBe('/account/change-password');
    expect(resolveAuthenticationDestination(true, '/account/change-password')).toBe('/account/change-password');
  });

  it('keeps a regular session out of the first-password-change route', () => {
    expect(resolveAuthenticationDestination(false, '/account/change-password')).toBe('/');
    expect(resolveAuthenticationDestination(false, '/identity/users')).toBe('/identity/users');
  });
});
