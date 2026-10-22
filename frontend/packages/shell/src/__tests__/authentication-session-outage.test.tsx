import { LocalizationProvider } from '@icp-platform/core';
import React, { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AuthenticationSessionBoundary } from '../components/AuthenticationSessionBoundary';

describe('Shell authentication session boundary', () => {
  let root: Root | undefined;
  let container: HTMLDivElement | undefined;

  afterEach(async () => {
    if (root) await act(async () => root?.unmount());
    container?.remove();
  });

  it('renders a retryable outage instead of the anonymous branch when session validation is unavailable', async () => {
    const retry = vi.fn().mockResolvedValue(undefined);
    container = document.createElement('div');
    document.body.append(container);

    await act(async () => {
      root = createRoot(container!);
      root.render(
        <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: "zhs", displayName: "简体中文" }]} catalogs={{}}>
        <AuthenticationSessionBoundary
          authentication={{
            isInitialized: true,
            sessionError: 'AUTH_SESSION_STORE_UNAVAILABLE',
            retrySessionValidation: retry,
          }}
        >
          <span>anonymous-login</span>
        </AuthenticationSessionBoundary></LocalizationProvider>,
      );
    });

    expect(container.textContent).toContain('认证服务暂时不可用');
    expect(container.textContent).toContain('AUTH_SESSION_STORE_UNAVAILABLE');
    expect(container.textContent).not.toContain('anonymous-login');

    await act(async () => {
      (container!.querySelector('button') as HTMLButtonElement).click();
    });
    expect(retry).toHaveBeenCalledOnce();
  });
});
