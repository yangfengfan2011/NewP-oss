import { type ReactElement } from 'react';
import { RouteCapabilityOutlet } from '../capabilities/RouteCapabilityOutlet';
import { lazy, Suspense } from 'react';
const PageEntry = lazy(() => import('../composition/PageOutlet').then(module => ({ default: module.PageEntry })));
import type { RuntimePageEntry } from './pageEntryTypes';

export function renderPageEntry(entry: RuntimePageEntry): ReactElement {
  if (entry.entryType === 'page' && entry.page) return <Suspense fallback={<div role="status">正在加载页面…</div>}><PageEntry page={entry.page} /></Suspense>;
  if (entry.entryType === 'page') return <div role="alert">缺少页面引用</div>;
  return (
    <RouteCapabilityOutlet
      capability={{ kind: entry.entryType, targetKey: entry.targetKey }}
    />
  );
}
