import { useMemo, useState, type ReactElement } from 'react';
import type {
  StandardCrudFieldDefinition,
  StandardCrudFormMode,
  StandardCrudOption,
  StandardCrudFormSection,
} from '@icp-platform/sdk';
import { Button, Form } from 'antd';
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
  fieldErrors?: Record<string, string>;
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

function resolveSections(fields: StandardCrudFieldDefinition[], sections: StandardCrudFormSection[]): ResolvedSection[] {
  if (sections.length === 0) {
    return [{ key: 'default', titleKey: '基础信息', fields }];
  }
  const fieldByKey = new Map(fields.map((field) => [field.key, field]));
  return sections.map((section) => ({
    key: section.key,
    titleKey: section.titleKey,
    fields: section.fields.map((fieldKey) => fieldByKey.get(fieldKey)).filter((field): field is StandardCrudFieldDefinition => Boolean(field)),
  }));
}

function isEmptyRequiredValue(field: StandardCrudFieldDefinition, value: unknown): boolean {
  if (field.kind === 'dateRange') {
    return !Array.isArray(value) || value.length !== 2 || value.some((item) => typeof item !== 'string' || item.trim() === '');
  }
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
  fieldErrors = {},
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

  async function handleFinish() {
    if (saving) {
      return;
    }
    const nextErrors = fields.reduce<Record<string, string>>((current, field) => {
      if (field.required && field.kind !== 'readonly' && field.readOnly !== true && isEmptyRequiredValue(field, values[field.key])) {
        current[field.key] = '请填写此字段';
      }
      return current;
    }, {});
    setValidationErrors(nextErrors);
    if (Object.keys(nextErrors).length === 0) {
      await onSubmit();
    }
  }

  function handleFieldChange(fieldKey: string, value: unknown) {
    setValidationErrors((current) => {
      if (!current[fieldKey]) return current;
      const { [fieldKey]: _removed, ...rest } = current;
      return rest;
    });
    onChange(fieldKey, value);
  }

  return (
    <Form id={formId} className={`np-standard-crud-form np-standard-crud-form-${mode}`} layout="vertical" onFinish={() => void handleFinish()}>
      {error ? <ErrorState message={error} /> : null}
      {resolvedSections.map((section, index) => {
        const actions = showSubmitAction && index === 0 ? (
          <Button size="small" type="primary" htmlType="submit" form={formId} loading={saving} disabled={saving}>
            {saveLabel ?? (mode === 'create' ? '保存并关闭' : '保存修改')}
          </Button>
        ) : undefined;
        return (
          <Panel key={section.key} title={titleForSection(section.titleKey) || undefined} actions={actions}>
            <div className="sh-section-grid sh-section-grid-2">
              {section.fields.map((field) => (
                <StandardCrudField
                  key={field.key}
                  field={field}
                  value={values[field.key]}
                  mode={mode}
                  options={lookupOptions[field.key]}
                  loading={lookupLoading[field.key]}
                  error={validationErrors[field.key] ?? fieldErrors[field.key] ?? lookupErrors[field.key]}
                  onChange={handleFieldChange}
                />
              ))}
            </div>
          </Panel>
        );
      })}
    </Form>
  );
}
