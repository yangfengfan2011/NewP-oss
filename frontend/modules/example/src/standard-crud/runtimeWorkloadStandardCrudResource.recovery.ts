import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { runtimeWorkloadStandardCrudResource } from './runtimeWorkloadStandardCrudResource';

export const recovery: StandardCrudResourceRegistration = {
  ...runtimeWorkloadStandardCrudResource,
} as unknown as StandardCrudResourceRegistration;
