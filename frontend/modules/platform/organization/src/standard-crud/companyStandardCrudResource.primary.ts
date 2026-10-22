import { companyStandardCrudResource } from './companyStandardCrudResource';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';

export const primary: StandardCrudResourceRegistration = {
  ...companyStandardCrudResource,
} as unknown as StandardCrudResourceRegistration;
