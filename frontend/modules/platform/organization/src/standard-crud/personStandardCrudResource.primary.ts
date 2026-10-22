import { personStandardCrudResource } from './personStandardCrudResource';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';

export const primary: StandardCrudResourceRegistration = {
  ...personStandardCrudResource,
} as unknown as StandardCrudResourceRegistration;
