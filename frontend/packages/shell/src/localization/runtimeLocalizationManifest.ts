export type RuntimeLocalizationManifest = {
  defaultLanguage: string;
  supportedLanguages: Array<{
    code: string;
    displayName: string;
  }>;
  catalogs: Record<string, Record<string, string>>;
};

export async function loadRuntimeLocalizationManifest(): Promise<RuntimeLocalizationManifest> {
  const response = await fetch('/plugins/localization-manifest.json', {
    credentials: 'same-origin',
  });

  if (!response.ok) {
    throw new Error(`Failed to load localization manifest: ${response.status}`);
  }

  return response.json() as Promise<RuntimeLocalizationManifest>;
}
