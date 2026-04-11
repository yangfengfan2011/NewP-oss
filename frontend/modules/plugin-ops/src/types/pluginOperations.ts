export type PluginModuleStatusValue =
  | 'NotInstalled'
  | 'Installed'
  | 'Enabled'
  | 'Failed';

export interface PluginModuleStatus {
  moduleId: string;
  displayName: string;
  installedVersions: string[];
  currentEnabledVersion: string | null;
  status: PluginModuleStatusValue | string;
  lastOperation: string | null;
  lastError: string | null;
  canEnable: boolean;
  canDisable: boolean;
  canUninstall: boolean;
  canRollback: boolean;
}

export interface PluginUploadVerifyResult {
  uploadId: string;
  moduleId: string;
  displayName: string;
  version: string;
  isValid: boolean;
  errors: string[];
}

export interface PluginOperationResult {
  moduleId: string;
  version: string | null;
  status: string;
  message: string;
}
