import { loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import type { FrontendBehaviorBindings } from '@icp-platform/sdk';
import { expect, it } from 'vitest';
import type { FrontendModule } from '@icp-platform/sdk';
const { default: workspace } = await loadComponentSource<{ default: FrontendBehaviorBindings }>('Workspace', 'index.ts');
const navigation = readComponentJson('Workspace', 'metadata/navigation/Workspace.nav.json');
const tree = readComponentJson('Workspace', 'metadata/views/WorkspaceStructure.view.json');
const list = readComponentJson('Workspace', 'metadata/views/WorkspaceFolderList.view.json');
const detail = readComponentJson('Workspace', 'metadata/views/WorkspaceFolderDetail.view.json');
const references = readComponentJson('Workspace', 'metadata/views/WorkspaceReferencesList.view.json');
const capsule = readComponentJson('Workspace', 'newp.plugin.json');
import { compileStandardCrudRouteProjection } from '../navigation/standardCrudRouteAdapter';

const module: FrontendModule = { metadata: { moduleId: 'Workspace', displayName: '工作空间', version: '1' }, ...workspace };
const views = [tree, list, detail, references];

it('projects the formal Workspace composed content list without inventing standalone routes', () => {
  expect(module.standardCrudCapabilities?.map(binding => binding.kind)).toEqual(['standard-list', 'standard-list']);
  expect(new Set(capsule.frontend.bindings)).toEqual(new Set([
    ...(workspace.treeProviders ?? []).map(binding => binding.key),
    ...(workspace.pageInputResolvers ?? []).map(binding => binding.key),
    ...(workspace.standardCrudCapabilities ?? []).map(binding => binding.targetKey),
  ]));
  expect(compileStandardCrudRouteProjection([module], [navigation], views, [])).toEqual({ detailEntries: [], drawerRedirects: [], fallbackRoutes: [] });
});

it('still rejects an unconsumed list binding and an object list that hides every detail action', () => {
  expect(() => compileStandardCrudRouteProjection([module], [navigation], [list, detail], [])).toThrow('STANDARD_CRUD_NAV_NOT_UNIQUE');
  const composedList = { ...tree, view: { ...tree.view, nodeTypes: [{ ...tree.view.nodeTypes[0], pageRef: {
    pageKey: 'platform.list', parameters: { viewKey: list.view.key },
  } }] } };
  const hiddenDetailList = { ...list, view: { ...list.view, rowActions: [], columns: list.view.columns.map(column => ({ ...column, standardAction: undefined })) } };
  expect(() => compileStandardCrudRouteProjection([module], [navigation], [composedList, hiddenDetailList, detail], [])).toThrow('STANDARD_CRUD_DETAIL_ACTION_MISSING');
});
