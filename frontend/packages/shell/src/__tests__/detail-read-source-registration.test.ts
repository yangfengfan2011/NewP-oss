import { expect, it } from 'vitest';
import { validateCompositionCandidate } from '../composition/compositionRegistry';
import type { FrontendModule } from '@icp-platform/sdk';
import { loadActiveModuleRuntimeWith } from '../active-module-runtime-loader';

it('preserves a declared read-only source and rejects malformed source descriptors before publication', async () => {
  const source = { key: 'source.properties', resourceKeys: ['part.part'], get: async () => ({ value: { fields: {}, summaries: {} }, normalized: { fields: {}, summaries: {} } }) };
  const load = (sources: unknown[]) => loadActiveModuleRuntimeWith({
    fetchCatalog: async () => ({ ok: true, status: 200, json: async () => ({ schemaVersion: '1.0', snapshotId: 'read-source', planHash: 'read-source',
      modules: [{ moduleId: 'Source', displayName: 'Source', version: '1', dependencies: [],
        frontend: { moduleId: 'source', behaviorKey: 'source.frontend', bindingMode: 'behavior-bindings', bindings: ['source.properties'] } }] }) }),
    internalModules: { source: async () => ({ default: { behaviorKey: 'source.frontend', standardCrudDetailReadSources: sources } }) },
  });
  const result = await load([source]);
  expect(result.modules[0].standardCrudDetailReadSources).toEqual([source]);
  for (const invalid of [{ ...source, resourceKeys: [] }, { ...source, resourceKeys: ['part.part', 'part.part'] }, { ...source, get: null }])
    await expect(load([invalid])).rejects.toThrow('detail read source');
  await expect(load([source, source])).rejects.toThrow('duplicate');
});

it('composition rejects unknown sources and mismatched target resources without changing ordinary detail pages', () => {
  const modules: FrontendModule[] = [{ metadata: { moduleId: 'Source', displayName: 'Source', version: '1' },
    standardCrudDetailReadSources: [{ key: 'source.properties', resourceKeys: ['part.part'], get: async () => { throw new Error('must not read during validation'); } }] }];
  const metadata = (sourceKey?: string, resourceKey = 'part.part') => ({ manifest: { modules: [] }, modelDocuments: [],
    viewDocuments: [{ type: 'view' as const, view: { key: 'part.detail', mode: 'detail', resourceKey } }],
    navDocuments: [{ type: 'nav' as const, navigation: { items: [{ key: 'source.entry', page: { pageKey: 'platform.detail', parameters: {
      viewKey: 'part.detail', ...(sourceKey ? { detailReadSourceKey: sourceKey } : {}),
    } } }] } }],
  });
  expect(() => validateCompositionCandidate(modules, metadata())).not.toThrow();
  expect(() => validateCompositionCandidate(modules, metadata('source.properties'))).not.toThrow();
  expect(() => validateCompositionCandidate(modules, metadata('unknown'))).toThrow('detail read source');
  expect(() => validateCompositionCandidate(modules, metadata('source.properties', 'document.document'))).toThrow('detail read source');
});
