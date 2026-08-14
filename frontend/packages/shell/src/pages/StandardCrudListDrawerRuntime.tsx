import type { ReactNode } from 'react';
import type {
  StandardCrudFieldDefinition,
  StandardCrudFormSection,
  StandardCrudOption,
} from '@icp-platform/sdk';
import { CrudFormDrawer, StandardCrudForm } from '@icp-platform/ui/crud-form';

export interface StandardCrudListDrawerRuntimeProps {
  mode: 'create' | 'view' | 'edit';
  viewTitle: ReactNode;
  mutationTitle: ReactNode;
  detailLoading: boolean;
  detailError: string | null;
  saving: boolean;
  saveError: string | null;
  saveFieldErrors: Record<string, string>;
  formId: string;
  fields: StandardCrudFieldDefinition[];
  sections: StandardCrudFormSection[];
  values: Record<string, unknown>;
  lookupOptions: Record<string, StandardCrudOption[]>;
  lookupLoading: Record<string, boolean>;
  lookupErrors: Record<string, string | null>;
  canEdit: boolean;
  viewSecondaryAction?: ReactNode;
  saveErrorActions?: ReactNode;
  onChange: (fieldKey: string, value: unknown) => void;
  onEdit: () => void;
  onSubmit: () => void;
  onClose: () => void;
  afterOpenChange: (open: boolean) => void;
  titleForSection: (titleKey: string) => string;
}

export default function StandardCrudListDrawerRuntime({
  mode,
  viewTitle,
  mutationTitle,
  detailLoading,
  detailError,
  saving,
  saveError,
  saveFieldErrors,
  formId,
  fields,
  sections,
  values,
  lookupOptions,
  lookupLoading,
  lookupErrors,
  canEdit,
  viewSecondaryAction,
  saveErrorActions,
  onChange,
  onEdit,
  onSubmit,
  onClose,
  afterOpenChange,
  titleForSection,
}: StandardCrudListDrawerRuntimeProps) {
  if (mode === 'view') {
    return (
      <CrudFormDrawer
        open
        title={viewTitle}
        loading={detailLoading}
        error={detailError}
        primaryLabel="编辑"
        secondaryAction={viewSecondaryAction}
        hidePrimary={!canEdit}
        primaryAction={onEdit}
        onClose={onClose}
        afterOpenChange={afterOpenChange}
      >
        {!detailError ? (
          <StandardCrudForm
            formId={`${formId}-view`}
            mode="view"
            fields={fields}
            sections={sections}
            values={values}
            lookupOptions={lookupOptions}
            lookupLoading={lookupLoading}
            lookupErrors={lookupErrors}
            onChange={onChange}
            onSubmit={() => undefined}
            saving={false}
            showSubmitAction={false}
            titleForSection={titleForSection}
          />
        ) : null}
      </CrudFormDrawer>
    );
  }

  return (
    <CrudFormDrawer
      open
      title={mutationTitle}
      saving={saving}
      error={saveError}
      errorActions={saveErrorActions}
      submitFormId={formId}
      primaryLabel={mode === 'create' ? '保存并关闭' : '保存修改'}
      onClose={onClose}
      afterOpenChange={afterOpenChange}
    >
      <StandardCrudForm
        formId={formId}
        mode={mode}
        fields={fields}
        sections={sections}
        values={values}
        lookupOptions={lookupOptions}
        lookupLoading={lookupLoading}
        lookupErrors={lookupErrors}
        fieldErrors={saveFieldErrors}
        onChange={onChange}
        onSubmit={onSubmit}
        saving={saving}
        error={null}
        showSubmitAction={false}
        titleForSection={titleForSection}
      />
    </CrudFormDrawer>
  );
}
