export interface RuntimeMetadataModuleEntry {
  moduleId: string;
  features?: { key: string; position: string; kind: string; referenceKey?: string; parentPosition?: string | null; staticScope?: string | null; actionScope?: string | null; container?: boolean }[];
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
  featureKey?: string;
  featureAuthorization?: boolean;
  featureOwner?: string;
  featurePosition?: string;
  key: string;
  titleKey?: string;
  icon?: string;
  path?: string;
  parentKey?: string;
  order?: number;
  permissions?: string[];
  localAdministratorOnly?: boolean;
  shellViews?: string[];
  entryType?: 'standard-list' | 'custom' | 'standard-crud-detail';
  routeOnly?: boolean;
  redirectTo?: string;
  targetKey?: string;
  page?: import("@icp-platform/sdk").PageRef;
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

