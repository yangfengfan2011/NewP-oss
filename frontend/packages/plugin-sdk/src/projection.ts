import type {
  StandardDataResourceProjection,
  StandardPluginBlueprint,
  StandardPluginProjection,
} from './contracts.js';

const separatePascalCase = (value: string, separator: string): string =>
  value.replace(/([a-z0-9])([A-Z])/g, `$1${separator}$2`);

const slugify = (value: string): string =>
  separatePascalCase(value.replace(/[._]/g, '-'), '-').toLowerCase();

const snakeCase = (value: string): string =>
  separatePascalCase(value.replace(/[.-]/g, '_'), '_').toLowerCase();

export const projectStandardPluginBlueprint = (
  blueprint: StandardPluginBlueprint,
): StandardPluginProjection => {
  const moduleSlug = slugify(blueprint.moduleId);
  const resources: StandardDataResourceProjection[] = blueprint.standardData.resources
    .map((resource) => ({
      resourceKey: resource.key,
      apiRoute: `/api/plugins/${moduleSlug}/${slugify(resource.pluralName)}`,
      pageRoute: `/plugins/${moduleSlug}/${slugify(resource.pluralName)}`,
      schemaName: `np_${snakeCase(blueprint.moduleId)}`,
      tableName: resource.pluralName,
      uniqueIndexes: resource.fields
        .filter((field) => field.unique)
        .map((field) => `UX_${resource.pluralName}_${field.name}`)
        .sort(),

      detailViewKey: resource.detailViewKey,
    }))
    .sort((left, right) => left.resourceKey.localeCompare(right.resourceKey));

  return { moduleSlug, resources };
};
