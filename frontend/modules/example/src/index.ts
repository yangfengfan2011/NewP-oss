import {
  createRecoverableCapabilityLoader,
  type FrontendBehaviorBindings,
  type StandardCrudCapabilityModule,
} from '@icp-platform/sdk';

const loadRuntimeWorkloadStandardCrudResource = createRecoverableCapabilityLoader<StandardCrudCapabilityModule>(
  async () => ({
    default: (await import('./standard-crud/runtimeWorkloadStandardCrudResource.primary')).primary,
  }),
  async () => ({
    default: (await import('./standard-crud/runtimeWorkloadStandardCrudResource.recovery')).recovery,
  }),
);

export const exampleBindings: FrontendBehaviorBindings = {
  behaviorKey: 'example.frontend',
  standardCrudCapabilities: [
    {
      kind: 'standard-list',
      targetKey: 'example.runtime-workload.list',
      load: loadRuntimeWorkloadStandardCrudResource,
    },
  ],
};

export default exampleBindings;
