import { readFileSync, readdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import type { FrontendModule, TreeProviderBinding } from '@icp-platform/sdk';
import { resolvePageInput, validateCompositionCandidate } from '../composition/compositionRegistry';
import type { RuntimeMetadataState } from '../metadata/runtimeMetadataManifest';

const root = resolve(__dirname, '../../../../..');
function authoring(): RuntimeMetadataState {
  const read = (folder: string) => readdirSync(resolve(root, 'modules/Organization/metadata', folder)).map(file => JSON.parse(readFileSync(resolve(root, 'modules/Organization/metadata', folder, file), 'utf8')));
  return { manifest: { modules: [] }, navDocuments: read('navigation'), viewDocuments: read('views'), modelDocuments: read('models') };
}
const modules: FrontendModule[] = [{ metadata: { moduleId: 'Organization', displayName: 'Organization', version: '1' },
  treeProviders: [{ key: 'organization.tree', roots: async () => [], children: async () => [], path: async () => [], search: async () => ({ items: [], total: 0, page: 1, pageSize: 20 }), resolveChange: async () => ({ kind: 'unrelated' }) } as TreeProviderBinding],
  pageInputResolvers: ['organization.node.detail', 'organization.company.direct', 'organization.department.direct', 'organization.tree.global'].map(key => ({ key, pageKeys: ['platform.tree', 'platform.list', 'platform.detail'], resolve: parameters => ({ parameters }) })),
}];
describe('formal page configuration references', () => {
  it('accepts real Organization Tree/Tab authoring without page identities per view', () => { expect(() => validateCompositionCandidate(modules, authoring())).not.toThrow(); });
  it('allows sibling reuse and rejects the current configuration chain returning through a tree', () => {
    const metadata = authoring();
    const company = metadata.viewDocuments.find(item => item.view.key === 'organization.company.tabs')!.view;
    const tabs = company.tabs as Array<Record<string, unknown>>;
    tabs.push({ ...tabs[0], key: 'duplicateContent' });
    expect(() => validateCompositionCandidate(modules, metadata)).not.toThrow();
    tabs[2].pageRef = { pageKey: 'platform.tree', parameters: { viewKey: 'organization.structure' } };
    expect(() => validateCompositionCandidate(modules, metadata)).toThrow(/循环/);
  });
  it('refuses missing detail context and missing tree scope before loading a page', () => {
    expect(() => resolvePageInput({ pageKey: 'platform.detail', parameters: { viewKey: 'detail' } }, modules)).toThrow(/上下文/);
    expect(() => resolvePageInput({ pageKey: 'platform.tree', parameters: { viewKey: 'tree' } }, modules)).toThrow(/treeScope/);
  });
});
