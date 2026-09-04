import { companyStandardCrudResource } from './companyStandardCrudResource';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';

export const recovery: StandardCrudResourceRegistration = {
  ...companyStandardCrudResource,
} as unknown as StandardCrudResourceRegistration;
