import { personStandardCrudResource } from './personStandardCrudResource';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';

export const recovery: StandardCrudResourceRegistration = {
  ...personStandardCrudResource,
} as unknown as StandardCrudResourceRegistration;
