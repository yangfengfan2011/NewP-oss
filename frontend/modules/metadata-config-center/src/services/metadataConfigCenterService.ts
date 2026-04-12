export interface MetadataAuthoringOptions {
  editable: boolean;
}

export interface MetadataTypeDescriptor {
  enabled: boolean;
  sourcePath: string;
  adapter?: string;
  authoring?: MetadataAuthoringOptions;
}

export interface MetadataModuleManifest {
  schemaVersion: string;
  moduleId: string;
  metadataRoot: string;
  types: Record<string, MetadataTypeDescriptor>;
}

export interface SettingDefinitionDto {
  key: string;
  displayName: string;
  description?: string | null;
  valueType: string;
  defaultValue: string;
  required: boolean;
  isSensitive: boolean;
  supportsOverride: boolean;
  group: string;
  sourceFile: string;
  versionSummary: string;
}

export interface SaveSettingDefinitionsRequest {
  definitions: SettingDefinitionDto[];
}

export interface SaveSettingDefinitionsResult {
  definitions: SettingDefinitionDto[];
  status: string;
  hasUnsavedChanges: boolean;
  hasUnappliedChanges: boolean;
  gitSyncPending: boolean;
}

async function readJson<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || '元数据请求失败');
  }

  return response.json() as Promise<T>;
}

export async function getModules(): Promise<MetadataModuleManifest[]> {
  const response = await fetch('/api/metadata-config/modules');
  return readJson<MetadataModuleManifest[]>(response);
}

export async function getSettings(moduleId: string): Promise<SettingDefinitionDto[]> {
  const response = await fetch(`/api/metadata-config/modules/${encodeURIComponent(moduleId)}/settings`);
  return readJson<SettingDefinitionDto[]>(response);
}

export async function saveSettings(
  moduleId: string,
  payload: SaveSettingDefinitionsRequest,
): Promise<SaveSettingDefinitionsResult> {
  const response = await fetch(`/api/metadata-config/modules/${encodeURIComponent(moduleId)}/settings`, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(payload),
  });

  return readJson<SaveSettingDefinitionsResult>(response);
}
