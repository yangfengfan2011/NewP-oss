export interface PlatformApiDiagnostic {
  code: string;
  stage: string;
  message: string;
  jsonPointer?: string;
}

export interface StandardPluginBlueprint {
  schemaVersion: '1.0';
  profile: 'standard-data.v1';
  moduleId: string;
  displayName: string;
  version: string;
  description?: string;
  compatibility: {
    platformApi: {
      minInclusive: string;
      maxExclusive: string;
    };
    persistence: {
      contract: 'ef-migrations.v1';
      providers: ['sqlServer'];
    };
  };
  standardData: {
    resources: StandardDataResourceBlueprint[];
  };
  navigation: {
    slot: string;
    group: string;
    menu: string;
    order: number;
  };
  persistence: {
    models: string[];
    migrations: {
      sqlServer: {
        assembly: string;
        manifest: string;
      };
    };
  };
}

export interface StandardDataResourceBlueprint {
  key: string;
  pluralName: string;
  displayName: string;
  displayField: string;
  detailViewKey?: string;
  fields: StandardDataFieldBlueprint[];
}

export interface StandardDataFieldBlueprint {
  name: string;
  type: 'string' | 'date' | 'dateTime' | 'boolean' | 'integer' | 'decimal';
  required: boolean;
  unique?: boolean;
}

export interface StandardPluginProjection {
  moduleSlug: string;
  resources: StandardDataResourceProjection[];
}

export interface StandardDataResourceProjection {
  resourceKey: string;
  apiRoute: string;
  pageRoute: string;
  schemaName: string;
  tableName: string;
  uniqueIndexes: string[];

  detailViewKey?: string;
}
