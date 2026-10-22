import { createContext } from 'react';
import type { StandardCrudOperationResult } from '@icp-platform/sdk';

export type EntryNavigation = NonNullable<StandardCrudOperationResult['navigation']>;
export const EntryNavigationContext = createContext<((target: EntryNavigation) => void) | null>(null);
