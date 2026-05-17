import { useEffect, useMemo, useState, type ReactElement } from 'react';
import { useLocalization } from '@icp-platform/core';
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
import { DetailPage, ErrorState, FormPage, LoadingState, PageHeader, StandardCrudForm } from '@icp-platform/ui';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { buildStandardCrudDetailSchema } from '../metadata/standardCrudSchemaResolver';
import { resolveDetailViewDocument } from '../metadata/viewMetadataResolver';
import { buildStandardCrudRegistry } from '../registries/standardCrudRegistry';
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';
import {
  buildStandardCrudDetailColumns,
  buildStandardCrudDetailFormValues,
  buildStandardCrudDetailPreviewData,
} from '../standard-crud/standardCrudDetailView';
import { StandardCrudSummaryBlocks } from '../standard-crud/standardCrudSummaryRuntime';

interface StandardCrudDetailPageHostProps {
  viewKey: string;
  id: string | null;
}

const EMPTY_DETAIL_FIELDS: Record<string, unknown> = {};

type LoadedDetail<TDetail> = {
  id: string;
  normalized: StandardCrudDetail;
  value: TDetail | StandardCrudDetail;
};

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

  return <StandardCrudDetailPageContent id={id} view={viewDocument.view as Record<string, unknown>} registration={registration} />;
}

function StandardCrudDetailPageContent<TDetail>({
  id,
  view,
  registration,
}: {
  id: string;
  view: Record<string, unknown>;
  registration: StandardCrudResourceRegistration<Record<string, string>, Record<string, unknown>, TDetail>;
}): ReactElement {
  const { t } = useLocalization();
  const schema = useMemo(() => buildStandardCrudDetailSchema(view, registration), [view, registration]);
  const client = useMemo(() => createStandardCrudClient(schema.apiRoute), [schema.apiRoute]);
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
        const values = buildStandardCrudDetailFormValues(normalized.fields, registration.fields ?? []);

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
  }, [client, id, registration, reloadTick]);

  const currentDetail = loadedDetail?.id === id ? loadedDetail.value : null;
  const currentNormalizedDetail = loadedDetail?.id === id ? loadedDetail.normalized : null;
  const editFields = registration.fields ?? [];
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
      : registration.detailActions
        ? (
          <StandardCrudActions
            actions={registration.detailActions}
            grantedPermissions={registration.permissions}
            handlers={registration.actionHandlers}
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
            titleForBlock={(key) => key}
          />
          {registration.renderDetail({ id, detail: currentDetail as TDetail, refresh, loading: showLoading })}
        </div>
      ) : currentNormalizedDetail && editFields.length > 0 ? (
        <div className="sh-page-stack">
          <StandardCrudSummaryBlocks
            blocks={schema.summaryBlocks}
            summaries={currentNormalizedDetail.summaries}
            renderers={registration.summaryRenderers}
            titleForBlock={(key) => key}
          />
          <DetailPage
            title="详情信息"
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
              sections={registration.formSections ?? []}
              values={formValues}
              lookupOptions={lookupOptions}
              lookupLoading={lookupLoading}
              lookupErrors={lookupErrors}
              onChange={handleFormChange}
              onSubmit={() => handleSave(editFields)}
              saving={saving}
              error={saveError}
              titleForSection={(titleKey) => titleKey}
            />
          </FormPage>
        </div>
      ) : currentNormalizedDetail ? (
        <div className="sh-page-stack">
          <StandardCrudSummaryBlocks
            blocks={schema.summaryBlocks}
            summaries={currentNormalizedDetail.summaries}
            renderers={registration.summaryRenderers}
            titleForBlock={(key) => key}
          />
          <DetailPage
            title="详情信息"
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
