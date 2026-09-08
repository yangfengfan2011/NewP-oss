import {
  createRecoverableCapabilityLoader,
  type FrontendBehaviorBindings,
  type StandardCrudCapabilityModule,
} from '@icp-platform/sdk';

import { organizationPageInputs, organizationTreeProvider } from './organizationTree';

const loadPersonStandardCrudResource = createRecoverableCapabilityLoader<StandardCrudCapabilityModule>(
  async () => ({
    default: (await import('./standard-crud/personStandardCrudResource.primary')).primary,
  }),
  async () => ({
    default: (await import('./standard-crud/personStandardCrudResource.recovery')).recovery,
  }),
);

const loadPositionStandardCrudResource = createRecoverableCapabilityLoader<StandardCrudCapabilityModule>(
  async () => ({
    default: (await import('./standard-crud/positionStandardCrudResource.primary')).primary,
  }),
  async () => ({
    default: (await import('./standard-crud/positionStandardCrudResource.recovery')).recovery,
  }),
);

const loadCompanyStandardCrudResource = createRecoverableCapabilityLoader<StandardCrudCapabilityModule>(
  async () => ({
    default: (await import('./standard-crud/companyStandardCrudResource.primary')).primary,
  }),
  async () => ({
    default: (await import('./standard-crud/companyStandardCrudResource.recovery')).recovery,
  }),
);

const loadDepartmentStandardCrudResource = createRecoverableCapabilityLoader<StandardCrudCapabilityModule>(
  async () => ({
    default: (await import('./standard-crud/departmentStandardCrudResource.primary')).primary,
  }),
  async () => ({
    default: (await import('./standard-crud/departmentStandardCrudResource.recovery')).recovery,
  }),
);

export const organizationBindings: FrontendBehaviorBindings = {
  behaviorKey: 'organization.frontend',
  pageInputResolvers: organizationPageInputs,
  treeProviders: [organizationTreeProvider],
  standardCrudCapabilities: [
    {
      kind: 'standard-list',
      targetKey: 'organization.person.list',
      load: loadPersonStandardCrudResource,
    },
    {
      kind: 'standard-crud-detail',
      targetKey: 'organization.person.detail',
      load: loadPersonStandardCrudResource,
    },
    {
      kind: 'standard-list',
      targetKey: 'organization.position.list',
      load: loadPositionStandardCrudResource,
    },
    {
      kind: 'standard-list',
      targetKey: 'organization.company.list',
      load: loadCompanyStandardCrudResource,
    },
    {
      kind: 'standard-list',
      targetKey: 'organization.department.list',
      load: loadDepartmentStandardCrudResource,
    },
  ],
};

export default organizationBindings;
