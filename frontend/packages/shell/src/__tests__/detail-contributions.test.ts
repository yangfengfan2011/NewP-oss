import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { discoverFrontendEntries } from '../../../../scripts/component-sources.mjs';
import { expect, it } from 'vitest';
import type { FrontendModule } from '@icp-platform/sdk';
import type { RuntimeMetadataState } from '../metadata/runtimeMetadataManifest';
import { validateCompositionCandidate } from '../composition/compositionRegistry';
import { resolveDetailContributions } from '../standard-crud/detailContributionRegistry';

const frontendEntries = Object.values(discoverFrontendEntries(resolve(__dirname, '../../../../..')));
const componentRoots = frontendEntries.map(entryPath => dirname(dirname(entryPath)))
  .filter(componentRoot => existsSync(resolve(componentRoot, 'newp.plugin.json')));
// 正式发现拥有源码位置，测试只加载所需真实 owner 的 binding，不固化组合嵌套深度。
const bindings = import.meta.glob<{ default: Omit<FrontendModule, 'metadata'> }>('../../../../modules/**/src/index.ts');
const modules: FrontendModule[] = await Promise.all(['Workspace', 'Part', 'Document'].map(async id => {
  const root = componentRoots.find(path => JSON.parse(readFileSync(resolve(path, 'newp.plugin.json'), 'utf8')).moduleId === id);
  const entry = frontendEntries.find(path => dirname(dirname(path)) === root);
  const loader = Object.entries(bindings).find(([path]) => resolve(__dirname, path) === resolve(entry ?? ''))?.[1];
  if (!loader) throw new Error(`Frontend binding '${id}' was not found through the component catalog.`);
  return { ...(await loader()).default, metadata: { moduleId: id, displayName: id, version: '1' } };
}));
function metadata(): RuntimeMetadataState {
  const read = (module: string, folder: string) => {
    const moduleRoot = componentRoots.find(componentRoot => JSON.parse(readFileSync(resolve(componentRoot, 'newp.plugin.json'), 'utf8')).moduleId === module);
    if (!moduleRoot) throw new Error(`Authoring component '${module}' was not found.`);
    return readdirSync(resolve(moduleRoot, 'metadata', folder)).map(file =>
      JSON.parse(readFileSync(resolve(moduleRoot, 'metadata', folder, file), 'utf8')));
  };
  const navDocuments = modules.flatMap(module => read(module.metadata.moduleId, 'navigation')).map(document => ({ ...document,
    navigation: { items: document.navigation.items.map((item: { key: string }) => ({ ...item, featurePosition: `${item.key}-entry` })) } }));
  return { manifest: { modules: modules.map(module => ({ moduleId: module.metadata.moduleId, metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [],
    features: navDocuments.flatMap(document => document.navigation.items).filter(item => read(module.metadata.moduleId, 'navigation').some(document => document.navigation.items.some((nav: { key: string }) => nav.key === item.key)))
      .map(item => ({ key: item.key, position: item.featurePosition, kind: 'navigation' })),
  })) }, navDocuments, modelDocuments: [], viewDocuments: modules.flatMap(module => read(module.metadata.moduleId, 'views')) };
}

it('compiles three real owning details through Workspace authoring and existing controlled page bindings', () => {
  const candidate = metadata();
  expect(resolveDetailContributions(modules, candidate).map(entry => entry.targetViewKey)).toEqual(['workspace.folder.detail', 'part.part.detail', 'document.document.detail']);
  expect(() => validateCompositionCandidate(modules, candidate)).not.toThrow();
});
it.each(['target', 'model', 'navigation', 'page', 'resolver', 'duplicate-key', 'duplicate-target', 'record-id', 'source', 'owner'])(
  'rejects invalid %s before publishing a detail contribution candidate', failure => {
    const candidate = metadata();
    const list = candidate.viewDocuments.find(document => document.view.key === 'workspace.references.list')!.view;
    const entries = list.detailContributions as Array<{ key: string; targetViewKey: string; navigationKey: string; pageRef: { pageKey: string; parameters: Record<string, unknown>; inputResolverKey: string } }>;
    const entry = entries[1]; let bindings = modules;
    if (failure === 'target') entry.targetViewKey = 'missing.detail';
    if (failure === 'model') entry.pageRef.parameters.anchorModelKey = 'Document';
    if (failure === 'navigation') entry.navigationKey = 'part.parts';
    if (failure === 'page') entry.pageRef.parameters.viewKey = 'workspace.folder.list';
    if (failure === 'resolver') entry.pageRef.inputResolverKey = 'missing';
    if (failure === 'duplicate-key') entries.push(structuredClone(entry));
    if (failure === 'duplicate-target') entries.push({ ...entry, key: 'second-target' });
    if (failure === 'record-id') entry.pageRef.parameters.recordId = 'spoofed';
    if (failure === 'source') bindings = modules.map(module => ({ ...module, standardCrudCapabilities: module.standardCrudCapabilities?.filter(binding => binding.targetKey !== 'workspace.references.list') }));
    if (failure === 'owner') candidate.manifest.modules[0].features = [];
    expect(() => validateCompositionCandidate(bindings, candidate)).toThrow(/贡献/);
  });
