import { useEffect, useMemo, useState, type ReactElement } from 'react';
import {
  ModuleRegistry,
  createStandardCrudClient,
  normalizeStandardCrudDetail,
  type StandardCrudDetail,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';
import { ErrorState, LoadingState, PageHeader } from '@icp-platform/ui';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { buildStandardCrudDetailSchema } from '../metadata/standardCrudSchemaResolver';
import { resolveDetailViewDocument } from '../metadata/viewMetadataResolver';
import { buildStandardCrudRegistry } from '../registries/standardCrudRegistry';
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';
import { StandardCrudSummaryBlocks } from '../standard-crud/standardCrudSummaryRuntime';

interface StandardCrudDetailPageHostProps {
  viewKey: string;
  id: string | null;
}

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
  const schema = useMemo(() => buildStandardCrudDetailSchema(view, registration), [view, registration]);
  const client = useMemo(() => createStandardCrudClient(schema.apiRoute), [schema.apiRoute]);
  const [loadedDetail, setLoadedDetail] = useState<LoadedDetail<TDetail> | null>(null);
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

        if (!cancelled) {
          setLoadedDetail({ id, normalized, value: mapped });
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

  const refresh = () => setReloadTick((current) => current + 1);
  const currentDetail = loadedDetail?.id === id ? loadedDetail.value : null;
  const currentNormalizedDetail = loadedDetail?.id === id ? loadedDetail.normalized : null;
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
        title={schema.titleKey}
        description={schema.descriptionKey}
        actions={detailHeaderActions}
      />

      {showLoading ? (
        <LoadingState message="正在加载详情..." />
      ) : error ? (
        <ErrorState message={error} />
      ) : currentDetail && registration.renderDetail ? (
        <div className="sh-page-stack">
          <StandardCrudSummaryBlocks
            blocks={nonReadySummaryBlocks}
            summaries={currentNormalizedDetail?.summaries ?? {}}
            titleForBlock={(key) => key}
          />
          {registration.renderDetail({ id, detail: currentDetail as TDetail, refresh, loading: showLoading })}
        </div>
      ) : currentNormalizedDetail ? (
        <div className="sh-page-stack">
          <StandardCrudSummaryBlocks
            blocks={schema.summaryBlocks}
            summaries={currentNormalizedDetail.summaries}
            renderers={registration.summaryRenderers}
            titleForBlock={(key) => key}
          />
          <pre>{JSON.stringify(currentNormalizedDetail.fields, null, 2)}</pre>
        </div>
      ) : (
        <ErrorState message="未找到详情。" />
      )}
    </div>
  );
}
