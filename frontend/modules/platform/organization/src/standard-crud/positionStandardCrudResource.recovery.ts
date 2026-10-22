import { positionStandardCrudResource } from './positionStandardCrudResource';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';

export const recovery: StandardCrudResourceRegistration = {
  ...positionStandardCrudResource,
} as unknown as StandardCrudResourceRegistration;
