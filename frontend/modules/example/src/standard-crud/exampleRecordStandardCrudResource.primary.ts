import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { exampleRecordStandardCrudResource } from './exampleRecordStandardCrudResource';

export const primary: StandardCrudResourceRegistration = {
  ...exampleRecordStandardCrudResource,
} as unknown as StandardCrudResourceRegistration;
