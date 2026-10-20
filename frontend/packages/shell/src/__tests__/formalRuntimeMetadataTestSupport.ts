import type { ActionDataAccessRequirement } from '@icp-platform/sdk';
import { bindActionDataAccess, bindNavigationDataAccess } from '../metadata/runtimeMetadataLoader';
import type { RuntimeMetadataState } from '../metadata/runtimeMetadataManifest';
import standardRead from '../../../../../tests/Kernel.Tests/Fixtures/StandardReadDataAccess.contract.json';
import organization from '../../../../../tests/Kernel.Tests/Fixtures/OrganizationDataAccess.contract.json';
import example from '../../../../../tests/Kernel.Tests/Fixtures/ExampleCrudDataAccess.contract.json';
import partWrite from '../../../../../tests/Kernel.Tests/Fixtures/PartWriteDataAccess.contract.json';
import partHistory from '../../../../../tests/Kernel.Tests/Fixtures/PartHistoryDataAccess.contract.json';
import documentHistory from '../../../../../tests/Kernel.Tests/Fixtures/DocumentHistoryDataAccess.contract.json';
import workspaceHistory from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceHistoryDataAccess.contract.json';
import documentFiles from '../../../../../tests/Kernel.Tests/Fixtures/DocumentFileDataAccess.contract.json';
import clipboard from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceClipboardDataAccess.contract.json';
import authorizationManagement from '../../../../../tests/Kernel.Tests/Fixtures/AuthorizationManagementRuntime.contract.json';
import ownership from '../../../../../tests/Kernel.Tests/Fixtures/OwnershipDataAccess.contract.json';
import objectAuthorization from '../../../../../tests/Kernel.Tests/Fixtures/ObjectAuthorizationDataAccess.contract.json';

const declarations = uniqueDeclarations([...(standardRead as ActionDataAccessRequirement[]), ...(organization as ActionDataAccessRequirement[]), ...(example as ActionDataAccessRequirement[]),
  ...(partWrite as ActionDataAccessRequirement[]), ...(partHistory as ActionDataAccessRequirement[]), ...(documentHistory as ActionDataAccessRequirement[]),
  ...(documentFiles.dataAccess as ActionDataAccessRequirement[]),
  ...(workspaceHistory as ActionDataAccessRequirement[]), ...(clipboard.declarations as ActionDataAccessRequirement[]),
  ...(authorizationManagement.dataAccess as ActionDataAccessRequirement[]), ...(authorizationManagement.identityDataAccess as ActionDataAccessRequirement[]),
  ...(ownership as ActionDataAccessRequirement[]), ...(objectAuthorization as ActionDataAccessRequirement[])]);

function uniqueDeclarations(items: readonly ActionDataAccessRequirement[]) {
  const byId = new Map<string, ActionDataAccessRequirement>();
  for (const item of items) {
    const previous = byId.get(item.id);
    if (previous && JSON.stringify(previous) !== JSON.stringify(item)) throw new Error(`正式测试投影冲突：${item.id}`);
    byId.set(item.id, item);
  }
  return [...byId.values()];
}

export function formalDataAccessFor(viewKey: string, surface: string, actionKey = '') {
  const matches = declarations.filter(item => item.viewKey === viewKey && item.surface === surface && item.actionKey === actionKey);
  if (matches.length !== 1) throw new Error(`正式测试声明缺失或冲突：${viewKey}/${surface}/${actionKey}`);
  return matches[0];
}

function hasAuthoringReference(value: unknown): boolean {
  if (typeof value !== 'object' || value === null) return false;
  if (Array.isArray(value)) return value.some(hasAuthoringReference);
  const record = value as Record<string, unknown>;
  const reference = record.dataAccess;
  if (typeof reference === 'object' && reference !== null && 'execution' in reference && !('id' in reference)) return true;
  return Object.entries(record).some(([key, child]) => key !== 'dataAccess' && hasAuthoringReference(child));
}

// 正常旧资产沿生产 binder 消费正式编译目录；已绑定快照及故意删声明的反例保持原样，不补单次请求上下文。
export function bindFormalRuntimeMetadata(value: RuntimeMetadataState): RuntimeMetadataState {
  const viewKeys = new Set(value.viewDocuments.map(document => String(document.view.key)));
  for (const document of value.viewDocuments) if (Array.isArray(document.view.tabs))
    for (const tab of document.view.tabs) if (typeof tab.pageRef?.pageKey === 'string') viewKeys.add(tab.pageRef.pageKey);
  for (const item of value.navDocuments.flatMap(document => document.navigation.items)) if (item.page) viewKeys.add(item.page.pageKey);
  const relevant = declarations.filter(item => viewKeys.has(item.viewKey));
  const modules = value.manifest.modules.map(module => ({ ...module,
    dataAccess: uniqueDeclarations([...relevant.filter(item => item.moduleId === module.moduleId), ...(module.dataAccess ?? [])]),
  }));
  for (const moduleId of new Set(relevant.map(item => item.moduleId))) {
    if (!modules.some(module => module.moduleId === moduleId)) modules.push({ moduleId, metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [],
      dataAccess: relevant.filter(item => item.moduleId === moduleId) });
  }
  return { ...value, manifest: { ...value.manifest, modules },
    navDocuments: value.navDocuments.map(document => hasAuthoringReference(document) ? bindNavigationDataAccess(document, declarations) : document),
    viewDocuments: value.viewDocuments.map(document => hasAuthoringReference(document.view) ? bindActionDataAccess(document, declarations) : document),
  };
}
