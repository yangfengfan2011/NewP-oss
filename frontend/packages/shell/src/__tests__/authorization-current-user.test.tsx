// @vitest-environment jsdom

import React, { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  AuthorizationProvider,
  useAuthorization,
} from '../../../core/src/authorization/AuthorizationContext';

function PermissionProbe() {
  const authorization = useAuthorization();
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
    vi.unstubAllGlobals();
  });

  it('loads permissions only from the current-user endpoint', async () => {
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
    expect(fetchMock).not.toHaveBeenCalledWith(expect.stringContaining('/authorization/users/'), expect.anything());
    expect(container.textContent).toBe('Organization.Person.View');
  });
});
