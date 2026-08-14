import {
  createRecoverableCapabilityLoader,
  type FrontendBehaviorBindings,
  type StandardCrudCapabilityModule,
} from '@icp-platform/sdk';

const loadPersonStandardCrudResource = createRecoverableCapabilityLoader<StandardCrudCapabilityModule>(
  async () => ({
    default: (await import('./standard-crud/personStandardCrudResource.primary')).primary,
  }),
  async () => ({
    default: (await import('./standard-crud/personStandardCrudResource.recovery')).recovery,
  }),
);

export const organizationBindings: FrontendBehaviorBindings = {
  behaviorKey: 'organization.frontend',
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
  ],
};

export default organizationBindings;
