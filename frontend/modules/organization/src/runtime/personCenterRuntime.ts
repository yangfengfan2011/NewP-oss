import type {
  PersonDetailRuntimeSchema,
  PersonListRuntimeAdvancedSection,
  PersonListRuntimeField,
  PersonListRuntimeSchema,
} from './personCenterRuntime.types';

interface RuntimeMetadataState {
  viewDocuments: Array<Record<string, unknown>>;
  modelDocuments: Array<Record<string, unknown>>;
}

declare global {
  interface Window {
    __ICP_RUNTIME_METADATA__?: RuntimeMetadataState;
  }
}

interface RuntimeViewEnvelope {
  view?: Record<string, unknown>;
}

interface RuntimeModelEnvelope {
  Person?: {
    sections?: Array<{
      key?: string;
      titleKey?: string;
      fields?: string[];
    }>;
  };
}

const fallbackPersonListSchema: PersonListRuntimeSchema = {
  dicKey: 'PersonListView',
  titleKey: 'Title',
  descriptionKey: 'Description',
  editorMode: 'drawer',
  detailViewKey: 'organization.person.detail',
  queryFields: [
    { key: 'keyword', labelKey: 'Query.Keyword', source: 'preset', preset: 'keyword', operator: 'contains' },
    { key: 'companyId', labelKey: 'Query.Company', source: 'model', field: 'CompanyId', operator: '=' },
    { key: 'departmentId', labelKey: 'Query.Department', source: 'model', field: 'DepartmentId', operator: '=' },
    { key: 'employmentStatus', labelKey: 'Query.EmploymentStatus', source: 'model', field: 'EmploymentStatus', operator: '=' },
  ],
  advancedFilter: {
    enabled: false,
    titleKey: 'AdvancedFilter.Title',
    sections: [],
  },
  pageActions: [
    { key: 'create', labelKey: 'Action.Create' },
    { key: 'refresh', labelKey: 'Action.Refresh' },
  ],
  rowActions: [
    { key: 'view', labelKey: 'Action.View' },
    { key: 'edit', labelKey: 'Action.Edit' },
    { key: 'disable', labelKey: 'Action.Disable' },
  ],
  listColumns: [
    { key: 'personNo', labelKey: 'Column.PersonNo' },
    { key: 'name', labelKey: 'Column.Name' },
    { key: 'companyName', labelKey: 'Column.CompanyName' },
    { key: 'departmentName', labelKey: 'Column.DepartmentName' },
    { key: 'employmentStatus', labelKey: 'Column.EmploymentStatus' },
    { key: 'isEnabled', labelKey: 'Column.IsEnabled' },
  ],
  exportModes: ['current', 'all'],
};

const fallbackPersonDetailSchema: PersonDetailRuntimeSchema = {
  dicKey: 'PersonDetailView',
  titleKey: 'Title',
  descriptionKey: 'Description',
  sections: [
    { key: 'basic', titleKey: 'Section.Basic', fields: ['PersonNo', 'Name'] },
    {
      key: 'organization',
      titleKey: 'Section.Organization',
      fields: ['CompanyId', 'DepartmentId', 'PositionId', 'EmploymentStatus'],
    },
  ],
  summaryBlocks: [
    { key: 'userLink', titleKey: 'Block.UserLink' },
    { key: 'authorizationSummary', titleKey: 'Block.AuthorizationSummary' },
  ],
  pageActions: [
    { key: 'disable', labelKey: 'Action.Disable' },
  ],
};

function getRuntimeMetadata(): RuntimeMetadataState | undefined {
  return window.__ICP_RUNTIME_METADATA__;
}

function getViewDocument(viewKey: string): RuntimeViewEnvelope | undefined {
  return getRuntimeMetadata()?.viewDocuments.find((document) => {
    const view = (document as RuntimeViewEnvelope).view;
    return typeof view?.key === 'string' && view.key === viewKey;
  }) as RuntimeViewEnvelope | undefined;
}

function getPersonModelDocument(): RuntimeModelEnvelope | undefined {
  return getRuntimeMetadata()?.modelDocuments.find((document) => 'Person' in document) as RuntimeModelEnvelope | undefined;
}

function readString(value: unknown, fallback: string): string {
  return typeof value === 'string' && value.length > 0 ? value : fallback;
}

function readArray<T>(value: unknown): T[] {
  return Array.isArray(value) ? (value as T[]) : [];
}

function buildRuntimeField(field: Record<string, unknown>): PersonListRuntimeField {
  return {
    key: readString(field.key, ''),
    labelKey: readString(field.labelKey, ''),
    source: field.source === 'model' ? 'model' : 'preset',
    field: typeof field.field === 'string' ? field.field : undefined,
    preset: typeof field.preset === 'string' ? field.preset : undefined,
    operator: typeof field.operator === 'string' ? field.operator : undefined,
  };
}

