// @vitest-environment jsdom

import { afterEach, expect, it, vi } from 'vitest';
import { companyStandardCrudResource } from '../standard-crud/companyStandardCrudResource';
import { departmentStandardCrudResource } from '../standard-crud/departmentStandardCrudResource';
import { positionStandardCrudResource } from '../standard-crud/positionStandardCrudResource';

const request = vi.hoisted(() => vi.fn());
vi.mock('@icp-platform/core', async importOriginal => ({
  ...await importOriginal<typeof import('@icp-platform/core')>(),
  platformFetch: request,
}));
afterEach(() => { request.mockReset(); });

it.each([
  ['organization.company', 'Company_1', companyStandardCrudResource],
  ['organization.department', 'Department_1', departmentStandardCrudResource],
  ['organization.position', 'Position_1', positionStandardCrudResource],
])('%s manages the fixed object through its registered action', async (resourceKey, id, registration) => {
  request.mockResolvedValueOnce({ ok: true, status: 204 });
  const configuration = { resourceKey, objectId: id, subjects: [] };
  expect(registration.actionInputRenderers?.objectAuthorization).toBeTypeOf('function');
  await registration.actionHandlers?.authorize?.({ id, input: configuration, featurePosition: `${resourceKey}.authorize` });
  expect(request).toHaveBeenCalledWith(
    `/api/authorization/data-scope/objects/${resourceKey}/${id}`,
    expect.objectContaining({
      method: 'PUT',
      headers: expect.objectContaining({ 'X-NewP-Feature': `${resourceKey}.authorize` }),
      body: JSON.stringify(configuration),
    }),
  );
});

it.each([
  ['organization.company', 'Company_1', companyStandardCrudResource],
  ['organization.department', 'Department_1', departmentStandardCrudResource],
  ['organization.position', 'Position_1', positionStandardCrudResource],
])('%s checks the current object and feature position before exposing the action', async (resourceKey, id, registration) => {
  const resolver = registration.actionEligibilityResolvers?.authorize;
  expect(resolver).toBeTypeOf('function');
  const context = { id, featurePosition: `${resourceKey}:list.rowActions:authorize`,
    action: { key: 'authorize', labelKey: '对象授权' }, signal: new AbortController().signal };
  request.mockResolvedValueOnce({ status: 403, ok: false }).mockResolvedValueOnce({ status: 200, ok: true });
  await expect(resolver?.(context)).resolves.toEqual({ visible: false, available: false });
  await expect(resolver?.(context)).resolves.toEqual({ visible: true, available: true });
  expect(request).toHaveBeenNthCalledWith(1,
    `/api/authorization/data-scope/objects/${resourceKey}/${id}/eligibility`,
    expect.objectContaining({ headers: { 'X-NewP-Feature': context.featurePosition }, signal: context.signal }),
  );
});
