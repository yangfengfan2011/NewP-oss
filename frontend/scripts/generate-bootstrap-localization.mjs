import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';

const root = fileURLToPath(new URL('../../', import.meta.url));
const read = path => JSON.parse(readFileSync(resolve(root, path), 'utf8').replace(/^\uFEFF/, ''));
const languages = read('frontend/modules/platform/metadata/languages.json');
const catalogs = Object.fromEntries(languages.supportedLanguages.map(language => [language.code, {}]));
// 只携带认证、语言入口及恢复所需词条；业务词典仍由活动 runtime 按原边界装载。
const sources = [
  ['Platform', 'PlatformAuthentication'], ['Platform', 'AuthenticationErrors'],
  ['Identity', 'AuthenticationErrors'], ['Platform', 'Persistence'],
  ['Platform', 'Platform', ['Language', 'MutationOutcomeUnknown', 'Diagnostics', 'UnexpectedError', 'Unauthorized', 'Forbidden', 'MutationConflict', 'ValidationFailed']],
];
for (const [module, name, keys] of sources) {
  const path = `frontend/modules/${module.replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase()}/metadata/dictionaries/${name}.dic.json`;
  const entries = read(path)[name];
  for (const key of keys ?? Object.keys(entries)) {
    const fullKey = `${name}.${key}`;
    let parameters;
    for (const { code } of languages.supportedLanguages) {
      const text = entries[key]?.[code];
      if (typeof text !== 'string' || !text.trim()) throw new Error(`${path}: ${fullKey} missing ${code}`);
      if (fullKey in catalogs[code]) throw new Error(`${path}: duplicate ${fullKey}`);
      const currentParameters = JSON.stringify([...new Set([...text.matchAll(/\{([^{}]+)\}/g)].map(match => match[1]))].sort());
      if (parameters !== undefined && parameters !== currentParameters) throw new Error(`${path}: ${fullKey} parameters differ in ${code}`);
      parameters = currentParameters;
      catalogs[code][fullKey] = text;
    }
  }
}
const output = JSON.stringify({ ...languages, catalogs }, null, 2) + '\n';
const destination = resolve(root, 'frontend/packages/shell/src/localization/bootstrapLocalization.generated.json');
if (process.argv.includes('--check')) {
  if (readFileSync(destination, 'utf8').replace(/\r\n/g, '\n') !== output) {
    throw new Error('Bootstrap localization is stale. Run node frontend/scripts/generate-bootstrap-localization.mjs');
  }
  console.log('Bootstrap localization matches authoring.');
} else {
  writeFileSync(destination, output.replace(/\n/g, '\r\n'));
}
