import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { exampleRecordStandardCrudResource } from './exampleRecordStandardCrudResource';

export const recovery: StandardCrudResourceRegistration = {
  ...exampleRecordStandardCrudResource,
} as unknown as StandardCrudResourceRegistration;
