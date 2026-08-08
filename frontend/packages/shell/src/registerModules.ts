import { ModuleRegistry } from '@icp-platform/sdk';
import { loadActiveModuleRuntime } from './active-module-runtime-loader';

export async function registerShellModules() {
  const result = await loadActiveModuleRuntime();
  ModuleRegistry.replaceAll(result.modules, result.snapshotId);
}
