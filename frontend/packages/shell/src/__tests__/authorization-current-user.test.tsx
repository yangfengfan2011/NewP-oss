// @vitest-environment jsdom

import React, { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  AuthorizationProvider,
  useAuthorization,
  type AuthorizationContextValue,
} from '../../../core/src/authorization/AuthorizationContext';

let observedAuthorization: AuthorizationContextValue | undefined;

function PermissionProbe() {
  const authorization = useAuthorization();
  observedAuthorization = authorization;
  return <span>{authorization.permissions.join(',')}</span>;
}

describe('AuthorizationProvider current subject projection', () => {
  let root: Root | undefined;
  let container: HTMLDivElement | undefined;

  afterEach(async () => {
    if (root) {
      await act(async () => root?.unmount());
    }

    container?.remove();
    observedAuthorization = undefined;
    vi.unstubAllGlobals();
  });

  it('projects permissions from authentication without owning another request', async () => {
    const fetchMock = vi.fn();
    const refreshPermissions = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal('fetch', fetchMock);
    container = document.createElement('div');
    document.body.append(container);

    await act(async () => {
      root = createRoot(container!);
      root.render(
        <AuthorizationProvider
          permissions={['Organization.Person.View']}
          refreshPermissions={refreshPermissions}
        >
          <PermissionProbe />
        </AuthorizationProvider>,
      );
    });

    expect(fetchMock).not.toHaveBeenCalled();
    expect(container.textContent).toBe('Organization.Person.View');

    await act(async () => observedAuthorization?.refreshPermissions());
    expect(refreshPermissions).toHaveBeenCalledOnce();
  });

  it('keeps the legacy current-user loading contract when no snapshot is supplied', async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({
      id: 'current-subject',
      permissions: ['Organization.Person.View'],
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }));
    vi.stubGlobal('fetch', fetchMock);
    container = document.createElement('div');
    document.body.append(container);

    await act(async () => {
      root = createRoot(container!);
      root.render(
        <AuthorizationProvider isAuthenticated>
          <PermissionProbe />
        </AuthorizationProvider>,
      );
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(fetchMock).toHaveBeenCalledWith('/api/auth/current-user', expect.objectContaining({
      credentials: 'include',
    }));
    expect(container.textContent).toBe('Organization.Person.View');
  });
});
