export type PluginModuleStatusValue =
  | 'NotInstalled'
  | 'Installed'
  | 'Enabled'
  | 'Disabled'
  | 'PendingRestart'
  | 'Failed';

export interface PluginModuleStatus {
  moduleId: string;
  displayName: string;
  installedVersions: string[];
  desiredVersion: string | null;
  desiredEnabled: boolean;
  activeVersion: string | null;
  lastKnownGoodVersion: string | null;
  restartRequired: boolean;
  /** 一期只读兼容字段；值等于 activeVersion。 */
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
