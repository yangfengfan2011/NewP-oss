import React, { type ReactElement } from 'react';
import { ModuleRegistry } from '@icp-platform/sdk';
import StandardListPageHost from '../pages/StandardListPageHost';
import { buildCustomPageRegistry } from '../registries/customPageRegistry';
import type { RuntimePageEntry } from './pageEntryTypes';

export function renderPageEntry(entry: RuntimePageEntry): ReactElement {
  switch (entry.entryType) {
    case 'standard-list':
      return <StandardListPageHost viewKey={entry.targetKey} />;
    case 'custom': {
      const component = buildCustomPageRegistry(ModuleRegistry.getAllModules()).get(entry.targetKey)?.component;
      return component
        ? React.createElement(component)
        : <div className="sh-error-state">未找到自定义页面注册项</div>;
    }
  }

  return <div className="sh-error-state">{`未知页面入口类型：${entry.entryType}`}</div>;
}
