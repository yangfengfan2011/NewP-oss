import { departmentStandardCrudResource } from './departmentStandardCrudResource';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';

export const recovery: StandardCrudResourceRegistration = {
  ...departmentStandardCrudResource,
} as unknown as StandardCrudResourceRegistration;
