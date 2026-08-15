import { useState } from 'react';
import {
  applyStandardCrudLookupValueChange,
  normalizeStandardCrudSubmitValues,
  useStandardCrudLookupRuntime,
  type StandardCrudDetail,
  type StandardCrudFieldDefinition,
  type StandardCrudFormSection,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';
import { FormPage, StandardCrudForm } from '@icp-platform/ui/crud-form';
import { buildStandardCrudDetailFormValues } from '../standard-crud/standardCrudDetailFormValues';

interface StandardCrudDetailEditorProps {
  id: string;
  resourceKey: string;
  detail: StandardCrudDetail;
  fields: StandardCrudFieldDefinition[];
  sections: StandardCrudFormSection[];
  registration: Pick<
    StandardCrudResourceRegistration,
    'lookupProviders' | 'buildMutationValues' | 'concurrencyRevisionField'
  >;
  onSave: (values: Record<string, unknown>) => Promise<void>;
  onSaved: () => void;
  titleForSection: (titleKey: string) => string;
}

export default function StandardCrudDetailEditor({
  id,
  resourceKey,
  detail,
  fields,
  sections,
  registration,
  onSave,
  onSaved,
  titleForSection,
}: StandardCrudDetailEditorProps) {
  const [formValues, setFormValues] = useState<Record<string, unknown>>(
    () => buildStandardCrudDetailFormValues(detail.fields, fields),
  );
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const { lookupOptions, lookupLoading, lookupErrors } = useStandardCrudLookupRuntime({
    fields,
    providers: registration.lookupProviders ?? [],
    values: formValues,
    detailFields: detail.fields,
  });

  const handleSave = async () => {
    try {
      setSaving(true);
      setSaveError(null);
      const normalizedValues = registration.buildMutationValues
        ? registration.buildMutationValues(formValues)
        : normalizeStandardCrudSubmitValues(fields, formValues);
      const revisionField = registration.concurrencyRevisionField;
      const values = revisionField
        && Object.prototype.hasOwnProperty.call(detail.fields, revisionField)
        && detail.fields[revisionField] !== undefined
        ? { ...normalizedValues, [revisionField]: detail.fields[revisionField] }
        : normalizedValues;
      await onSave(values);
      onSaved();
    } catch (error) {
      setSaveError(error instanceof Error ? error.message : '保存标准资源失败');
    } finally {
      setSaving(false);
    }
  };

  return (
    <FormPage<Record<string, unknown>>
      component="div"
      submitter={false}
      initialValues={formValues}
      onFinish={handleSave}
    >
      <StandardCrudForm
        formId={`${resourceKey}-${id}-detail-form`}
        mode="edit"
        fields={fields}
        sections={sections}
        values={formValues}
        lookupOptions={lookupOptions}
        lookupLoading={lookupLoading}
        lookupErrors={lookupErrors}
        onChange={(fieldKey, value) => setFormValues((current) => (
          applyStandardCrudLookupValueChange(current, fieldKey, value, fields)
        ))}
        onSubmit={handleSave}
        saving={saving}
        error={saveError}
        titleForSection={titleForSection}
      />
    </FormPage>
  );
}
