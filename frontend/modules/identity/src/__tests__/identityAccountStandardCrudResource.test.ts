import { describe, expect, it } from 'vitest';
import { identityAccountStandardCrudResource } from '../standard-crud/identityAccountStandardCrudResource';

describe('Identity account Standard CRUD resource candidate', () => {
  it('maps the existing Identity list response into metadata protocol fields', () => {
    const row = identityAccountStandardCrudResource.mapListItem({
      eid: 'User_2',
      ecode: 'alice',
      ename: 'Alice',
      status: 'Disabled',
      revision: 7,
      isLocalAdministrator: false,
      isPersonnelBound: true,
      personnelDisableHold: false,
    });

    expect(row).toMatchObject({
      id: 'User_2',
      EID: 'User_2',
      ECODE: 'alice',
      ENAME: 'Alice',
      Status: 'Disabled',
      Revision: 7,
      IsLocalAdministrator: false,
      IsPersonnelBound: true,
      PersonnelDisableHold: false,
    });
    expect(identityAccountStandardCrudResource.getRowId(row)).toBe('User_2');
  });

  it('keeps the existing Identity API filter names and exposes Deleted as an explicit option', () => {
    expect(identityAccountStandardCrudResource.createEmptyFilters()).toEqual({
      query: '',
      status: '',
      isPersonnelBound: '',
      isLocalAdministrator: '',
    });
    expect(identityAccountStandardCrudResource.buildListQuery({
      query: 'ali',
      status: 'Deleted',
      isPersonnelBound: 'true',
      isLocalAdministrator: 'false',
    })).toEqual({
      query: 'ali',
      status: 'Deleted',
      isPersonnelBound: 'true',
      isLocalAdministrator: 'false',
    });
    expect(identityAccountStandardCrudResource.enumOptions?.IdentityAccountStatus).toEqual([
      { value: 'Enabled', label: '启用' },
      { value: 'Disabled', label: '停用' },
      { value: 'Deleted', label: '已删除' },
    ]);
  });
});
