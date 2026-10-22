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

const loadExampleRecordStandardCrudResource = createRecoverableCapabilityLoader<StandardCrudCapabilityModule>(
  async () => ({
    default: (await import('./standard-crud/exampleRecordStandardCrudResource.primary')).primary,
  }),
  async () => ({
    default: (await import('./standard-crud/exampleRecordStandardCrudResource.recovery')).recovery,
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
    {
      kind: 'standard-list',
      targetKey: 'example.record.list',
      load: loadExampleRecordStandardCrudResource,
    },
    {
      kind: 'standard-crud-detail',
      targetKey: 'example.record.detail',
      load: loadExampleRecordStandardCrudResource,
    },
  ],
};

export default exampleBindings;
