import type {
  StandardCrudResolvedDetailFieldSchema,
  StandardCrudResolvedDetailSchema,
  StandardCrudRuntimeActionSchema,
} from '../metadata/standardCrudSchemaResolver';

export type StandardCrudDetailDiagnosticCode =
  | 'STANDARD_CRUD_DETAIL_FIELD_UNAVAILABLE'
  | 'STANDARD_CRUD_DETAIL_SECTION_UNAVAILABLE'
  | 'STANDARD_CRUD_DETAIL_CRITICAL_ID_MISSING'
  | 'STANDARD_CRUD_DETAIL_CRITICAL_ID_INVALID'
  | 'STANDARD_CRUD_DETAIL_CRITICAL_ID_MISMATCH'
  | 'STANDARD_CRUD_DETAIL_ACTION_DEPENDENCY_UNAVAILABLE';

export interface StandardCrudDetailDiagnostic {
  code: StandardCrudDetailDiagnosticCode;
  sectionKey?: string;
  fieldKey?: string;
  actionKey?: string;
}

export interface StandardCrudDetailFieldPresentation extends StandardCrudResolvedDetailFieldSchema {
  state: 'present' | 'unavailable';
  value?: unknown;
}

export interface StandardCrudDetailSectionPresentation {
  key: string;
  titleKey: string;
  state: 'available' | 'unavailable';
  fields: StandardCrudDetailFieldPresentation[];
}

export interface StandardCrudDetailActionPresentation extends StandardCrudRuntimeActionSchema {
  disabled: boolean;
  disabledReasonKey?: string;
}

export type StandardCrudDetailPresentation =
  | {
    kind: 'fatal';
    diagnostic: StandardCrudDetailDiagnostic;
  }
  | {
    kind: 'ready';
    sourceFields: Record<string, unknown>;
    sections: StandardCrudDetailSectionPresentation[];
    pageActions: StandardCrudDetailActionPresentation[];
    diagnostics: StandardCrudDetailDiagnostic[];
  };

export interface ResolveStandardCrudDetailPresentationInput {
  schema: StandardCrudResolvedDetailSchema;
  fields: Record<string, unknown>;
  expectedId: string;
}

const owns = (fields: Record<string, unknown>, key: string) =>
  Object.prototype.hasOwnProperty.call(fields, key);

const isAvailableProperty = (fields: Record<string, unknown>, key: string) =>
  owns(fields, key) && fields[key] !== undefined;

/** Reads a companion display value without replacing the raw field retained by the response. */
export function readStandardCrudFieldDisplayValue(
  fields: Record<string, unknown>,
  fieldKey: string,
): unknown {
  const displayValue = fields[`${fieldKey}_DisplayValue`];
  const hasDisplayValue = displayValue !== null
    && displayValue !== undefined
    && (typeof displayValue !== 'string' || displayValue.trim().length > 0);
  return hasDisplayValue ? displayValue : fields[fieldKey];
}

/**
 * Interprets one detail response against resolved page structure without turning response fields into candidate metadata.
 */
export function resolveStandardCrudDetailPresentation({
  schema,
  fields,
  expectedId,
}: ResolveStandardCrudDetailPresentationInput): StandardCrudDetailPresentation {
  if (!owns(fields, 'EID')) {
    return {
      kind: 'fatal',
      diagnostic: { code: 'STANDARD_CRUD_DETAIL_CRITICAL_ID_MISSING', fieldKey: 'EID' },
    };
  }

  const eid = fields.EID;
  if (typeof eid !== 'string' || eid.trim().length === 0) {
    return {
      kind: 'fatal',
      diagnostic: { code: 'STANDARD_CRUD_DETAIL_CRITICAL_ID_INVALID', fieldKey: 'EID' },
    };
  }

  if (eid !== expectedId) {
    return {
      kind: 'fatal',
      diagnostic: { code: 'STANDARD_CRUD_DETAIL_CRITICAL_ID_MISMATCH', fieldKey: 'EID' },
    };
  }

  const diagnostics: StandardCrudDetailDiagnostic[] = [];
  const sections = schema.sections.map<StandardCrudDetailSectionPresentation>((section) => {
    const sectionFields = section.fields.map<StandardCrudDetailFieldPresentation>((field) => {
      if (isAvailableProperty(fields, field.key)) {
        return {
          ...field,
          state: 'present',
          value: readStandardCrudFieldDisplayValue(fields, field.key),
        };
      }

      diagnostics.push({
        code: 'STANDARD_CRUD_DETAIL_FIELD_UNAVAILABLE',
        sectionKey: section.key,
        fieldKey: field.key,
      });
      return { ...field, state: 'unavailable' };
    });
    const unavailable = sectionFields.every((field) => field.state === 'unavailable');
    if (unavailable) {
      diagnostics.push({
        code: 'STANDARD_CRUD_DETAIL_SECTION_UNAVAILABLE',
        sectionKey: section.key,
      });
    }

    return {
      key: section.key,
      titleKey: section.titleKey,
      state: unavailable ? 'unavailable' : 'available',
      fields: sectionFields,
    };
  });

  const pageActions = schema.pageActions.map<StandardCrudDetailActionPresentation>((action) => {
    const missingField = action.dataDependency?.fields.find(
      (fieldKey) => !isAvailableProperty(fields, fieldKey) || fields[fieldKey] === null,
    );
    if (missingField) {
      diagnostics.push({
        code: 'STANDARD_CRUD_DETAIL_ACTION_DEPENDENCY_UNAVAILABLE',
        actionKey: action.key,
        fieldKey: missingField,
      });
    }

    return {
      ...action,
      disabled: missingField !== undefined,
      disabledReasonKey: missingField ? action.dataDependency?.reasonKey : undefined,
    };
  });

  return { kind: 'ready', sourceFields: fields, sections, pageActions, diagnostics };
}

/** Records stable, value-free contract diagnostics with enough schema context to locate the failing surface. */
export function reportStandardCrudDetailDiagnostics(
  schema: StandardCrudResolvedDetailSchema,
  diagnostics: StandardCrudDetailDiagnostic[],
): void {
  for (const diagnostic of diagnostics) {
    console.warn('[STANDARD_CRUD_DETAIL_CONTRACT]', {
      resourceKey: schema.resourceKey,
      viewKey: schema.viewKey,
      ...diagnostic,
    });
  }
}
