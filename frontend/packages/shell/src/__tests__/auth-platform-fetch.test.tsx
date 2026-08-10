// @vitest-environment jsdom

import React, { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import type { AuthContext as AuthContextValue } from '@icp-platform/sdk';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const platformFetchMock = vi.hoisted(() => vi.fn());
const invalidateTokenMock = vi.hoisted(() => vi.fn());

vi.mock('@icp-platform/core', () => ({
  invalidatePlatformAntiforgeryToken: invalidateTokenMock,
  platformFetch: platformFetchMock,
}));

import { AuthProvider, useAuth } from '../contexts/AuthContext';

let observedAuth: AuthContextValue | undefined;

function AuthProbe() {
  observedAuth = useAuth();
  return null;
}

describe('AuthProvider platformFetch integration', () => {
  let root: Root | undefined;
  let container: HTMLDivElement | undefined;

  beforeEach(() => {
    observedAuth = undefined;
    platformFetchMock.mockReset();
    invalidateTokenMock.mockReset();
  });

  afterEach(async () => {
    if (root) {
      await act(async () => root?.unmount());
    }
    root = undefined;
    container?.remove();
    container = undefined;
  });

  it('uses the sole platform seam and discards tokens across login and logout subjects', async () => {
    platformFetchMock
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(new Response(null, { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        id: 'User_1',
        username: 'admin',
        displayName: 'Administrator',
        permissions: [],
        mustChangePassword: false,
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    container = document.createElement('div');
    document.body.append(container);

    await act(async () => {
      root = createRoot(container!);
      root.render(<AuthProvider><AuthProbe /></AuthProvider>);
      await Promise.resolve();
      await Promise.resolve();
    });
    await act(async () => observedAuth?.login('admin', 'secret'));
    await act(async () => observedAuth?.logout());

    expect(platformFetchMock).toHaveBeenNthCalledWith(
      2,
      '/api/auth/login',
      expect.objectContaining({ method: 'POST' }),
    );
    expect(platformFetchMock).toHaveBeenNthCalledWith(
      4,
      '/api/auth/logout',
      expect.objectContaining({ method: 'POST' }),
    );
    expect(invalidateTokenMock).toHaveBeenCalledTimes(2);
  });
});
