import { Component, useEffect, useMemo, useState, type ErrorInfo, type ReactElement, type ReactNode } from 'react';
import { platformFetch, useAuthorization, useLocalization } from '@icp-platform/core';
import {
  ModuleRegistry,
  applyStandardCrudLookupValueChange,
  createStandardCrudClient,
  normalizeStandardCrudDetail,
  normalizeStandardCrudSubmitValues,
  useStandardCrudLookupRuntime,
  type StandardCrudDetail,
  type StandardCrudFieldDefinition,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';
import { ErrorState, LoadingState, PageHeader } from '@icp-platform/ui/core';
import { DetailPage, FormPage, StandardCrudForm } from '@icp-platform/ui/crud';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { buildStandardCrudDetailSchema } from '../metadata/standardCrudSchemaResolver';
import { resolveDetailViewDocument } from '../metadata/viewMetadataResolver';
import { buildStandardCrudRegistry } from '../registries/standardCrudRegistry';
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';
import { buildStandardCrudFormSchemaFromModel } from '../standard-crud/standardCrudMetadataForm';
import {
  buildStandardCrudDetailColumns,
  buildStandardCrudDetailFormValues,
  buildStandardCrudDetailPreviewData,
} from '../standard-crud/standardCrudDetailView';
import { StandardCrudSummaryBlocks } from '../standard-crud/standardCrudSummaryRuntime';

type MetadataRecord = Record<string, unknown>;

interface StandardCrudDetailPageHostProps {
  viewKey: string;
  id: string | null;
}

const EMPTY_DETAIL_FIELDS: Record<string, unknown> = {};

class StandardCrudHostErrorBoundary extends Component<{ children: ReactNode; message: string }, { error: string | null }> {
  state: { error: string | null } = { error: null };

  static getDerivedStateFromError(error: unknown): { error: string } {
    return { error: error instanceof Error ? error.message : '标准 CRUD 元数据解析失败' };
  }

  componentDidCatch(error: unknown, info: ErrorInfo): void {
    console.error(this.props.message, error, info);
  }

  render(): ReactNode {
    if (this.state.error) {
      return <ErrorState message={`${this.props.message}：${this.state.error}`} />;
    }

    return this.props.children;
  }
}

type LoadedDetail<TDetail> = {
  id: string;
  normalized: StandardCrudDetail;
  value: TDetail | StandardCrudDetail;
};

function findStandardCrudModelDocument(modelDocuments: MetadataRecord[], modelKey: unknown): MetadataRecord {
  if (typeof modelKey !== 'string' || modelKey.trim().length === 0) {
    return { fields: [] };
  }

  const document = modelDocuments.find((item) => item?.type === 'model' && Object.prototype.hasOwnProperty.call(item, modelKey));
  const model = document?.[modelKey];
  return typeof model === 'object' && model !== null && !Array.isArray(model) ? (model as MetadataRecord) : { fields: [] };
}

function localizeStandardCrudFields(
  fields: StandardCrudFieldDefinition[],
  dicKey: string,
  t: (key: string, fallback?: string) => string,
): StandardCrudFieldDefinition[] {
  return fields.map((field) => {
    const placeholder = t(`${dicKey}.${field.labelKey}_Placeholder`, '');

    return {
      ...field,
      labelKey: t(`${dicKey}.${field.labelKey}`, field.labelKey),
      placeholder: placeholder.length > 0 ? placeholder : field.placeholder,
    };
  });
}

function toDictionaryBlockKey(blockKey: string): string {
  if (blockKey.length === 0) {
    return blockKey;
  }

  return `Block_${blockKey.charAt(0).toUpperCase()}${blockKey.slice(1)}`;
}

function useOptionalGrantedPermissions(fallbackPermissions: string[] = []): string[] {
  try {
    return useAuthorization().permissions;
  } catch {
    return fallbackPermissions;
  }
}

export default function StandardCrudDetailPageHost({ viewKey, id }: StandardCrudDetailPageHostProps): ReactElement {
  const runtimeMetadata = useRuntimeMetadata();
  const viewDocument = resolveDetailViewDocument(runtimeMetadata?.viewDocuments ?? [], viewKey);
  const registration = buildStandardCrudRegistry(ModuleRegistry.getAllModules()).findByDetailViewKey(viewKey);

  if (!id) {
    return <ErrorState message="当前地址中没有有效的资源标识。" />;
  }

  if (!viewDocument) {
    return <div className="sh-error-state">未找到详情视图元数据</div>;
  }

  if (!registration) {
    return <div className="sh-error-state">未找到标准 CRUD 前端资源注册项</div>;
  }

  return (
    <StandardCrudHostErrorBoundary message={`标准 CRUD 详情元数据解析失败（${viewKey}）`}>
    <StandardCrudDetailPageContent
      id={id}
      view={viewDocument.view as Record<string, unknown>}
      registration={registration}
      modelDocuments={runtimeMetadata?.modelDocuments ?? []}
    />
    </StandardCrudHostErrorBoundary>
  );
}

function StandardCrudDetailPageContent<TDetail>({
  id,
  view,
  registration,
  modelDocuments,
}: {
  id: string;
  view: Record<string, unknown>;
  registration: StandardCrudResourceRegistration<Record<string, string>, Record<string, unknown>, TDetail>;
  modelDocuments: MetadataRecord[];
}): ReactElement {
  const { t } = useLocalization();
  const grantedPermissions = useOptionalGrantedPermissions(registration.permissions);
  const schema = useMemo(() => buildStandardCrudDetailSchema(view, registration, {
    modelDocuments,
  }), [view, registration, modelDocuments]);
  const client = useMemo(() => createStandardCrudClient(schema.apiRoute, platformFetch), [schema.apiRoute]);
  const modelDocument = useMemo(() => findStandardCrudModelDocument(modelDocuments, view.model), [modelDocuments, view]);
  const formSchema = useMemo(() => buildStandardCrudFormSchemaFromModel(modelDocument, {
    mode: 'edit',
    enumOptions: registration.enumOptions ?? {},
    lookupProviders: registration.lookupProviders ?? [],
  }), [modelDocument, registration.enumOptions, registration.lookupProviders]);
  const editFields = useMemo(
    () => localizeStandardCrudFields(formSchema.fields, schema.dicKey, t),
    [formSchema.fields, schema.dicKey, t],
  );
  const [loadedDetail, setLoadedDetail] = useState<LoadedDetail<TDetail> | null>(null);
  const [formValues, setFormValues] = useState<Record<string, unknown>>({});
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadTick, setReloadTick] = useState(0);

  useEffect(() => {
    let cancelled = false;

    async function loadDetail() {
      try {
        setLoading(true);
        setError(null);
        const rawDetail = await client.getDetail(id);
        const normalized = normalizeStandardCrudDetail(rawDetail);
        const mapped = registration.mapDetail ? registration.mapDetail(normalized) : normalized;
        const values = buildStandardCrudDetailFormValues(normalized.fields, editFields);

        if (!cancelled) {
          setLoadedDetail({ id, normalized, value: mapped });
          setFormValues(values);
          setSaveError(null);
        }
      } catch (loadError) {
        if (!cancelled) {
          setLoadedDetail(null);
          setError(loadError instanceof Error ? loadError.message : '加载标准 CRUD 详情失败');
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void loadDetail();

    return () => {
      cancelled = true;
    };
  }, [client, editFields, id, registration, reloadTick]);

  const currentDetail = loadedDetail?.id === id ? loadedDetail.value : null;
  const currentNormalizedDetail = loadedDetail?.id === id ? loadedDetail.normalized : null;
  const detailPreviewData = useMemo(
    () => (currentNormalizedDetail ? buildStandardCrudDetailPreviewData(currentNormalizedDetail.fields, editFields) : {}),
    [currentNormalizedDetail, editFields],
  );
  const detailColumns = useMemo(
    () => (currentNormalizedDetail ? buildStandardCrudDetailColumns(currentNormalizedDetail.fields, editFields) : []),
    [currentNormalizedDetail, editFields],
  );
  const { lookupOptions, lookupLoading, lookupErrors } = useStandardCrudLookupRuntime({
    fields: editFields,
    providers: currentNormalizedDetail ? registration.lookupProviders : [],
    values: formValues,
    detailFields: currentNormalizedDetail?.fields ?? EMPTY_DETAIL_FIELDS,
  });

  const refresh = () => setReloadTick((current) => current + 1);
  const detailActionHandlers = useMemo(() => ({
    ...registration.actionHandlers,
    edit: () => undefined,
  }), [registration.actionHandlers]);
  const handleFormChange = (fieldKey: string, value: unknown) => {
    setFormValues((current) => applyStandardCrudLookupValueChange(current, fieldKey, value, editFields));
  };
  const handleSave = async (fields: StandardCrudFieldDefinition[]) => {
    try {
      setSaving(true);
      setSaveError(null);
      const values = registration.buildMutationValues
        ? registration.buildMutationValues(formValues)
        : normalizeStandardCrudSubmitValues(fields, formValues);
      await client.update(id, values);
      refresh();
    } catch (saveFailure) {
      setSaveError(saveFailure instanceof Error ? saveFailure.message : '保存标准资源失败');
    } finally {
      setSaving(false);
    }
  };
  const showLoading = loading || (loadedDetail !== null && loadedDetail.id !== id);
  const nonReadySummaryBlocks = currentNormalizedDetail
    ? schema.summaryBlocks.filter((blockKey) => currentNormalizedDetail.summaries[blockKey]?.status !== 'ready')
    : [];
  const detailHeaderActions = !showLoading && currentDetail
    ? registration.renderDetailHeaderActions
      ? registration.renderDetailHeaderActions({ id, detail: currentDetail as TDetail, refresh, loading: showLoading })
      : schema.pageActions.length > 0
        ? (
          <StandardCrudActions
            actions={schema.pageActions.map((action) => ({
              ...action,
              labelKey: t(`${schema.dicKey}.${action.labelKey}`, action.labelKey),
              confirmMessage: action.confirmMessageKey
                ? t(`${schema.dicKey}.${action.confirmMessageKey}`, action.confirmMessageKey)
                : undefined,
            }))}
            grantedPermissions={grantedPermissions}
            handlers={detailActionHandlers}
            context={{ id, detail: currentDetail as TDetail, refresh }}
          />
        )
        : undefined
    : undefined;

  return (
    <div className="sh-page">
      <PageHeader
        eyebrow={schema.resourceKey}
        title={t(`${schema.dicKey}.${schema.titleKey}`, schema.titleKey)}
        description={t(`${schema.dicKey}.${schema.descriptionKey}`, schema.descriptionKey)}
        actions={detailHeaderActions}
      />

      {showLoading ? (
        <LoadingState message="正在加载详情..." />
      ) : error ? (
        <ErrorState message={error} />
      ) : currentDetail && registration.renderDetail ? (
        <div className="sh-page-stack">
          <DetailPage
            title="详情信息"
            dataSource={detailPreviewData}
            columns={detailColumns}
          />
          <StandardCrudSummaryBlocks
            blocks={nonReadySummaryBlocks}
            summaries={currentNormalizedDetail?.summaries ?? {}}
            titleForBlock={(key) => t(`${schema.dicKey}.${toDictionaryBlockKey(key)}`, key)}
          />
          {registration.renderDetail({ id, detail: currentDetail as TDetail, refresh, loading: showLoading })}
        </div>
      ) : currentNormalizedDetail && editFields.length > 0 ? (
        <div className="sh-page-stack">
          <StandardCrudSummaryBlocks
            blocks={schema.summaryBlocks}
            summaries={currentNormalizedDetail.summaries}
            renderers={registration.summaryRenderers}
            titleForBlock={(key) => t(`${schema.dicKey}.${toDictionaryBlockKey(key)}`, key)}
          />
          <DetailPage
            title={t(`${schema.dicKey}.Detail_Title`, '详情信息')}
            dataSource={detailPreviewData}
            columns={detailColumns}
          />
          <FormPage<Record<string, unknown>>
            component="div"
            submitter={false}
            initialValues={formValues}
            onFinish={() => handleSave(editFields)}
          >
            <StandardCrudForm
              formId={`${schema.resourceKey}-detail-form`}
              mode="edit"
              fields={editFields}
              sections={formSchema.sections}
              values={formValues}
              lookupOptions={lookupOptions}
              lookupLoading={lookupLoading}
              lookupErrors={lookupErrors}
              onChange={handleFormChange}
              onSubmit={() => handleSave(editFields)}
              saving={saving}
              error={saveError}
              titleForSection={(titleKey) => t(`${schema.dicKey}.${titleKey}`, titleKey)}
            />
          </FormPage>
        </div>
      ) : currentNormalizedDetail ? (
        <div className="sh-page-stack">
          <StandardCrudSummaryBlocks
            blocks={schema.summaryBlocks}
            summaries={currentNormalizedDetail.summaries}
            renderers={registration.summaryRenderers}
            titleForBlock={(key) => t(`${schema.dicKey}.${toDictionaryBlockKey(key)}`, key)}
          />
          <DetailPage
            title={t(`${schema.dicKey}.Detail_Title`, '详情信息')}
            dataSource={detailPreviewData}
            columns={detailColumns}
          />
        </div>
      ) : (
        <ErrorState message="未找到详情。" />
      )}
    </div>
  );
}
