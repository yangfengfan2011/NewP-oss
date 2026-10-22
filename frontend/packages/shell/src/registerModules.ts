import { ModuleRegistry } from '@icp-platform/sdk';
import { loadActiveModuleRuntime } from './active-module-runtime-loader';
import {
  loadRuntimeMetadata,
  setPreloadedRuntimeMetadata,
} from './metadata/runtimeMetadataLoader';
import { validateStandardCrudCandidate } from './registries/standardCrudRegistry';

import { validateCompositionCandidate } from './composition/compositionRegistry';

export async function registerShellModules() {
  const [result, metadata] = await Promise.all([
    loadActiveModuleRuntime(),
    loadRuntimeMetadata(),
  ]);
  validateStandardCrudCandidate(result.modules, metadata);
  validateCompositionCandidate(result.modules, metadata);
  ModuleRegistry.replaceAll(result.modules, result.snapshotId);
  setPreloadedRuntimeMetadata(metadata);
}
