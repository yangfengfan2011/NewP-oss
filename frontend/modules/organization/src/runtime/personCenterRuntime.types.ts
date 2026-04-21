export interface PersonListRuntimeField {
  key: string;
  labelKey: string;
  source: 'model' | 'preset';
  field?: string;
  preset?: string;
  operator?: string;
}

export interface PersonListRuntimeAdvancedSection {
  key: string;
  titleKey: string;
  fields: PersonListRuntimeField[];
}

export interface PersonListRuntimeAdvancedFilter {
  enabled: boolean;
  titleKey: string;
  sections: PersonListRuntimeAdvancedSection[];
}

export interface PersonListRuntimeColumn {
  key: string;
  labelKey: string;
}

export interface PersonListRuntimeAction {
  key: string;
  labelKey: string;
}

export interface PersonListRuntimeSchema {
  dicKey: string;
  titleKey: string;
  descriptionKey: string;
  editorMode: 'page' | 'drawer' | 'dialog';
  detailViewKey?: string;
  queryFields: PersonListRuntimeField[];
  advancedFilter: PersonListRuntimeAdvancedFilter;
  pageActions: PersonListRuntimeAction[];
  rowActions: PersonListRuntimeAction[];
  listColumns: PersonListRuntimeColumn[];
  exportModes: string[];
}

export interface PersonDetailRuntimeSection {
  key: string;
  titleKey: string;
  fields: string[];
}

export interface PersonDetailRuntimeBlock {
  key: string;
  titleKey: string;
}

export interface PersonDetailRuntimeAction {
  key: string;
  labelKey: string;
}

export interface PersonDetailRuntimeSchema {
  dicKey: string;
  titleKey: string;
  descriptionKey: string;
  sections: PersonDetailRuntimeSection[];
  summaryBlocks: PersonDetailRuntimeBlock[];
  pageActions: PersonDetailRuntimeAction[];
}
