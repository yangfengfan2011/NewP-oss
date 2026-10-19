import { useLocalization } from '@icp-platform/core';
import {
  type StandardCrudFieldDefinition,
  type StandardCrudFormSection,
  type StandardCrudLookupProvider,
  type StandardCrudOperationResult,
  type ActionDataAccessRequirement,
} from '@icp-platform/sdk';
import AntdButton from 'antd/es/button';
import { FormPage, StandardCrudForm } from '@icp-platform/ui/crud-form';
import { buildStandardCrudDetailFormValues } from '../standard-crud/standardCrudDetailFormValues';
import { useStandardCrudLookupRuntime } from '../standard-crud/standardCrudLookupRuntime';
import { useStandardCrudEditSession } from '../standard-crud/useStandardCrudEditSession';
import type { StandardCrudDetailReloadResult } from '../standard-crud/standardCrudDetailQuery';
import {
  type StandardCrudMutationRegistration,
  useStandardCrudMutationExecution,
} from '../standard-crud/useStandardCrudMutationExecution';

interface StandardCrudDetailEditorProps {
  id: string;
  resourceKey: string;
  featurePosition?: string;
  dataAccess?: ActionDataAccessRequirement;
  detail: { fields: Record<string, unknown> };
  reading?: boolean;
  fields: StandardCrudFieldDefinition[];
  sections: StandardCrudFormSection[];
  registration: StandardCrudMutationRegistration & {
    lookupProviders?: StandardCrudLookupProvider[];
  };
  onSaved: () => void;
  onRefresh: () => void;
  onReload: () => Promise<StandardCrudDetailReloadResult>;
  onResult: (result: StandardCrudOperationResult) => void;
  titleForSection: (titleKey: string) => string;
}

export default function StandardCrudDetailEditor({
  id,
  resourceKey,
  featurePosition,
  dataAccess,
  detail,
  reading = false,
  fields,
  sections,
  registration,
  onSaved,
  onRefresh,
  onReload,
  onResult,
  titleForSection,
}: StandardCrudDetailEditorProps) {
  const { t } = useLocalization();
  const editSession = useStandardCrudEditSession({
    identity: JSON.stringify([resourceKey, id, dataAccess?.id]), snapshot: detail.fields, fields,
    buildValues: buildStandardCrudDetailFormValues, reload: onReload,
  });
  const formValues = editSession.draft?.values ?? {};
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

  const handleSave = async () => {
    if (reading || editSession.reloading || !editSession.draft) return;
    const succeeded = await mutationExecution.execute({
      mode: 'edit',
      featurePosition,
      dataAccess,
      id,
      fields,
      ...editSession.draft,
    });
    if (succeeded) {
      onSaved();
    }
  };

  if (!editSession.draft) return null;
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
              void editSession.reload();
            }}
            disabled={mutationExecution.pending || reading || editSession.reloading}
          >
            {t('Platform.ReloadData', '重新加载当前数据')}
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
          editSession.change(fieldKey, value);
          if (mutationExecution.fieldErrors[fieldKey] || mutationExecution.conflict) {
            mutationExecution.clearError();
          }
        }}
        onSubmit={handleSave}
        saving={mutationExecution.pending || reading || editSession.reloading}
        error={mutationExecution.error}
        titleForSection={titleForSection}
      />
    </FormPage>
  );
}
