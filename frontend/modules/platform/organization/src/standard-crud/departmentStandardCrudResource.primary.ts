import { departmentStandardCrudResource } from './departmentStandardCrudResource';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';

export const primary: StandardCrudResourceRegistration = {
  ...departmentStandardCrudResource,
} as unknown as StandardCrudResourceRegistration;
