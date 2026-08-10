export interface RuntimeMetadataModuleEntry {
  moduleId: string;
  metadataBasePath: string;
  navFiles: string[];
  viewFiles: string[];
  modelFiles: string[];
}

export interface RuntimeMetadataManifest {
  modules: RuntimeMetadataModuleEntry[];
}

export interface RuntimeNavItemDocument {
  schemaVersion?: string;
  type: 'nav';
  navigation: {
    items: RuntimeNavItem[];
  };
}

export interface RuntimeNavItem {
  key: string;
  titleKey?: string;
  icon?: string;
  path?: string;
  parentKey?: string;
  order?: number;
  permissions?: string[];
  localAdministratorOnly?: boolean;
  shellViews?: string[];
  entryType?: 'standard-list' | 'custom';
  targetKey?: string;
}

export interface RuntimeViewDocument {
  schemaVersion?: string;
  type: 'view';
  view: Record<string, unknown>;
}

export interface RuntimeModelDocument {
  schemaVersion?: string;
  type: 'model';
  [key: string]: unknown;
}

export interface RuntimeMetadataState {
  manifest: RuntimeMetadataManifest;
  navDocuments: RuntimeNavItemDocument[];
  viewDocuments: RuntimeViewDocument[];
  modelDocuments: RuntimeModelDocument[];
}

