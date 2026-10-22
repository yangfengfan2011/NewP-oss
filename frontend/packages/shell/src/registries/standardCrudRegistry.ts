import type { FrontendModule, StandardCrudResourceRegistration } from '@icp-platform/sdk';
import type { RuntimeMetadataState } from '../metadata/runtimeMetadataManifest';
import { resolveStandardCrudDetailSchema } from '../metadata/standardCrudSchemaResolver';

export interface StandardCrudRegistry {
  findByResourceKey: (resourceKey: string) => StandardCrudResourceRegistration | undefined;
  findByListViewKey: (viewKey: string) => StandardCrudResourceRegistration | undefined;
  findByDetailViewKey: (viewKey: string) => StandardCrudResourceRegistration | undefined;
}

interface RegistryEntry {
  resource: StandardCrudResourceRegistration;
}

type MetadataRecord = Record<string, unknown>;

function asRecord(value: unknown): MetadataRecord {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as MetadataRecord
    : {};
}

function validateRetiredDetailMetadata(modelDocuments: RuntimeMetadataState['modelDocuments']): void {
  for (const document of modelDocuments) {
    for (const [modelKey, modelValue] of Object.entries(document)) {
      if (modelKey === 'schemaVersion' || modelKey === 'type') {
        continue;
      }

      const model = asRecord(modelValue);
      const fields = Array.isArray(model.fields) ? model.fields : [];
      for (const fieldValue of fields) {
        const field = asRecord(fieldValue);
        const fieldKey = typeof field.name === 'string' ? field.name : '<unknown>';
        if (Object.prototype.hasOwnProperty.call(field, 'view')) {
          throw new Error(`retired detail metadata "${modelKey}.${fieldKey}.view" is not allowed`);
        }
        if (Object.prototype.hasOwnProperty.call(asRecord(field.form), 'view')) {
          throw new Error(`retired detail metadata "${modelKey}.${fieldKey}.form.view" is not allowed`);
        }
      }
    }
  }
}

/**
 * 在活动模块注册表替换前校验整份标准 CRUD metadata 与 binding candidate。
 */
export function validateStandardCrudCandidate(
  modules: FrontendModule[],
  metadata: RuntimeMetadataState,
): void {
  validateRetiredDetailMetadata(metadata.modelDocuments);

  for (const module of modules) {
    for (const resource of module.standardCrudResources ?? []) {
      const registration = resource as StandardCrudResourceRegistration & MetadataRecord;
      for (const retiredKey of ['detailActions', 'renderDetail', 'renderDetailHeaderActions']) {
        if (Object.prototype.hasOwnProperty.call(registration, retiredKey)) {
          throw new Error(
            `retired standard CRUD registration property "${retiredKey}" is not allowed on "${resource.resourceKey}"`,
          );
        }
      }

      if (resource.detailViewKey) {
        resolveStandardCrudDetailSchema(
          metadata.viewDocuments as unknown as MetadataRecord[],
          resource,
          {
            modelDocuments: metadata.modelDocuments as unknown as MetadataRecord[],
          },
        );
      }
    }
  }
}

function increment(counts: Map<string, number>, key: string | undefined): void {
  if (!key) {
    return;
  }

  counts.set(key, (counts.get(key) ?? 0) + 1);
}

function isDuplicate(counts: Map<string, number>, key: string | undefined): boolean {
  return key !== undefined && (counts.get(key) ?? 0) > 1;
}

function hasConflict(
  resource: StandardCrudResourceRegistration,
  resourceKeyCounts: Map<string, number>,
  viewKeyCounts: Map<string, number>,
): boolean {
  return (
    isDuplicate(resourceKeyCounts, resource.resourceKey) ||
    isDuplicate(viewKeyCounts, resource.listViewKey) ||
    isDuplicate(viewKeyCounts, resource.detailViewKey)
  );
}

export function buildStandardCrudRegistry(modules: FrontendModule[]): StandardCrudRegistry {
  const entries: RegistryEntry[] = [];
  const resourceKeyCounts = new Map<string, number>();
  const viewKeyCounts = new Map<string, number>();

  for (const module of modules) {
    for (const resource of module.standardCrudResources ?? []) {
      entries.push({ resource });
      increment(resourceKeyCounts, resource.resourceKey);
      increment(viewKeyCounts, resource.listViewKey);
      increment(viewKeyCounts, resource.detailViewKey);
    }
  }

  const byResourceKey = new Map<string, StandardCrudResourceRegistration>();
  const byListViewKey = new Map<string, StandardCrudResourceRegistration>();
  const byDetailViewKey = new Map<string, StandardCrudResourceRegistration>();
  const reportedResourceKeys = new Set<string>();
  const reportedViewKeys = new Set<string>();

  for (const { resource } of entries) {
    if (isDuplicate(resourceKeyCounts, resource.resourceKey)) {
      if (!reportedResourceKeys.has(resource.resourceKey)) {
        console.error(
          `Duplicate standard CRUD resource registration detected for resourceKey "${resource.resourceKey}". The key is ignored.`,
        );
        reportedResourceKeys.add(resource.resourceKey);
      }
      continue;
    }

    if (isDuplicate(viewKeyCounts, resource.listViewKey)) {
      if (!reportedViewKeys.has(resource.listViewKey)) {
        console.error(
          `Duplicate standard CRUD view registration detected for viewKey "${resource.listViewKey}". The key is ignored.`,
        );
        reportedViewKeys.add(resource.listViewKey);
      }
    }

    if (resource.detailViewKey) {
      if (isDuplicate(viewKeyCounts, resource.detailViewKey)) {
        if (!reportedViewKeys.has(resource.detailViewKey)) {
          console.error(
            `Duplicate standard CRUD view registration detected for viewKey "${resource.detailViewKey}". The key is ignored.`,
          );
          reportedViewKeys.add(resource.detailViewKey);
        }
      }
    }

    if (hasConflict(resource, resourceKeyCounts, viewKeyCounts)) {
      continue;
    }

    byResourceKey.set(resource.resourceKey, resource);
    byListViewKey.set(resource.listViewKey, resource);
    if (resource.detailViewKey) {
      byDetailViewKey.set(resource.detailViewKey, resource);
    }
  }

  return {
    findByResourceKey: (resourceKey) => byResourceKey.get(resourceKey),
    findByListViewKey: (viewKey) => byListViewKey.get(viewKey),
    findByDetailViewKey: (viewKey) => byDetailViewKey.get(viewKey),
  };
}
