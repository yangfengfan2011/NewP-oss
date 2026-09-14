import type {
  RuntimeMetadataManifest,
  RuntimeMetadataState,
  RuntimeModelDocument,
  RuntimeNavItemDocument,
  RuntimeViewDocument,
} from './runtimeMetadataManifest';

const DEFAULT_RUNTIME_METADATA_MANIFEST_URL = '/plugins/metadata-runtime-manifest.json';
const RUNTIME_METADATA_LOAD_CONCURRENCY = 8;
const inFlightLoads = new WeakMap<typeof fetch, Promise<RuntimeMetadataState>>();
let preloadedRuntimeMetadata: RuntimeMetadataState | null = null;

type LoadedRuntimeMetadataDocument =
  | { kind: 'nav'; document: RuntimeNavItemDocument }
  | { kind: 'view'; document: RuntimeViewDocument }
  | { kind: 'model'; document: RuntimeModelDocument };

export function getPreloadedRuntimeMetadata(): RuntimeMetadataState | null {
  return preloadedRuntimeMetadata;
}

export function setPreloadedRuntimeMetadata(metadata: RuntimeMetadataState): void {
  preloadedRuntimeMetadata = metadata;
}

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

export function loadRuntimeMetadata(fetcher: typeof fetch = fetch): Promise<RuntimeMetadataState> {
  const existingLoad = inFlightLoads.get(fetcher);
  if (existingLoad) {
    return existingLoad;
  }

  let trackedLoad!: Promise<RuntimeMetadataState>;
  // finally 必须按 Promise 身份清理当前 in-flight 记录，因此需要先声明再赋值。
  // eslint-disable-next-line prefer-const
  trackedLoad = loadRuntimeMetadataOnce(fetcher).finally(() => {
    if (inFlightLoads.get(fetcher) === trackedLoad) {
      inFlightLoads.delete(fetcher);
    }
  });
  inFlightLoads.set(fetcher, trackedLoad);
  return trackedLoad;
}

async function loadRuntimeMetadataOnce(fetcher: typeof fetch): Promise<RuntimeMetadataState> {
  const manifestResponse = await fetcher(DEFAULT_RUNTIME_METADATA_MANIFEST_URL, {
    credentials: 'same-origin',
  });

  if (!manifestResponse.ok) {
    throw new Error(`Failed to load metadata runtime manifest: ${manifestResponse.status}`);
  }

  const manifest = await manifestResponse.json() as RuntimeMetadataManifest;
  const tasks: Array<() => Promise<LoadedRuntimeMetadataDocument>> = [];

  for (const entry of manifest.modules) {
    for (const relativePath of entry.navFiles) {
      const url = `${entry.metadataBasePath}/${relativePath}`;
      tasks.push(async () => ({
        kind: 'nav',
        document: bindFeaturePositions(await loadJson<RuntimeNavItemDocument>(fetcher, url), entry.features ?? []),
      }));
    }

    for (const relativePath of entry.viewFiles) {
      const url = `${entry.metadataBasePath}/${relativePath}`;
      tasks.push(async () => ({
        kind: 'view',
        document: await loadJson<RuntimeViewDocument>(fetcher, url),
      }));
    }

    for (const relativePath of entry.modelFiles) {
      const url = `${entry.metadataBasePath}/${relativePath}`;
      tasks.push(async () => ({
        kind: 'model',
        document: await loadJson<RuntimeModelDocument>(fetcher, url),
      }));
    }
  }

  // 并发只缩短 I/O 等待；结果仍按 manifest 顺序归并，且全部成功前不发布部分快照。
  const loadedDocuments = await mapWithConcurrency(
    tasks,
    RUNTIME_METADATA_LOAD_CONCURRENCY,
    (task) => task(),
  );
  const navDocuments: RuntimeNavItemDocument[] = [];
  const viewDocuments: RuntimeViewDocument[] = [];
  const modelDocuments: RuntimeModelDocument[] = [];
  for (const loaded of loadedDocuments) {
    switch (loaded.kind) {
      case 'nav':
        navDocuments.push(loaded.document);
        break;
      case 'view':
        viewDocuments.push(loaded.document);
        break;
      case 'model':
        modelDocuments.push(loaded.document);
        break;
    }
  }

  return {
    manifest,
    navDocuments,
    viewDocuments,
    modelDocuments,
  };
}

async function mapWithConcurrency<T, TResult>(
  items: T[],
  concurrency: number,
  map: (item: T, index: number) => Promise<TResult>,
): Promise<TResult[]> {
  const results = new Array<TResult>(items.length);
  let nextIndex = 0;
  const worker = async () => {
    while (nextIndex < items.length) {
      const currentIndex = nextIndex;
      nextIndex += 1;
      results[currentIndex] = await map(items[currentIndex], currentIndex);
    }
  };

  await Promise.all(
    Array.from({ length: Math.min(concurrency, items.length) }, () => worker()),
  );
  return results;
}

// 引用只解析正式编译目录；缺失或歧义声明不可退回旧权限入口。
function bindFeaturePositions(document: RuntimeNavItemDocument, features: NonNullable<RuntimeMetadataManifest["modules"][number]["features"]>): RuntimeNavItemDocument {
  return { ...document, navigation: { ...document.navigation, items: document.navigation.items.map(item => {
    const declared = features.filter(point => point.referenceKey === (item.featureOwner ?? `navigation:${item.key}`));
    if (!item.featureKey && !item.featureAuthorization && !item.featureOwner && declared.length === 0) return item;
    const matches = item.featureKey ? features.filter(point => point.key === item.featureKey && point.kind === "navigation") : declared;
    if (matches.length !== 1) throw new Error(`导航功能引用无效：${item.featureKey}`);
    return { ...item, featurePosition: matches[0].position };
  }) } };
}