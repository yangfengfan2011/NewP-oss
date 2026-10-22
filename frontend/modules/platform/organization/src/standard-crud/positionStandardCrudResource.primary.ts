import { positionStandardCrudResource } from './positionStandardCrudResource';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';

export const primary: StandardCrudResourceRegistration = {
  ...positionStandardCrudResource,
} as unknown as StandardCrudResourceRegistration;
