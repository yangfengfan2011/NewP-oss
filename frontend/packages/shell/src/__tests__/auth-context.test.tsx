import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { AuthProvider, useAuth, useOptionalAuth } from '../contexts/AuthContext';

function OptionalTimeZone() {
  const authentication = useOptionalAuth();
  return <span>{authentication?.user?.timeZoneId ?? 'default-time-zone'}</span>;
}

function RequiredAuthentication() {
  useAuth();
  return null;
}

describe('Shell authentication context', () => {
  it('allows optional consumers to render without a provider', () => {
    expect(renderToStaticMarkup(<OptionalTimeZone />)).toBe('<span>default-time-zone</span>');
  });

  it('passes the session time zone to optional consumers', () => {
    const markup = renderToStaticMarkup(
      <AuthProvider initialUser={{
        id: 'user-1',
        ecode: 'user',
        ename: 'User',
        permissions: [],
        mustChangePassword: false,
        timeZoneId: 'Asia/Taipei',
      }}>
        <OptionalTimeZone />
      </AuthProvider>,
    );
    expect(markup).toBe('<span>Asia/Taipei</span>');
  });

  it('still rejects required consumers without a provider', () => {
    expect(() => renderToStaticMarkup(<RequiredAuthentication />))
      .toThrow('useAuth must be used within AuthProvider');
  });
});
