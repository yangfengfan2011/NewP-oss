// @vitest-environment jsdom

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import PluginOperationsCenter from '../../../../modules/plugin-ops/src/pages/PluginOperationsCenter';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      'Content-Type': 'application/json',
    },
  });
}

async function flushUi(): Promise<void> {
  await act(async () => {
    await Promise.resolve();
  });
}

describe('PluginOperationsCenter', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    globalThis.IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.appendChild(container);
    vi.stubGlobal('fetch', vi.fn());
  });

  afterEach(async () => {
    if (root) {
      await act(async () => {
        root.unmount();
      });
    }

    container.remove();
    vi.unstubAllGlobals();
    globalThis.IS_REACT_ACT_ENVIRONMENT = false;
  });

  it('loads module statuses, verifies uploads, and refreshes the list after install', async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock
      .mockResolvedValueOnce(jsonResponse([
        {
          moduleId: 'Audit',
          displayName: '审计日志',
          installedVersions: ['1.0.0'],
          currentEnabledVersion: '1.0.0',
          status: 'Enabled',
          lastOperation: 'Enable',
          lastError: null,
          canEnable: true,
          canDisable: true,
          canUninstall: false,
          canRollback: false,
        },
      ]))
      .mockResolvedValueOnce(jsonResponse({
        uploadId: 'upload-1',
        moduleId: 'Audit',
        displayName: '审计日志',
        version: '1.1.0',
        isValid: true,
        errors: [],
      }))
      .mockResolvedValueOnce(jsonResponse({
        moduleId: 'Audit',
        version: '1.1.0',
        status: 'Installed',
        message: '安装成功',
      }))
      .mockResolvedValueOnce(jsonResponse([
        {
          moduleId: 'Audit',
          displayName: '审计日志',
          installedVersions: ['1.0.0', '1.1.0'],
          currentEnabledVersion: '1.0.0',
          status: 'Installed',
          lastOperation: 'Install',
          lastError: null,
          canEnable: true,
          canDisable: true,
          canUninstall: false,
          canRollback: true,
        },
      ]));

    await act(async () => {
      root = createRoot(container);
      root.render(<PluginOperationsCenter />);
    });
    await flushUi();

    expect(container.textContent).toContain('审计日志');
    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/plugin-ops/modules', {
      credentials: 'include',
    });

    const fileInput = container.querySelector('input[type="file"]') as HTMLInputElement | null;
    expect(fileInput).not.toBeNull();
    const file = new File(['zip'], 'audit.zip', { type: 'application/zip' });
    Object.defineProperty(fileInput, 'files', {
      configurable: true,
      value: [file],
    });

    await act(async () => {
      fileInput?.dispatchEvent(new Event('change', { bubbles: true }));
    });

    const verifyButton = Array.from(container.querySelectorAll('button')).find(
      button => button.textContent?.includes('校验'),
    );
    expect(verifyButton).toBeDefined();

    await act(async () => {
      verifyButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushUi();

    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      '/api/plugin-ops/uploads/verify',
      expect.objectContaining({
        method: 'POST',
        body: expect.any(FormData),
        credentials: 'include',
      }),
    );
    expect(container.textContent).toContain('upload-1');
    expect(container.textContent).toContain('1.1.0');

    const installButton = Array.from(container.querySelectorAll('button')).find(
      button => button.textContent?.includes('安装已校验包'),
    );
    expect(installButton).toBeDefined();

    await act(async () => {
      installButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushUi();

    expect(fetchMock).toHaveBeenNthCalledWith(
      3,
      '/api/plugin-ops/uploads/upload-1/install',
      expect.objectContaining({
        method: 'POST',
        credentials: 'include',
      }),
    );
    expect(fetchMock).toHaveBeenNthCalledWith(4, '/api/plugin-ops/modules', {
      credentials: 'include',
    });
    expect(container.textContent).toContain('安装成功');
    expect(container.textContent).toContain('1.0.0, 1.1.0');
  });
  it('shows warning tone for success messages and exposes uninstall actions', async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock
      .mockResolvedValueOnce(jsonResponse([
        {
          moduleId: 'Cleanup',
          displayName: '娓呯悊妯″潡',
          installedVersions: ['1.0.0'],
          currentEnabledVersion: null,
          status: 'Installed',
          lastOperation: 'Install',
          lastError: null,
          canEnable: true,
          canDisable: false,
          canUninstall: true,
          canRollback: false,
        },
      ]))
      .mockResolvedValueOnce(jsonResponse({
        moduleId: 'Cleanup',
        version: null,
        status: 'Uninstalled',
        message: 'Uninstalled with warning: other installed plugins still depend on Cleanup.',
      }))
      .mockResolvedValueOnce(jsonResponse([
        {
          moduleId: 'Cleanup',
          displayName: '娓呯悊妯″潡',
          installedVersions: ['1.0.0'],
          currentEnabledVersion: null,
          status: 'Installed',
          lastOperation: 'Install',
          lastError: null,
          canEnable: true,
          canDisable: false,
          canUninstall: true,
          canRollback: false,
        },
      ]));

    await act(async () => {
      root = createRoot(container);
      root.render(<PluginOperationsCenter />);
    });
    await flushUi();

    const uninstallButton = Array.from(container.querySelectorAll('button')).find(
      button => button.textContent?.includes('卸载'),
    );
    expect(uninstallButton).toBeDefined();

    await act(async () => {
      uninstallButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushUi();

    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      '/api/plugin-ops/modules/Cleanup',
      expect.objectContaining({
        method: 'DELETE',
        credentials: 'include',
      }),
    );

    const notice = container.querySelector('p[data-tone]');
    expect(notice?.getAttribute('data-tone')).toBe('warning');
    expect(container.textContent).toContain('Uninstalled with warning');
  });
});
