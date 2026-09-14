import { expect, it } from 'vitest';
import navigation from '../../../../../modules/Authorization/metadata/navigation/Authorization.nav.json';
import bindings from '../../../../modules/authorization/src';
import model from '../../../../../tests/Kernel.Tests/Fixtures/AuthorizationRoleRuntime.contract.json';
import list from '../../../../../modules/Authorization/metadata/views/RoleList.view.json';
import detail from '../../../../../modules/Authorization/metadata/views/RoleDetail.view.json';
import { roleResource } from '../../../../modules/authorization/src/roleResource';
import { compileStandardCrudRouteProjection } from '../navigation/standardCrudRouteAdapter';
import { buildStandardCrudListSchema, resolveStandardCrudDetailSchema } from '../metadata/standardCrudSchemaResolver';
import { buildStandardCrudFormSchemaFromModel } from '../standard-crud/standardCrudMetadataForm';

it('正式角色authoring经runtime投影进入列表、详情和表单', () => {
  const schema = buildStandardCrudListSchema(list.view, roleResource, { modelDocuments: [model] });
  expect(schema.columns.map(c => c.field)).toEqual(['ECODE', 'ENAME', 'Description', 'IsEnabled', 'IsBuiltIn']);
  expect(schema.rowActions.map(a => a.key)).toEqual(['edit', 'permissions', 'userRoles']);
  expect(schema.rowActions.find(a => a.key === 'permissions')?.navigationTargetKey).toBe('authorization.workbench');
  expect(schema.rowActions.every(a => !a.requiredPermissions?.length)).toBe(true);
  const resolved = resolveStandardCrudDetailSchema([detail], roleResource, { modelDocuments: [model] });
  expect(resolved.pageActions.map(a => a.key)).toEqual(['edit', 'permissions', 'userRoles']);
  const create = buildStandardCrudFormSchemaFromModel(model.Role, { mode: 'create', enumOptions: {} });
  const edit = buildStandardCrudFormSchemaFromModel(model.Role, { mode: 'edit', enumOptions: {} });
  expect(create.fields.filter(f => !f.readOnly).map(f => f.key)).toEqual(['ECODE', 'ENAME', 'Description']);
  expect(edit.fields.find(f => f.key === 'ECODE')?.readOnly).toBe(true);
  expect(create.fields.some(f => f.key === 'IsBuiltIn')).toBe(false);
});
it('角色详情路由来自实际导航与lazy binding', () => {
  const routes = compileStandardCrudRouteProjection([{ ...bindings, metadata: { moduleId: 'Authorization', displayName: '权限管理', version: '1.0.0' } }], [navigation], [list, detail], []);
  expect(routes.detailEntries).toContainEqual({ path: '/authorization/roles/:id', viewKey: 'authorization.role.detail', permissions: undefined, featurePosition: undefined });
});
