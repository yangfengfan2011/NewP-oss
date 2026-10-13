import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { identityAccountStandardCrudResource } from './identityAccountStandardCrudResource';

export const primary: StandardCrudResourceRegistration = {
  ...identityAccountStandardCrudResource,
} as unknown as StandardCrudResourceRegistration;
