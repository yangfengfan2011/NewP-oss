import { useMemo, useState, type FormEvent, type ReactElement } from 'react';
import type {
  StandardCrudFieldDefinition,
  StandardCrudFormMode,
  StandardCrudOption,
  StandardCrudFormSection,
} from '@icp-platform/sdk';
import { Button } from '../components/Button';
import { ErrorState } from '../components/ErrorState';
import { Panel } from './Panel';
import { StandardCrudField } from './StandardCrudField';

export interface StandardCrudFormProps {
  formId: string;
  mode: StandardCrudFormMode;
  fields: StandardCrudFieldDefinition[];
  sections: StandardCrudFormSection[];
  values: Record<string, unknown>;
  lookupOptions?: Record<string, StandardCrudOption[]>;
  lookupLoading?: Record<string, boolean>;
  lookupErrors?: Record<string, string | null>;
  onChange: (fieldKey: string, value: unknown) => void;
  onSubmit: () => void | Promise<void>;
  saving: boolean;
  error?: string | null;
  saveLabel?: string;
  showSubmitAction?: boolean;
  titleForSection: (titleKey: string) => string;
}

interface ResolvedSection {
  key: string;
  titleKey: string;
  fields: StandardCrudFieldDefinition[];
}

function resolveSections(
  fields: StandardCrudFieldDefinition[],
  sections: StandardCrudFormSection[],
): ResolvedSection[] {
  if (sections.length === 0) {
    return [{ key: 'default', titleKey: '基础信息', fields }];
  }

  const fieldByKey = new Map(fields.map((field) => [field.key, field]));
  return sections.map((section) => ({
    key: section.key,
    titleKey: section.titleKey,
    fields: section.fields
      .map((fieldKey) => fieldByKey.get(fieldKey))
      .filter((field): field is StandardCrudFieldDefinition => Boolean(field)),
  }));
}

function resolveSubmitText(mode: StandardCrudFormMode, saving?: boolean): string {
  if (saving) {
    return '保存中...';
  }

  return mode === 'create' ? '保存并关闭' : '保存修改';
}

function isEmptyRequiredValue(value: unknown): boolean {
  return value === null || value === undefined || (typeof value === 'string' && value.trim() === '');
}

export function StandardCrudForm({
  formId,
  mode,
  fields,
  sections,
  values,
  lookupOptions = {},
  lookupLoading = {},
  lookupErrors = {},
  onChange,
  onSubmit,
  saving,
  error = null,
  saveLabel,
  showSubmitAction = true,
  titleForSection,
}: StandardCrudFormProps): ReactElement {
  const resolvedSections = useMemo(() => resolveSections(fields, sections), [fields, sections]);
  const [validationErrors, setValidationErrors] = useState<Record<string, string>>({});

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (mode === 'view') {
      return;
    }

    const nextErrors = fields.reduce<Record<string, string>>((current, field) => {
      if (field.required && field.kind !== 'readonly' && field.readOnly !== true && isEmptyRequiredValue(values[field.key])) {
        current[field.key] = '请填写此字段';
      }

      return current;
    }, {});

    setValidationErrors(nextErrors);

    if (Object.keys(nextErrors).length > 0) {
      return;
    }

    await onSubmit();
  }

  function handleFieldChange(fieldKey: string, value: unknown) {
    setValidationErrors((current) => {
      if (!current[fieldKey]) {
        return current;
      }

      const { [fieldKey]: _removed, ...rest } = current;
      return rest;
    });
    onChange(fieldKey, value);
  }

  return (
    <form
      id={formId}
      className={`np-standard-crud-form np-standard-crud-form-${mode}`}
      noValidate
      onSubmit={(event) => void handleSubmit(event)}
    >
      {error ? <ErrorState message={error} /> : null}

      {resolvedSections.map((section, index) => {
        const title = titleForSection(section.titleKey);
        const actions = showSubmitAction && index === 0 && mode !== 'view'
          ? (
            <Button size="sm" type="submit" form={formId} variant="primary" disabled={saving}>
              {saving ? '保存中...' : (saveLabel ?? resolveSubmitText(mode, false))}
            </Button>
          )
          : undefined;

        return (
          <Panel key={section.key} title={title || undefined} actions={actions}>
            <div className="sh-section-grid sh-section-grid-2">
              {section.fields.map((field) => (
                <StandardCrudField
                  key={field.key}
                  field={field}
                  value={values[field.key]}
                  mode={mode}
                  options={lookupOptions[field.key]}
                  loading={lookupLoading[field.key]}
                  error={validationErrors[field.key] ?? lookupErrors[field.key]}
                  onChange={handleFieldChange}
                />
              ))}
            </div>
          </Panel>
        );
      })}
    </form>
  );
}
