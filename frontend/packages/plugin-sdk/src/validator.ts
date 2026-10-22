import type { PlatformApiDiagnostic, StandardPluginBlueprint } from './contracts.js';
import { platformApiReleaseIdentity } from './identity.js';

const diagnostic = (
  code: string,
  stage: string,
  message: string,
  jsonPointer?: string,
): PlatformApiDiagnostic => ({ code, stage, message, jsonPointer });

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);

export const validateStandardPluginBlueprint = (value: unknown): PlatformApiDiagnostic[] => {
  if (!isRecord(value)) {
    return [diagnostic('PACKAGE_SCHEMA_UNSUPPORTED', 'schema', 'Blueprint must be a JSON object.', '/')];
  }

  const diagnostics: PlatformApiDiagnostic[] = [];
  if (value.schemaVersion !== '1.0') {
    diagnostics.push(diagnostic('PACKAGE_SCHEMA_UNSUPPORTED', 'schema', 'schemaVersion must be 1.0.', '/schemaVersion'));
  }
  if (value.profile !== platformApiReleaseIdentity.profile) {
    diagnostics.push(diagnostic('PLUGIN_PROFILE_UNSUPPORTED', 'profile', 'Only standard-data.v1 is supported.', '/profile'));
  }
  if ('backend' in value) {
    diagnostics.push(diagnostic('STANDARD_PROFILE_BACKEND_CODE_FORBIDDEN', 'profile', 'Backend runtime entries are forbidden.', '/backend'));
  }
  if ('frontend' in value) {
    diagnostics.push(diagnostic('STANDARD_PROFILE_FRONTEND_CODE_FORBIDDEN', 'profile', 'Frontend runtime entries are forbidden.', '/frontend'));
  }

  const isIdentityArray = (input: unknown): input is string[] => Array.isArray(input)
    && input.every((item: unknown) => typeof item === 'string' && item.trim().length > 0);
  // 本地校验只检查输入形状；完整图、实际依赖与必需集合由Host的同一规范编译器裁决。
  if ('dependencies' in value && !isIdentityArray(value.dependencies)) {
    diagnostics.push(diagnostic('PACKAGE_SCHEMA_UNSUPPORTED', 'schema', 'dependencies must contain component identities.', '/dependencies'));
  }
  if ('composition' in value && (!isRecord(value.composition) || !isIdentityArray(value.composition.members)
    || Object.keys(value.composition).some((key) => key !== 'members'))) {
    diagnostics.push(diagnostic('PACKAGE_SCHEMA_UNSUPPORTED', 'schema', 'composition must declare only members.', '/composition'));
  }
  if ('distributionRole' in value) {
    diagnostics.push(diagnostic('PACKAGE_SCHEMA_UNSUPPORTED', 'schema', 'Distribution roles cannot be declared by external authors.', '/distributionRole'));
  }

  const compatibility = isRecord(value.compatibility) ? value.compatibility : undefined;
  const platformApi = compatibility && isRecord(compatibility.platformApi) ? compatibility.platformApi : undefined;
  if (
    !platformApi ||
    platformApi.minInclusive !== platformApiReleaseIdentity.version ||
    platformApi.maxExclusive !== '2.0.0'
  ) {
    diagnostics.push(
      diagnostic(
        'PLATFORM_API_INCOMPATIBLE',
        'compatibility',
        'minInclusive must equal builtWith and maxExclusive must be the next major.',
        '/compatibility/platformApi',
      ),
    );
  }

  const persistence = compatibility && isRecord(compatibility.persistence) ? compatibility.persistence : undefined;
  if (
    !persistence ||
    persistence.contract !== platformApiReleaseIdentity.persistenceContract ||
    !Array.isArray(persistence.providers) ||
    persistence.providers.length !== 1 ||
    persistence.providers[0] !== platformApiReleaseIdentity.sqlServerProvider
  ) {
    diagnostics.push(
      diagnostic(
        'PERSISTENCE_CONTRACT_UNSUPPORTED',
        'compatibility',
        'standard-data.v1 requires ef-migrations.v1 with sqlServer only.',
        '/compatibility/persistence',
      ),
    );
  }

  const standardData = isRecord(value.standardData) ? value.standardData : undefined;
  if (!standardData || !Array.isArray(standardData.resources) || standardData.resources.length === 0) {
    diagnostics.push(
      diagnostic('PACKAGE_SCHEMA_UNSUPPORTED', 'schema', 'At least one standard data resource is required.', '/standardData/resources'),
    );
  } else {
    standardData.resources.forEach((resource, index) => {
      if (isRecord(resource)
        && 'detailViewKey' in resource
        && (typeof resource.detailViewKey !== 'string' || resource.detailViewKey.trim().length === 0)) {
        diagnostics.push(diagnostic(
          'PACKAGE_SCHEMA_UNSUPPORTED',
          'schema',
          'Declared detailViewKey must not be empty.',
          `/standardData/resources/${index}/detailViewKey`,
        ));
      }
    });
  }

  return diagnostics.sort((left, right) =>
    left.code.localeCompare(right.code) || (left.jsonPointer ?? '').localeCompare(right.jsonPointer ?? ''),
  );
};

export const isStandardPluginBlueprint = (value: unknown): value is StandardPluginBlueprint =>
  validateStandardPluginBlueprint(value).length === 0;
