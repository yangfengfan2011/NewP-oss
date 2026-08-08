import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { FrontendModule } from '@icp-platform/sdk';

describe('active module registry commit', () => {
  beforeEach(() => {
    vi.resetModules();
  });

  it('commits all active modules through one atomic registry replacement', async () => {
    const modules: FrontendModule[] = [
      {
        metadata: {
          moduleId: 'Organization',
          displayName: '组织结构',
          version: '1.0.0',
        },
      },
      {
        metadata: {
          moduleId: 'PluginOps',
          displayName: '插件运维',
          version: '1.0.0',
        },
      },
    ];
    const replaceAll = vi.fn();
    vi.doMock('../active-module-runtime-loader', () => ({
      loadActiveModuleRuntime: vi.fn().mockResolvedValue({
        snapshotId: 'snapshot-1',
        planHash: 'snapshot-1',
        modules,
      }),
    }));
    vi.doMock('@icp-platform/sdk', () => ({
      ModuleRegistry: { replaceAll },
    }));

    const { registerShellModules } = await import('../registerModules');
    await registerShellModules();

    expect(replaceAll).toHaveBeenCalledTimes(1);
    expect(replaceAll).toHaveBeenCalledWith(modules, 'snapshot-1');
  });

  it('does not mutate the registry when a required module fails to load', async () => {
    const replaceAll = vi.fn();
    vi.doMock('../active-module-runtime-loader', () => ({
      loadActiveModuleRuntime: vi.fn().mockRejectedValue(
        new Error('MODULE_PROJECTION_FAILED: Organization'),
      ),
    }));
    vi.doMock('@icp-platform/sdk', () => ({
      ModuleRegistry: { replaceAll },
    }));

    const { registerShellModules } = await import('../registerModules');

    await expect(registerShellModules()).rejects.toThrow('MODULE_PROJECTION_FAILED');
    expect(replaceAll).not.toHaveBeenCalled();
  });
});
