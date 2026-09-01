export type RuntimeLocalizationManifest = {
  defaultLanguage: string;
  supportedLanguages: Array<{
    code: string;
    displayName: string;
    locale: string;
  }>;
  catalogs: Record<string, Record<string, string>>;
};

const inFlightLoads = new WeakMap<typeof fetch, Promise<RuntimeLocalizationManifest>>();

export function getFallbackRuntimeLocalizationManifest(): RuntimeLocalizationManifest {
  return {
    defaultLanguage: 'zhs',
    supportedLanguages: [
      {
        code: 'zhs',
        displayName: '简体中文',
        locale: 'zh-CN',
      },
    ],
    catalogs: {
      zhs: {},
    },
  };
}

export function loadRuntimeLocalizationManifest(
  fetcher: typeof fetch = fetch,
): Promise<RuntimeLocalizationManifest> {
  const existingLoad = inFlightLoads.get(fetcher);
  if (existingLoad) {
    return existingLoad;
  }

  let trackedLoad!: Promise<RuntimeLocalizationManifest>;
  // finally 必须按 Promise 身份清理当前 in-flight 记录，因此需要先声明再赋值。
  // eslint-disable-next-line prefer-const
  trackedLoad = loadRuntimeLocalizationManifestOnce(fetcher).finally(() => {
    if (inFlightLoads.get(fetcher) === trackedLoad) {
      inFlightLoads.delete(fetcher);
    }
  });
  inFlightLoads.set(fetcher, trackedLoad);
  return trackedLoad;
}

async function loadRuntimeLocalizationManifestOnce(
  fetcher: typeof fetch,
): Promise<RuntimeLocalizationManifest> {
  const response = await fetcher('/plugins/localization-manifest.json', {
    credentials: 'same-origin',
  });

  if (!response.ok) {
    throw new Error(`Failed to load localization manifest: ${response.status}`);
  }

  return response.json() as Promise<RuntimeLocalizationManifest>;
}
