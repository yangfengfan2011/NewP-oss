import { readFileSync, readdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { organizationBindings } from '../../../../modules/organization/src';
import { buildMenusFromNavDocuments } from '../metadata/navigationAdapter';
import { buildPageEntriesFromNavDocuments } from '../navigation/pageEntryAdapter';
import { compileStandardCrudRouteProjection } from '../navigation/standardCrudRouteAdapter';
import { validateCompositionCandidate, resolvePageInput } from '../composition/compositionRegistry';
import type { RuntimeMetadataState } from '../metadata/runtimeMetadataManifest';
import type { FrontendModule, TabView } from '@icp-platform/sdk';

const folder = resolve(__dirname, '../../../../../modules/Organization/metadata');
const read = (name: string) => readdirSync(resolve(folder, name)).map(file => JSON.parse(readFileSync(resolve(folder, name, file), 'utf8').replace(/^\uFEFF/, '')));
const metadata: RuntimeMetadataState = { manifest: { modules: [] }, navDocuments: read('navigation'), viewDocuments: read('views'), modelDocuments: read('models') };
const modules: FrontendModule[] = [{ ...organizationBindings, metadata: { moduleId: 'Organization', displayName: 'Organization', version: '1' } }];

describe('Organization management authoring consumers', () => {
  it('publishes one menu and five configured pages while owning compatibility and full detail independently', () => {
    const menus = buildMenusFromNavDocuments(metadata.navDocuments, key => key ?? '');
    expect(menus.flatMap(menu => menu.children ?? []).map(menu => menu.path)).toEqual(['/organization/structure']);
    expect(() => validateCompositionCandidate(modules, metadata)).not.toThrow();
    const view = metadata.viewDocuments.find(item => item.view.key === 'organization.management')?.view as unknown as TabView;
    expect(view.defaultTabKey).toBe('structure');
    expect(view.tabs.map(tab => tab.key)).toEqual(['structure', 'companies', 'departments', 'positions', 'persons']);
    const global = view.tabs.find(tab => tab.key === 'departments')!.pageRef;
    expect(global.inputResolverKey).toBe('organization.department.global');
    expect(resolvePageInput(global, modules, { nodeKey: 'department:x', nodeType: 'department', label: 'x', hasChildren: false })).toEqual({ parameters: global.parameters, queryScope: { kind: 'global' } });
    const entries = buildPageEntriesFromNavDocuments(metadata.navDocuments);
    for (const tab of ['companies', 'departments', 'positions', 'persons']) {
      expect(entries.find(entry => entry.path === `/organization/${tab}`)).toMatchObject({ redirectTo: `/organization/structure?tab=${tab}` });
    }
    const projection = compileStandardCrudRouteProjection(modules, metadata.navDocuments, metadata.viewDocuments, []);
    expect(projection.detailEntries).toEqual([{ path: '/organization/persons/:id', viewKey: 'organization.person.detail', featurePosition: undefined }]);
    const withoutAliases = metadata.navDocuments.map(document => ({ ...document, navigation: { items: document.navigation.items.filter(item => !item.redirectTo) } }));
    expect(compileStandardCrudRouteProjection(modules, withoutAliases, metadata.viewDocuments, []).detailEntries).toEqual(projection.detailEntries);
  });
});
