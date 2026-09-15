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
  return <span>{authorization.featurePositions.join(',')}</span>;
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

  it('projects feature positions from authentication without owning another request', async () => {
    const fetchMock = vi.fn();
    const refreshFeatures = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal('fetch', fetchMock);
    container = document.createElement('div');
    document.body.append(container);

    await act(async () => {
      root = createRoot(container!);
      root.render(
        <AuthorizationProvider
          featurePositions={['read']}
          refreshFeatures={refreshFeatures}
        >
          <PermissionProbe />
        </AuthorizationProvider>,
      );
    });

    expect(fetchMock).not.toHaveBeenCalled();
    expect(container.textContent).toBe('read');

    await act(async () => observedAuthorization?.refreshFeatures());
    expect(refreshFeatures).toHaveBeenCalledOnce();
  });

  it('projects live feature positions separately and never substitutes permission codes', async () => {
    container = document.createElement('div'); document.body.append(container); root = createRoot(container);
    for (const positions of [undefined, ['read', 'save'], ['read'], ['read', 'save']]) {
      await act(async () => root!.render(<AuthorizationProvider featurePositions={positions}><PermissionProbe /></AuthorizationProvider>));
      expect(observedAuthorization!.hasFeature('save')).toBe(positions?.includes('save') ?? false);
    }
  });

  it('denies every feature when no authentication snapshot is supplied without issuing a request', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    container = document.createElement('div'); document.body.append(container);
    await act(async () => {
      root = createRoot(container!);
      root.render(<AuthorizationProvider><PermissionProbe /></AuthorizationProvider>);
    });
    expect(fetchMock).not.toHaveBeenCalled();
    expect(observedAuthorization?.hasFeature('save')).toBe(false);
  });
});
