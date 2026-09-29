import { readFileSync, readdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import type { FrontendModule, TreeProviderBinding } from '@icp-platform/sdk';
import { resolvePageInput, validateCompositionCandidate } from '../composition/compositionRegistry';
import type { RuntimeMetadataState } from '../metadata/runtimeMetadataManifest';
import organization from '../../../../modules/organization/src';
// 真实资源装配归测试收集阶段；绑定断言仍使用正式 capability loader 与编译入口。
import '../../../../modules/organization/src/standard-crud/departmentStandardCrudResource.primary';

const root = resolve(__dirname, '../../../../..');
function authoring(): RuntimeMetadataState {
  const read = (folder: string) => readdirSync(resolve(root, 'modules/Organization/metadata', folder)).map(file => JSON.parse(readFileSync(resolve(root, 'modules/Organization/metadata', folder, file), 'utf8')));
  return { manifest: { modules: [] }, navDocuments: read('navigation'), viewDocuments: read('views'), modelDocuments: read('models') };
}
const modules: FrontendModule[] = [{ metadata: { moduleId: 'Organization', displayName: 'Organization', version: '1' },
  standardCrudCapabilities: organization.standardCrudCapabilities,
  treeProviders: [{ key: 'organization.tree', roots: async () => [], children: async () => [], path: async () => [], search: async () => ({ items: [], total: 0, page: 1, pageSize: 20 }), resolveChange: async () => ({ kind: 'unrelated' }) } as TreeProviderBinding],
  pageInputResolvers: ['organization.department.global', 'organization.node.detail', 'organization.company.direct', 'organization.department.direct', 'organization.tree.global'].map(key => ({ key, pageKeys: ['platform.tree', 'platform.list', 'platform.detail'], resolve: parameters => ({ parameters }) })),
}];
describe('formal page configuration references', () => {
  it('accepts configured trees without a module Provider and still accepts a custom Provider', () => {
    expect(() => validateCompositionCandidate([{ ...modules[0], treeProviders: [] }], authoring())).not.toThrow();
    const metadata = authoring();
    const tree = metadata.viewDocuments.find(item => item.view.mode === 'tree')!.view;
    tree.providerKey = 'organization.tree'; delete tree.relationSource;
    expect(() => validateCompositionCandidate(modules, metadata)).not.toThrow();
    expect(() => validateCompositionCandidate([{ ...modules[0], treeProviders: [] }], metadata)).toThrow('Provider');
  });
  it.each(['handler', 'input'])('rejects a stable action with a mismatched %s binding before consumption', async kind => {
    const metadata = authoring();
    const list = metadata.viewDocuments.find(item => item.view.key === 'organization.department.list')!.view;
    const action = (list.rowActions as Array<Record<string, unknown>>).find(item => item.standardAction === 'delete')!;
    action[kind === 'handler' ? 'handlerKey' : 'inputKey'] = 'missing';
    const binding = organization.standardCrudCapabilities!.find(item => item.targetKey === list.key)!;
    const { default: resource } = await binding.load();
    expect(() => validateCompositionCandidate([{ ...modules[0], standardCrudResources: [resource] }], metadata)).toThrow(/binding/);
  });
  it('accepts omitted action arrays and rejects a missing resource binding', () => {
    const metadata = authoring();
    expect(() => validateCompositionCandidate([{ ...modules[0], standardCrudCapabilities: [] }], metadata)).toThrow(/binding/);
    const tree = metadata.viewDocuments.find(item => item.view.mode === 'tree')!.view;
    delete tree.treeActions;
    for (const type of tree.nodeTypes as Array<Record<string, unknown>>) delete type.contextMenu;
    expect(() => validateCompositionCandidate(modules, metadata)).not.toThrow();
  });
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
