import { expect, it } from 'vitest';
import type { FrontendModule } from '@icp-platform/sdk';
import workspace from '../../../../modules/workspace/src';
import navigation from '../../../../../modules/Workspace/metadata/navigation/Workspace.nav.json';
import tree from '../../../../../modules/Workspace/metadata/views/WorkspaceStructure.view.json';
import list from '../../../../../modules/Workspace/metadata/views/WorkspaceFolderList.view.json';
import detail from '../../../../../modules/Workspace/metadata/views/WorkspaceFolderDetail.view.json';
import capsule from '../../../../../modules/Workspace/newp.plugin.json';
import { compileStandardCrudRouteProjection } from '../navigation/standardCrudRouteAdapter';

const module: FrontendModule = { metadata: { moduleId: 'Workspace', displayName: '工作空间', version: '1' }, ...workspace };
const views = [tree, list, detail];

it('projects the formal Workspace composed content list without inventing standalone routes', () => {
  expect(module.standardCrudCapabilities?.map(binding => binding.kind)).toEqual(['standard-list']);
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
  const hiddenDetailList = { ...list, view: { ...list.view, rowActions: [] } };
  expect(() => compileStandardCrudRouteProjection([module], [navigation], [composedList, hiddenDetailList, detail], [])).toThrow('STANDARD_CRUD_DETAIL_ACTION_MISSING');
});
