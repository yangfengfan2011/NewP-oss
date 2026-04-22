import type { ReactElement } from 'react';
import { StandardListPage } from '@icp-platform/ui';
import { ModuleRegistry } from '@icp-platform/sdk';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { resolveListViewDocument } from '../metadata/viewMetadataResolver';
import { buildListViewRegistry } from '../registries/listViewRegistry';

interface StandardListPageHostProps {
  viewKey: string;
}

export default function StandardListPageHost({ viewKey }: StandardListPageHostProps): ReactElement {
  const runtimeMetadata = useRuntimeMetadata();
  const viewDocuments = runtimeMetadata?.viewDocuments ?? [];
  const viewDocument = resolveListViewDocument(viewDocuments, viewKey);

  if (!viewDocument) {
    return <div className="sh-error-state">未找到列表视图元数据</div>;
  }

  // 宿主只负责把 view metadata 和模块注册项装配起来，真正的列表骨架渲染继续交给 StandardListPage。
  const registration = buildListViewRegistry(ModuleRegistry.getAllModules()).get(viewKey);

  if (!registration) {
    return <div className="sh-error-state">未找到列表页注册项</div>;
  }

  const EntryComponent = registration.component;

  return (
    <EntryComponent
      viewKey={viewKey}
      viewMetadata={viewDocument.view as Record<string, unknown>}
    >
      {(definition) => <StandardListPage {...definition} />}
    </EntryComponent>
  );
}
