import { describe, expect, it } from 'vitest';
import { organizationPageInputs } from './organizationTree';
const company = { nodeKey: 'company:c1', nodeType: 'company', label: '公司', hasChildren: true, context: { id: 'c1', companyId: 'c1' } };
describe('Organization page input adapters', () => {
  it('separates a company direct scope and fixed create values from user filters', () => {
    const resolver = organizationPageInputs.find(item => item.key === 'organization.company.direct')!;
    const input = resolver.resolve({ viewKey: 'organization.department.list' }, company);
    expect(input.queryScope).toEqual({ CompanyId: 'c1', ParentDepartmentId: null });
    expect(input.fixedCreateValues).toEqual({ CompanyId: 'c1', ParentDepartmentId: null });
    expect(() => resolver.resolve({}, undefined)).toThrow(/上下文/);
  });
  it('uses department identity as direct parent and rejects a company in that resolver', () => {
    const resolver = organizationPageInputs.find(item => item.key === 'organization.department.direct')!;
    const input = resolver.resolve({}, { ...company, nodeKey: 'department:d1', nodeType: 'department', context: { id: 'd1', companyId: 'c1' } });
    expect(input.queryScope).toEqual({ CompanyId: 'c1', ParentDepartmentId: 'd1' });
    expect(() => resolver.resolve({}, company)).toThrow(/上下文/);
  });

});
