import type { ReactElement } from 'react';
import { StandardListPage } from '@icp-platform/ui/core';
import { ModuleRegistry, type StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { resolveListViewDocument } from '../metadata/viewMetadataResolver';
import { buildListViewRegistry } from '../registries/listViewRegistry';
import { buildStandardCrudRegistry } from '../registries/standardCrudRegistry';
import StandardCrudListPageHost from './StandardCrudListPageHost';

interface StandardListPageHostProps {
  viewKey: string;
  registration?: StandardCrudResourceRegistration;
}

export default function StandardListPageHost({ viewKey, registration }: StandardListPageHostProps): ReactElement {
  const runtimeMetadata = useRuntimeMetadata();
  const viewDocuments = runtimeMetadata?.viewDocuments ?? [];
  const viewDocument = resolveListViewDocument(viewDocuments, viewKey);

  if (!viewDocument) {
    return <div className="sh-error-state">未找到列表视图元数据</div>;
  }

  const modules = ModuleRegistry.getAllModules();
  const standardCrudRegistration = registration
    ?? buildStandardCrudRegistry(modules).findByListViewKey(viewKey);

  if (standardCrudRegistration) {
    return <StandardCrudListPageHost viewKey={viewKey} registration={standardCrudRegistration} />;
  }

  // 旧 standardListViews 作为兼容机制保留；新标准 CRUD 资源优先由 capability loader 显式传入。
  const listViewRegistration = buildListViewRegistry(modules).get(viewKey);

  if (!listViewRegistration) {
    return <div className="sh-error-state">未找到列表页注册项</div>;
  }

  const EntryComponent = listViewRegistration.component;

  return (
    <EntryComponent
      viewKey={viewKey}
      viewMetadata={viewDocument.view as Record<string, unknown>}
    >
      {(definition) => <StandardListPage {...definition} />}
    </EntryComponent>
  );
}
