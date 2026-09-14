import { useEffect, useState } from 'react';
import {
  applyStandardCrudLookupValueChange,
  type StandardCrudFieldDefinition,
  type StandardCrudFormSection,
  type StandardCrudLookupProvider,
  type StandardCrudOperationResult,
} from '@icp-platform/sdk';
import AntdButton from 'antd/es/button';
import { FormPage, StandardCrudForm } from '@icp-platform/ui/crud-form';
import { buildStandardCrudDetailFormValues } from '../standard-crud/standardCrudDetailFormValues';
import { useStandardCrudLookupRuntime } from '../standard-crud/standardCrudLookupRuntime';
import {
  type StandardCrudMutationRegistration,
  useStandardCrudMutationExecution,
} from '../standard-crud/useStandardCrudMutationExecution';

interface StandardCrudDetailEditorProps {
  id: string;
  resourceKey: string;
  featurePosition?: string;
  detail: { fields: Record<string, unknown> };
  fields: StandardCrudFieldDefinition[];
  sections: StandardCrudFormSection[];
  registration: StandardCrudMutationRegistration & {
    lookupProviders?: StandardCrudLookupProvider[];
  };
  onSaved: () => void;
  onRefresh: () => void;
  onResult: (result: StandardCrudOperationResult) => void;
  titleForSection: (titleKey: string) => string;
}

export default function StandardCrudDetailEditor({
  id,
  resourceKey,
  featurePosition,
  detail,
  fields,
  sections,
  registration,
  onSaved,
  onRefresh,
  onResult,
  titleForSection,
}: StandardCrudDetailEditorProps) {
  const [formValues, setFormValues] = useState<Record<string, unknown>>(
    () => buildStandardCrudDetailFormValues(detail.fields, fields),
  );
  const lookupRuntime = useStandardCrudLookupRuntime({
    resourceKey,
  featurePosition,
    fields,
    providers: registration.lookupProviders ?? [],
    values: formValues,
    detailFields: detail.fields,
  });
  const mutationExecution = useStandardCrudMutationExecution({
    registration,
    refresh: onRefresh,
    onResult,
  });

  useEffect(() => {
    setFormValues(buildStandardCrudDetailFormValues(detail.fields, fields));
  }, [detail, fields]);

  const handleSave = async () => {
    const succeeded = await mutationExecution.execute({
      mode: 'edit',
      featurePosition,
      id,
      fields,
      values: formValues,
      detailFields: detail.fields,
    });
    if (succeeded) {
      onSaved();
    }
  };

  return (
    <FormPage<Record<string, unknown>>
      component="div"
      submitter={false}
      initialValues={formValues}
      onFinish={handleSave}
    >
      {mutationExecution.conflict ? (
        <div style={{ marginBottom: 16 }}>
          <AntdButton
            type="default"
            onClick={() => {
              mutationExecution.clearError();
              onRefresh();
            }}
            disabled={mutationExecution.pending}
          >
            重新加载当前数据
          </AntdButton>
        </div>
      ) : null}
      <StandardCrudForm
        formId={`${resourceKey}-${id}-detail-form`}
        mode="edit"
        fields={fields}
        sections={sections}
        values={formValues}
        lookupOptions={lookupRuntime.lookupOptions}
        lookupLoading={lookupRuntime.lookupLoading}
        lookupErrors={lookupRuntime.lookupErrors}
        lookupHasMore={lookupRuntime.lookupHasMore}
        onLookupSearch={lookupRuntime.search}
        onLookupLoadMore={lookupRuntime.loadMore}
        onLookupRetry={lookupRuntime.retry}
        fieldErrors={mutationExecution.fieldErrors}
        onChange={(fieldKey, value) => {
          setFormValues((current) => applyStandardCrudLookupValueChange(current, fieldKey, value, fields));
          if (mutationExecution.fieldErrors[fieldKey] || mutationExecution.conflict) {
            mutationExecution.clearError();
          }
        }}
        onSubmit={handleSave}
        saving={mutationExecution.pending}
        error={mutationExecution.error}
        titleForSection={titleForSection}
      />
    </FormPage>
  );
}
