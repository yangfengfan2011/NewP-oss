import type { FormEvent, ReactElement } from 'react';
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
  titleForSection,
}: StandardCrudFormProps): ReactElement {
  const resolvedSections = resolveSections(fields, sections);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (mode === 'view') {
      return;
    }

    await onSubmit();
  }

  return (
    <form id={formId} onSubmit={(event) => void handleSubmit(event)} style={{ marginTop: 8 }}>
      {error ? <ErrorState message={error} /> : null}

      {resolvedSections.map((section, index) => {
        const title = titleForSection(section.titleKey);
        const actions = index === 0 && mode !== 'view'
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
                  error={lookupErrors[field.key]}
                  onChange={onChange}
                />
              ))}
            </div>
          </Panel>
        );
      })}
    </form>
  );
}
