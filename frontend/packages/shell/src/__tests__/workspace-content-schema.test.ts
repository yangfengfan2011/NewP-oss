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
  expect(collectStandardCrudListRequestFields(schema.columns, schema.rowActions, schema.idField)).toEqual(['LID', 'ObjectType', 'Code', 'Name', 'UpdatedBy', 'UpdatedAt']);
  expect(() => buildStandardCrudListSchema({ ...list.view, columns: [{ key: 'origin', labelKey: 'Name', source: 'projection', field: 'OriginSpaceId' }] }, resource, context)).toThrow('query field catalog');
  expect(() => buildStandardCrudListSchema({ ...list.view, query: { ...list.view.query, defaultSort: { field: 'Description', direction: 'asc' } } }, resource, context)).toThrow('unsupported default sort');
  expect(() => buildStandardCrudListSchema({ ...list.view, querySource: { kind: 'table', model: 'WorkspaceFolderContent', resourceKey: 'workspace.content' } }, resource, context)).toThrow('unsupported query source');
});
