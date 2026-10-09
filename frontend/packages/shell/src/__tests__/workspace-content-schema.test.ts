import { expect, it } from 'vitest';
import folder from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceFolderRuntime.contract.json';
import content from '../../../../../modules/Workspace/metadata/models/WorkspaceFolderContent.model.json';
import list from '../../../../../modules/Workspace/metadata/views/WorkspaceFolderList.view.json';
import resource from '../../../../modules/workspace/src/workspaceRootResource';
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

it('groups existing creation actions without replacing their identities and opens properties from Name', () => {
  const schema = buildStandardCrudListSchema(list.view, resource, { modelDocuments: [folder, content] });
  expect(schema.pageActions.map(action => action.key)).toEqual(['create', 'createPart', 'createDocument', 'deleteObjects']);
  expect(schema.pageActionGroups).toEqual([{ key: 'new', labelKey: 'New', variant: 'primary', actionKeys: ['create', 'createPart', 'createDocument'] }]);
  expect(schema.columns.find(column => column.field === 'Name')?.standardAction).toBe('view');
  expect(schema.rowActions.map(action => action.key)).toEqual(['edit', 'delete', 'remove']);
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
