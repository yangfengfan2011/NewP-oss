import { describe, expect, it } from 'vitest';
import { companyStandardCrudResource } from '../../../../modules/organization/src/standard-crud/companyStandardCrudResource';
import companyModelDocument from '../../../../../modules/Organization/metadata/models/Company.model.json';
import companyListViewDocument from '../../../../../modules/Organization/metadata/views/CompanyList.view.json';
import { buildStandardCrudListSchema } from '../metadata/standardCrudSchemaResolver';

describe('organization company standard CRUD metadata', () => {
  it('compiles the UpdatedAt list column against the Company model authoring document', () => {
    const schema = buildStandardCrudListSchema(
      companyListViewDocument.view,
      companyStandardCrudResource,
      { modelDocuments: [companyModelDocument] },
    );

    expect(schema.columns.find((column) => column.key === 'updatedAt')).toMatchObject({
      source: 'model',
      field: 'UpdatedAt',
    });
  });
});
