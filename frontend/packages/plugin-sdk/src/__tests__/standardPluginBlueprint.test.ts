import { describe, expect, it } from 'vitest';
import {
  platformApiReleaseIdentity,
  projectStandardPluginBlueprint,
  validateStandardPluginBlueprint,
  type StandardPluginBlueprint,
} from '../index';

const blueprint = (): StandardPluginBlueprint => ({
  schemaVersion: '1.0',
  profile: 'standard-data.v1',
  moduleId: 'Partner.AssetCatalog',
  displayName: '资产目录',
  version: '1.0.0',
  description: 'Partner-owned asset catalog',
  compatibility: {
    platformApi: { minInclusive: '1.0.0', maxExclusive: '2.0.0' },
    persistence: { contract: 'ef-migrations.v1', providers: ['sqlServer'] },
  },
  standardData: {
    resources: [
      {
        key: 'Asset',
        pluralName: 'Assets',
        displayName: '资产',
        displayField: 'Name',
        detailViewKey: 'partner.asset.detail',
        fields: [
          { name: 'Code', type: 'string', required: true, unique: true },
          { name: 'Name', type: 'string', required: true },
          { name: 'PurchasedAt', type: 'date', required: false },
        ],
      },
    ],
  },
  navigation: { slot: 'extensions', group: '扩展示例', menu: '资产目录', order: 100 },
  persistence: {
    models: ['metadata/models/Asset.model.json'],
    migrations: {
      sqlServer: {
        assembly: 'migrations/sqlserver/Partner.AssetCatalog.Migrations.dll',
        manifest: 'migrations/sqlserver/migrations.json',
      },
    },
  },
});

describe('@icp-platform/plugin-sdk', () => {
  it('shares release identity and derives the canonical standard frontend projection', () => {
    const input = blueprint();

    expect(validateStandardPluginBlueprint(input)).toEqual([]);
    expect(platformApiReleaseIdentity).toMatchObject({
      version: '1.0.0',
      profile: 'standard-data.v1',
      releaseDigest: 'sha256:e6f17cb0ca9747b06c83d9803d7cc634cff32c3e1234306389bc106a6fc3e273',
    });
    expect(projectStandardPluginBlueprint(input).resources[0]).toMatchObject({
      resourceKey: 'Asset',
      pageRoute: '/plugins/partner-asset-catalog/assets',
      detailViewKey: 'partner.asset.detail',
      permissionCodes: [],
    });
  });

  it('rejects an empty declared detail view key', () => {
    const input = blueprint();
    input.standardData.resources[0].detailViewKey = '';

    expect(validateStandardPluginBlueprint(input)).toContainEqual(expect.objectContaining({
      code: 'PACKAGE_SCHEMA_UNSUPPORTED',
      jsonPointer: '/standardData/resources/0/detailViewKey',
    }));
  });

  it('rejects runtime code and unsupported profiles with stable diagnostics', () => {
    const input = {
      ...blueprint(),
      profile: 'custom-code.v1',
      backend: { entryAssembly: 'Partner.AssetCatalog.dll' },
      frontend: { entry: 'dist/index.js' },
    };

    expect(validateStandardPluginBlueprint(input).map((diagnostic) => diagnostic.code)).toEqual([
      'PLUGIN_PROFILE_UNSUPPORTED',
      'STANDARD_PROFILE_BACKEND_CODE_FORBIDDEN',
      'STANDARD_PROFILE_FRONTEND_CODE_FORBIDDEN',
    ]);
  });
});
