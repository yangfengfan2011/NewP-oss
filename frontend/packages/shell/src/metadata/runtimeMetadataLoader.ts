import type {
  RuntimeMetadataManifest,
  RuntimeMetadataState,
  RuntimeModelDocument,
  RuntimeNavItemDocument,
  RuntimeViewDocument,
} from './runtimeMetadataManifest';

const DEFAULT_RUNTIME_METADATA_MANIFEST_URL = '/plugins/metadata-runtime-manifest.json';

async function loadJson<T>(fetcher: typeof fetch, url: string): Promise<T> {
  const response = await fetcher(url, { credentials: 'same-origin' });
  if (!response.ok) {
    throw new Error(`Failed to load runtime metadata document: ${url} (${response.status})`);
  }

  return response.json() as Promise<T>;
}

export function getFallbackRuntimeMetadata(): RuntimeMetadataState {
  return {
    manifest: { modules: [] },
    navDocuments: [],
    viewDocuments: [],
    modelDocuments: [],
  };
}

export async function loadRuntimeMetadata(fetcher: typeof fetch = fetch): Promise<RuntimeMetadataState> {
  const manifestResponse = await fetcher(DEFAULT_RUNTIME_METADATA_MANIFEST_URL, {
    credentials: 'same-origin',
  });

  if (!manifestResponse.ok) {
    throw new Error(`Failed to load metadata runtime manifest: ${manifestResponse.status}`);
  }

  const manifest = await manifestResponse.json() as RuntimeMetadataManifest;
  const navDocuments: RuntimeNavItemDocument[] = [];
  const viewDocuments: RuntimeViewDocument[] = [];
  const modelDocuments: RuntimeModelDocument[] = [];

  for (const entry of manifest.modules) {
    for (const relativePath of entry.navFiles) {
      navDocuments.push(await loadJson<RuntimeNavItemDocument>(fetcher, `${entry.metadataBasePath}/${relativePath}`));
    }

    for (const relativePath of entry.viewFiles) {
      viewDocuments.push(await loadJson<RuntimeViewDocument>(fetcher, `${entry.metadataBasePath}/${relativePath}`));
    }

    for (const relativePath of entry.modelFiles) {
      modelDocuments.push(await loadJson<RuntimeModelDocument>(fetcher, `${entry.metadataBasePath}/${relativePath}`));
    }
  }

  return {
    manifest,
    navDocuments,
    viewDocuments,
    modelDocuments,
  };
}

