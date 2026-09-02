import type { ReactNode } from 'react';
import type {
  StandardCrudFieldDefinition,
  StandardCrudFormSection,
  StandardCrudOption,
  StandardCrudResourceRegistration,
} from '@icp-platform/sdk';
import { CrudFormDrawer, StandardCrudForm } from '@icp-platform/ui/crud-form';
import StandardCrudResolvedDetailSections from '../standard-crud/StandardCrudResolvedDetailSections';
import type { StandardCrudDetailPresentation } from '../standard-crud/standardCrudDetailPresentation';

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
  detailPresentation: StandardCrudDetailPresentation | null;
  registration: Pick<StandardCrudResourceRegistration, 'detailFieldRenderers' | 'enumOptions'>;
  values: Record<string, unknown>;
  lookupOptions: Record<string, StandardCrudOption[]>;
  lookupLoading: Record<string, boolean>;
  lookupErrors: Record<string, string | null>;
  lookupHasMore: Record<string, boolean>;
  canEdit: boolean;
  viewSecondaryAction?: ReactNode;
  detailErrorActions?: ReactNode;
  saveErrorActions?: ReactNode;
  onChange: (fieldKey: string, value: unknown) => void;
  onLookupSearch: (fieldKey: string, keyword: string) => void;
  onLookupLoadMore: (fieldKey: string) => void;
  onLookupRetry: (fieldKey?: string) => void;
  onEdit: () => void;
  onSubmit: () => void;
  onClose: () => void;
  afterOpenChange: (open: boolean) => void;
  titleForSection: (titleKey: string) => string;
  textForDetail: (key: string, fallback: string) => string;
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
  detailPresentation,
  registration,
  values,
  lookupOptions,
  lookupLoading,
  lookupErrors,
  lookupHasMore,
  canEdit,
  viewSecondaryAction,
  detailErrorActions,
  saveErrorActions,
  onChange,
  onLookupSearch,
  onLookupLoadMore,
  onLookupRetry,
  onEdit,
  onSubmit,
  onClose,
  afterOpenChange,
  titleForSection,
  textForDetail,
}: StandardCrudListDrawerRuntimeProps) {
  if (mode === 'view') {
    const readyPresentation = detailPresentation?.kind === 'ready' ? detailPresentation : null;
    const fatalDiagnostic = detailPresentation?.kind === 'fatal' ? detailPresentation.diagnostic : null;
    const editAction = readyPresentation?.pageActions.find(
      (action) => action.standardAction === 'edit' || action.key === 'edit',
    );
    const primaryDisabledReason = editAction?.disabledReasonKey
      ? textForDetail(editAction.disabledReasonKey, editAction.disabledReasonKey)
      : undefined;

    return (
      <CrudFormDrawer
        open
        title={viewTitle}
        loading={detailLoading}
        error={detailError ?? (fatalDiagnostic ? `详情数据契约失败：${fatalDiagnostic.code}` : null)}
        errorActions={detailError ? detailErrorActions : undefined}
        primaryLabel="编辑"
        secondaryAction={fatalDiagnostic ? undefined : viewSecondaryAction}
        hidePrimary={!canEdit || !editAction || fatalDiagnostic !== null}
        primaryDisabled={editAction?.disabled === true}
        primaryDisabledReason={primaryDisabledReason}
        primaryAction={onEdit}
        onClose={onClose}
        afterOpenChange={afterOpenChange}
      >
        {!detailError && readyPresentation ? (
          <StandardCrudResolvedDetailSections
            presentation={readyPresentation}
            registration={registration}
            textFor={textForDetail}
          />
        ) : null}
      </CrudFormDrawer>
    );
  }

  return (
    <CrudFormDrawer
      open
      title={mutationTitle}
      loading={mode === 'edit' && detailLoading}
      saving={saving}
      error={mode === 'edit' ? detailError ?? saveError : saveError}
      errorActions={mode === 'edit' && detailError ? detailErrorActions : saveErrorActions}
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
        lookupHasMore={lookupHasMore}
        fieldErrors={saveFieldErrors}
        onChange={onChange}
        onLookupSearch={onLookupSearch}
        onLookupLoadMore={onLookupLoadMore}
        onLookupRetry={onLookupRetry}
        onSubmit={onSubmit}
        saving={saving}
        error={null}
        showSubmitAction={false}
        titleForSection={titleForSection}
      />
    </CrudFormDrawer>
  );
}
