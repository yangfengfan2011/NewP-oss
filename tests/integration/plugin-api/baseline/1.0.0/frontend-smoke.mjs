import assert from 'node:assert/strict';
import {
  platformApiReleaseIdentity,
  projectStandardPluginBlueprint,
  validateStandardPluginBlueprint,
} from '@icp-platform/plugin-sdk';
import { readFile } from 'node:fs/promises';

const blueprint = JSON.parse(await readFile(new URL('./newp.plugin.json', import.meta.url), 'utf8'));
assert.deepEqual(validateStandardPluginBlueprint(blueprint), []);
assert.equal(platformApiReleaseIdentity.version, '1.0.0');
assert.equal(platformApiReleaseIdentity.profile, 'standard-data.v1');
const projection = projectStandardPluginBlueprint(blueprint);
assert.equal(projection.resources[0].apiRoute, '/api/plugins/partner-asset-catalog/assets');
assert.equal(projection.resources[0].pageRoute, '/plugins/partner-asset-catalog/assets');
assert.equal(projection.resources[0].detailViewKey, 'partner.asset.detail');
console.log(JSON.stringify({ status: 'passed', release: platformApiReleaseIdentity.version }));
