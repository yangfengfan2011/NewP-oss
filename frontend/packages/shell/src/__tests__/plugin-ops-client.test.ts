import { beforeEach, describe, expect, it, vi } from 'vitest';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      'Content-Type': 'application/json',
    },
  });
}

describe('pluginOperationsClient', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('exposes the API methods expected by the operations center', async () => {
    // 这个测试先锁住客户端对外暴露的最小 API 面，
    // 防止页面层还没报错之前，导出名漂移就已经把运维入口悄悄掐断。
    const api = await import('../../../../modules/plugin-ops/src/api/pluginOperationsClient');
    const exports = api as Record<string, unknown>;

    expect(typeof exports.getModules).toBe('function');
    expect(typeof exports.verifyUpload).toBe('function');
    expect(typeof exports.installUpload).toBe('function');
    expect(typeof exports.enableModule).toBe('function');
    expect(typeof exports.disableModule).toBe('function');
    expect(typeof exports.uninstallModule).toBe('function');
    expect(typeof exports.rollbackModule).toBe('function');
  });

  it('loads module statuses from the host API', async () => {
    // 列表查询必须走宿主 API 且携带登录态 cookie；
    // 这是页面能拿到真实运行时状态的基础前提。
    const api = await import('../../../../modules/plugin-ops/src/api/pluginOperationsClient');
    const getModules = (api as Record<string, unknown>).getModules as
      | ((fetcher?: typeof fetch) => Promise<unknown>)
      | undefined;
    const modules = [
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
    ];
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(jsonResponse(modules));

    expect(typeof getModules).toBe('function');
    await expect(getModules?.(fetcher)).resolves.toEqual(modules);
    expect(fetcher).toHaveBeenCalledWith('/api/plugin-ops/modules', {
      credentials: 'include',
    });
  });

  it('posts uploaded packages for verification and keeps validation payloads on bad requests', async () => {
    // 校验接口即使返回 400，也可能带有结构化校验结果；
    // 客户端不能一律抛异常，否则页面无法展示“为什么这个包不合法”。
    const api = await import('../../../../modules/plugin-ops/src/api/pluginOperationsClient');
    const verifyUpload = (api as Record<string, unknown>).verifyUpload as
      | ((file: File, fetcher?: typeof fetch) => Promise<unknown>)
      | undefined;
    const payload = {
      uploadId: 'upload-1',
      moduleId: 'Audit',
      displayName: '审计日志',
      version: '1.1.0',
      isValid: false,
      errors: ['manifest 缺少 entry.js'],
    };
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(jsonResponse(payload, 400));
    const file = new File(['zip'], 'audit.zip', { type: 'application/zip' });

    expect(typeof verifyUpload).toBe('function');
    await expect(verifyUpload?.(file, fetcher)).resolves.toEqual(payload);
    expect(fetcher).toHaveBeenCalledWith(
      '/api/plugin-ops/uploads/verify',
      expect.objectContaining({
        method: 'POST',
        body: expect.any(FormData),
        credentials: 'include',
      }),
    );
  });

  it('sends lifecycle operations to the dedicated endpoints', async () => {
    // 生命周期动作必须各自命中独立端点；
    // 这个测试保护的是前端路由契约，避免启用/禁用/卸载/回滚串线。
    const api = await import('../../../../modules/plugin-ops/src/api/pluginOperationsClient');
    const installUpload = (api as Record<string, unknown>).installUpload as
      | ((uploadId: string, fetcher?: typeof fetch) => Promise<unknown>)
      | undefined;
    const enableModule = (api as Record<string, unknown>).enableModule as
      | ((moduleId: string, version: string, fetcher?: typeof fetch) => Promise<unknown>)
      | undefined;
    const disableModule = (api as Record<string, unknown>).disableModule as
      | ((moduleId: string, fetcher?: typeof fetch) => Promise<unknown>)
      | undefined;
    const uninstallModule = (api as Record<string, unknown>).uninstallModule as
      | ((moduleId: string, fetcher?: typeof fetch) => Promise<unknown>)
      | undefined;
    const rollbackModule = (api as Record<string, unknown>).rollbackModule as
      | ((moduleId: string, fetcher?: typeof fetch) => Promise<unknown>)
      | undefined;
    const fetcher = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(jsonResponse({ moduleId: 'Audit', version: '1.1.0', status: 'Installed', message: 'ok' }))
      .mockResolvedValueOnce(jsonResponse({ moduleId: 'Audit', version: '1.1.0', status: 'Enabled', message: 'ok' }))
      .mockResolvedValueOnce(jsonResponse({ moduleId: 'Audit', version: null, status: 'Disabled', message: 'ok' }))
      .mockResolvedValueOnce(jsonResponse({ moduleId: 'Audit', version: null, status: 'Uninstalled', message: 'ok' }))
      .mockResolvedValueOnce(jsonResponse({ moduleId: 'Audit', version: '1.0.0', status: 'RolledBack', message: 'ok' }));

    expect(typeof installUpload).toBe('function');
    expect(typeof enableModule).toBe('function');
    expect(typeof disableModule).toBe('function');
    expect(typeof uninstallModule).toBe('function');
    expect(typeof rollbackModule).toBe('function');

    await installUpload?.('upload-1', fetcher);
    await enableModule?.('Audit', '1.1.0', fetcher);
    await disableModule?.('Audit', fetcher);
    await uninstallModule?.('Audit', fetcher);
    await rollbackModule?.('Audit', fetcher);

    expect(fetcher).toHaveBeenNthCalledWith(
      1,
      '/api/plugin-ops/uploads/upload-1/install',
      expect.objectContaining({
        method: 'POST',
        credentials: 'include',
      }),
    );
    expect(fetcher).toHaveBeenNthCalledWith(
      2,
      '/api/plugin-ops/modules/Audit/enable',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ version: '1.1.0' }),
        credentials: 'include',
      }),
    );
    expect(fetcher).toHaveBeenNthCalledWith(
      3,
      '/api/plugin-ops/modules/Audit/disable',
      expect.objectContaining({
        method: 'POST',
        credentials: 'include',
      }),
    );
    expect(fetcher).toHaveBeenNthCalledWith(
      4,
      '/api/plugin-ops/modules/Audit',
      expect.objectContaining({
        method: 'DELETE',
        credentials: 'include',
      }),
    );
    expect(fetcher).toHaveBeenNthCalledWith(
      5,
      '/api/plugin-ops/modules/Audit/rollback',
      expect.objectContaining({
        method: 'POST',
        credentials: 'include',
      }),
    );
  });
});
