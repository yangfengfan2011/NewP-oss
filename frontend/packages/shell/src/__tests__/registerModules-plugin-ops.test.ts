import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { FrontendModule } from '@icp-platform/sdk';
import { createStandardCrudTestDataAdapter } from './standardCrudTestDataAdapter';

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
    vi.doMock('../metadata/runtimeMetadataLoader', () => ({
      loadRuntimeMetadata: vi.fn().mockResolvedValue({
        manifest: { modules: [] },
        navDocuments: [],
        viewDocuments: [],
        modelDocuments: [],
      }),
      setPreloadedRuntimeMetadata: vi.fn(),
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
    vi.doMock('../metadata/runtimeMetadataLoader', () => ({
      loadRuntimeMetadata: vi.fn().mockResolvedValue({
        manifest: { modules: [] },
        navDocuments: [],
        viewDocuments: [],
        modelDocuments: [],
      }),
      setPreloadedRuntimeMetadata: vi.fn(),
    }));
    vi.doMock('@icp-platform/sdk', () => ({
      ModuleRegistry: { replaceAll },
    }));

    const { registerShellModules } = await import('../registerModules');

    await expect(registerShellModules()).rejects.toThrow('MODULE_PROJECTION_FAILED');
    expect(replaceAll).not.toHaveBeenCalled();
  });

  it('rejects an invalid detail view candidate before replacing the active snapshot', async () => {
    let activeSnapshotId = 'snapshot-active';
    const replaceAll = vi.fn((_modules: FrontendModule[], snapshotId: string) => {
      activeSnapshotId = snapshotId;
    });
    vi.doMock('../active-module-runtime-loader', () => ({
      loadActiveModuleRuntime: vi.fn().mockResolvedValue({
        snapshotId: 'snapshot-invalid',
        planHash: 'plan-invalid',
        modules: [{
          metadata: {
            moduleId: 'Organization',
            displayName: '组织结构',
            version: '1.0.0',
          },
          standardCrudResources: [{
            resourceKey: 'organization.person',
            dataAdapter: createStandardCrudTestDataAdapter('Person_1'),
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
          }],
        } satisfies FrontendModule],
      }),
    }));
    vi.doMock('../metadata/runtimeMetadataLoader', () => ({
      loadRuntimeMetadata: vi.fn().mockResolvedValue({
        manifest: { modules: [] },
        navDocuments: [],
        viewDocuments: [],
        modelDocuments: [],
      }),
      setPreloadedRuntimeMetadata: vi.fn(),
    }));
    vi.doMock('@icp-platform/sdk', () => ({
      ModuleRegistry: { replaceAll },
    }));

    const { registerShellModules } = await import('../registerModules');

    await expect(registerShellModules()).rejects.toThrow(
      'detail view "organization.person.detail" is missing',
    );
    expect(replaceAll).not.toHaveBeenCalled();
    expect(activeSnapshotId).toBe('snapshot-active');
  });

  it.each([
    {
      name: 'retired form.view metadata',
      fieldExtension: { form: { view: { visible: true } } },
      registrationExtension: {},
      diagnostic: 'Person.ENAME.form.view',
    },
    {
      name: 'retired runtime detailActions registration',
      fieldExtension: {},
      registrationExtension: { detailActions: [] },
      diagnostic: 'registration property "detailActions"',
    },
  ])('rejects $name without replacing the active snapshot', async ({
    fieldExtension,
    registrationExtension,
    diagnostic,
  }) => {
    let activeSnapshotId = 'snapshot-active';
    const replaceAll = vi.fn((_modules: FrontendModule[], snapshotId: string) => {
      activeSnapshotId = snapshotId;
    });
    vi.doMock('../active-module-runtime-loader', () => ({
      loadActiveModuleRuntime: vi.fn().mockResolvedValue({
        snapshotId: 'snapshot-invalid',
        planHash: 'plan-invalid',
        modules: [{
          metadata: {
            moduleId: 'Organization',
            displayName: '组织结构',
            version: '1.0.0',
          },
          standardCrudResources: [{
            resourceKey: 'organization.person',
            dataAdapter: createStandardCrudTestDataAdapter('Person_1'),
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
            ...registrationExtension,
          }],
        } as unknown as FrontendModule],
      }),
    }));
    vi.doMock('../metadata/runtimeMetadataLoader', () => ({
      loadRuntimeMetadata: vi.fn().mockResolvedValue({
        manifest: { modules: [] },
        navDocuments: [],
        viewDocuments: [{
          type: 'view',
          view: {
            key: 'organization.person.detail',
            mode: 'detail',
            model: 'Person',
            sections: [{ key: 'basic', titleKey: 'Section_Basic', fields: ['ENAME'] }],
          },
        }],
        modelDocuments: [{
          type: 'model',
          Person: {
            fields: [{
              name: 'ENAME',
              storage: { type: 'string' },
              uiType: 'text',
              ...fieldExtension,
            }],
          },
        }],
      }),
      setPreloadedRuntimeMetadata: vi.fn(),
    }));
    vi.doMock('@icp-platform/sdk', () => ({
      ModuleRegistry: { replaceAll },
    }));

    const { registerShellModules } = await import('../registerModules');

    await expect(registerShellModules()).rejects.toThrow(diagnostic);
    expect(replaceAll).not.toHaveBeenCalled();
    expect(activeSnapshotId).toBe('snapshot-active');
  });
});
