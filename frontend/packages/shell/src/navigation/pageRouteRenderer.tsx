import { type ReactElement } from 'react';
import { RouteCapabilityOutlet } from '../capabilities/RouteCapabilityOutlet';
import type { RuntimePageEntry } from './pageEntryTypes';

export function renderPageEntry(entry: RuntimePageEntry): ReactElement {
  return (
    <RouteCapabilityOutlet
      capability={{ kind: entry.entryType, targetKey: entry.targetKey }}
    />
  );
}
