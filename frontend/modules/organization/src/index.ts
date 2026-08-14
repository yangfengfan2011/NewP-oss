import {
  type FrontendBehaviorBindings,
  type StandardCrudCapabilityModule,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';

async function loadPersonStandardCrudResource(): Promise<StandardCrudCapabilityModule> {
  const { personStandardCrudResource } = await import('./standard-crud/personStandardCrudResource');
  return {
    default: personStandardCrudResource as unknown as StandardCrudResourceRegistration,
  };
}

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
