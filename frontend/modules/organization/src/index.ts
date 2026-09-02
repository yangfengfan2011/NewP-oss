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

const loadPositionStandardCrudResource = createRecoverableCapabilityLoader<StandardCrudCapabilityModule>(
  async () => ({
    default: (await import('./standard-crud/positionStandardCrudResource.primary')).primary,
  }),
  async () => ({
    default: (await import('./standard-crud/positionStandardCrudResource.recovery')).recovery,
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
    {
      kind: 'standard-list',
      targetKey: 'organization.position.list',
      load: loadPositionStandardCrudResource,
    },
  ],
};

export default organizationBindings;