function buildAdvancedSections(value: unknown): PersonListRuntimeAdvancedSection[] {
  return readArray<Record<string, unknown>>(value).map((section) => ({
    key: readString(section.key, ''),
    titleKey: readString(section.titleKey, ''),
    fields: readArray<Record<string, unknown>>(section.fields).map(buildRuntimeField),
  })).filter((section) => section.key.length > 0);
}

export function getPersonListRuntimeSchema(): PersonListRuntimeSchema {
  const viewDocument = getViewDocument('organization.person.list');
  const view = viewDocument?.view ?? {};
  const queryFields: PersonListRuntimeField[] = readArray<Record<string, unknown>>(view.query && (view.query as Record<string, unknown>).fields).map(buildRuntimeField);
  const advancedFilterRecord = typeof view.advancedFilter === 'object' && view.advancedFilter !== null
    ? view.advancedFilter as Record<string, unknown>
    : null;
  const pageActions = readArray<Record<string, unknown>>(view.pageActions).map((action) => ({
    key: readString(action.key, ''),
    labelKey: readString(action.labelKey, ''),
  }));
  const rowActions = readArray<Record<string, unknown>>(view.rowActions).map((action) => ({
    key: readString(action.key, ''),
    labelKey: readString(action.labelKey, ''),
  }));
  const listColumns = readArray<Record<string, unknown>>(view.columns).map((column) => ({
    key: readString(column.key, ''),
    labelKey: readString(column.labelKey, ''),
  }));
  const exportModes = readArray<string>(view.export && (view.export as Record<string, unknown>).modes)
    .filter((mode): mode is string => typeof mode === 'string');
  const advancedSections = buildAdvancedSections(advancedFilterRecord?.sections);

  return {
    dicKey: readString(view.dicKey, fallbackPersonListSchema.dicKey),
    titleKey: readString(view.titleKey, fallbackPersonListSchema.titleKey),
    descriptionKey: readString(view.descriptionKey, fallbackPersonListSchema.descriptionKey),
    editorMode: view.editorMode === 'page' || view.editorMode === 'dialog' || view.editorMode === 'drawer'
      ? view.editorMode
      : fallbackPersonListSchema.editorMode,
    detailViewKey: readString(view.detailViewKey, fallbackPersonListSchema.detailViewKey ?? ''),
    queryFields: queryFields.length > 0 ? queryFields : fallbackPersonListSchema.queryFields,
    advancedFilter: advancedFilterRecord && advancedFilterRecord.enabled === true
      ? {
        enabled: true,
        titleKey: readString(advancedFilterRecord.titleKey, fallbackPersonListSchema.advancedFilter.titleKey),
        sections: advancedSections.length > 0 ? advancedSections : fallbackPersonListSchema.advancedFilter.sections,
      }
      : fallbackPersonListSchema.advancedFilter,
    pageActions: pageActions.length > 0 ? pageActions : fallbackPersonListSchema.pageActions,
    rowActions: rowActions.length > 0 ? rowActions : fallbackPersonListSchema.rowActions,
    listColumns: listColumns.length > 0 ? listColumns : fallbackPersonListSchema.listColumns,
    exportModes: exportModes.length > 0 ? exportModes : fallbackPersonListSchema.exportModes,
  };
}

export function getPersonDetailRuntimeSchema(): PersonDetailRuntimeSchema {
  const viewDocument = getViewDocument('organization.person.detail');
  const view = viewDocument?.view ?? {};
  const modelDocument = getPersonModelDocument();
  const sectionsFromModel = readArray<{ key?: string; titleKey?: string; fields?: string[] }>(modelDocument?.Person?.sections)
    .filter((section) => typeof section.key === 'string');
  const sectionOrder = readArray<string>(view.sections);
  const sections = sectionOrder
    .map((sectionKey) => sectionsFromModel.find((section) => section.key === sectionKey))
    .filter((section): section is { key?: string; titleKey?: string; fields?: string[] } => Boolean(section))
    .map((section) => ({
      key: readString(section.key, ''),
      titleKey: readString(section.titleKey, `Section.${section.key}`),
      fields: readArray<string>(section.fields),
    }));
  const summaryBlocks = readArray<string>(view.summaryBlocks).map((blockKey) => ({
    key: blockKey,
    titleKey: `Block.${blockKey === 'userLink' ? 'UserLink' : 'AuthorizationSummary'}`,
  }));
  const pageActions = readArray<Record<string, unknown>>(view.pageActions).map((action) => ({
    key: readString(action.key, ''),
    labelKey: readString(action.labelKey, ''),
  }));

  return {
    dicKey: readString(view.dicKey, fallbackPersonDetailSchema.dicKey),
    titleKey: readString(view.titleKey, fallbackPersonDetailSchema.titleKey),
    descriptionKey: readString(view.descriptionKey, fallbackPersonDetailSchema.descriptionKey),
    sections: sections.length > 0 ? sections : fallbackPersonDetailSchema.sections,
    summaryBlocks: summaryBlocks.length > 0 ? summaryBlocks : fallbackPersonDetailSchema.summaryBlocks,
    pageActions: pageActions.length > 0 ? pageActions : fallbackPersonDetailSchema.pageActions,
  };
}
