import { loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { expect, it } from 'vitest';
import folder from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceFolderRuntime.contract.json';
const content = readComponentJson('Workspace', 'metadata/models/WorkspaceFolderContent.model.json');
const list = readComponentJson('Workspace', 'metadata/views/WorkspaceFolderList.view.json');
const { default: resource } = await loadComponentSource<{ default: StandardCrudResourceRegistration }>('Workspace', 'workspaceRootResource.ts');
import { buildStandardCrudListSchema } from '../metadata/standardCrudSchemaResolver';
import { collectStandardCrudListRequestFields } from '../standard-crud/standardCrudMetadataQuery';

it('validates Link projection fields against the formal query directory and requests LID identity', () => {
  const context = { modelDocuments: [folder, content] };
  const schema = buildStandardCrudListSchema(list.view, resource, context);
  expect(schema.idField).toBe('LID');
  expect(schema.query.fields[0]).toMatchObject({ key: 'ObjectType', field: 'ObjectType', dataSourceKey: 'workspace.content.types' });
  expect(() => buildStandardCrudListSchema({ ...list.view, query: { fields: [{ key: 'OriginSpaceId', source: 'model', field: 'OriginSpaceId' }] } }, resource, context)).toThrow('outside its field catalog');
  expect(collectStandardCrudListRequestFields(schema.columns, schema.rowActions, schema.idField)).toEqual(['LID', 'ObjectType', 'Code', 'Name', 'UpdatedBy', 'UpdatedAt']);
  expect(() => buildStandardCrudListSchema({ ...list.view, columns: [{ key: 'origin', labelKey: 'Name', source: 'projection', field: 'OriginSpaceId' }] }, resource, context)).toThrow('query field catalog');
  expect(() => buildStandardCrudListSchema({ ...list.view, query: { ...list.view.query, defaultSort: { field: 'Description', direction: 'asc' } } }, resource, context)).toThrow('unsupported default sort');
  expect(() => buildStandardCrudListSchema({ ...list.view, querySource: { kind: 'table', model: 'WorkspaceFolderContent', resourceKey: 'workspace.content' } }, resource, context)).toThrow('unsupported query source');
});

it('groups existing creation actions without replacing their identities and exposes the Name activation entry', () => {
  const schema = buildStandardCrudListSchema(list.view, resource, { modelDocuments: [folder, content] });
  expect(schema.pageActions.map(action => action.key)).toEqual(['create', 'createPart', 'createDocument', 'copySelected', 'cutSelected', 'paste', 'clearClipboard', 'removeSelected', 'deleteObjects']);
  expect(schema.pageActionGroups).toEqual([{ key: 'new', labelKey: 'New', variant: 'primary', actionKeys: ['create', 'createPart', 'createDocument'] }]);
  expect(schema.columns.find(column => column.field === 'Name')?.standardAction).toBe('view');
  expect(schema.rowActions.map(action => action.key)).toEqual(['view', 'copy', 'cut', 'edit', 'delete', 'remove']);
  expect(schema.pageActions.find(action => action.key === 'deleteObjects')?.requiresSelection).toBe(true);
});

it.each([
  null,
  [{ key: 'new', labelKey: 'New', actionKeys: [] }],
  [{ key: 'new', labelKey: 'New', actionKeys: ['missing'] }],
  [{ key: 'new', labelKey: 'New', actionKeys: ['create', 'create'] }],
  [{ key: 'new', labelKey: 'New', actionKeys: ['create'] }, { key: 'other', labelKey: 'Other', actionKeys: ['create'] }],
  [{ key: 'create', labelKey: 'New', actionKeys: ['createPart'] }],
  [{ key: 'new', labelKey: 'New', handlerKey: 'createPart', actionKeys: ['createPart'] }],
])('rejects invalid action group declarations %#', pageActionGroups => {
  expect(() => buildStandardCrudListSchema({ ...list.view, pageActionGroups }, resource, { modelDocuments: [folder, content] })).toThrow('page');
});

it('projects the real Workspace row menu as references to its existing actions', () => {
  const schema = buildStandardCrudListSchema(list.view, resource, { modelDocuments: [folder, content] });
  expect(schema.rowActionColumn).toBe('menu');
  expect(schema.rowContextMenu).toBeUndefined();
  for (const model of ['WorkspaceFolder', 'Part', 'Document']) {
    const keys = schema.rowActions.filter(action => model === 'WorkspaceFolder' || action.key !== 'view').map(action => action.key);
    expect(schema.rowActionSets?.[model]).toEqual({ column: keys, contextMenu: keys });
  }
});

it.each([null, 'edit', ['missing'], ['edit', 'edit'], ['create'], [{ key: 'edit', handlerKey: 'other' }]])(
  'rejects malformed, duplicate or non-row context menu references %#', rowContextMenu => {
    expect(() => buildStandardCrudListSchema({ ...list.view, rowActionSets: undefined, rowContextMenu }, resource,
      { modelDocuments: [folder, content] })).toThrow('row context menu');
  },
);

it.each(['unknown-model', 'missing-model', 'missing-action', 'duplicate', 'missing-surface', 'extra-surface', 'conflict', 'presentation', 'resolver'])(
  'rejects invalid typed action configuration: %s', kind => {
    const view = structuredClone(list.view);
    const registration = { ...resource, dataAdapter: { ...resource.dataAdapter } };
    if (kind === 'unknown-model') view.rowActionSets.Unknown = view.rowActionSets.Part;
    if (kind === 'missing-model') delete view.rowActionSets.Part;
    if (kind === 'missing-action') view.rowActionSets.Part.column = ['missing'];
    if (kind === 'duplicate') view.rowActionSets.Part.contextMenu = ['view', 'view'];
    if (kind === 'missing-surface') delete view.rowActionSets.Part.column;
    if (kind === 'extra-surface') view.rowActionSets.Part.other = [];
    if (kind === 'conflict') view.rowContextMenu = [];
    if (kind === 'presentation') view.rowActionColumn = ['menu'];
    if (kind === 'resolver') delete registration.dataAdapter.getRowModelKey;
    expect(() => buildStandardCrudListSchema(view, registration, { modelDocuments: [folder, content] })).toThrow();
  });
