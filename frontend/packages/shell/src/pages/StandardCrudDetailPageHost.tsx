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

interface StandardCrudDetailPageHostProps {
  viewKey: string;
  id: string | null;
}

type LoadedDetail<TDetail> = {
  id: string;
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
          setLoadedDetail({ id, value: mapped });
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
  const showLoading = loading || (loadedDetail !== null && loadedDetail.id !== id);

  return (
    <div className="sh-page">
      <PageHeader
        eyebrow={schema.resourceKey}
        title={schema.titleKey}
        description={schema.descriptionKey}
        actions={!showLoading && currentDetail && registration.renderDetailHeaderActions
          ? registration.renderDetailHeaderActions({ id, detail: currentDetail as TDetail, refresh, loading: showLoading })
          : undefined}
      />

      {showLoading ? (
        <LoadingState message="正在加载详情..." />
      ) : error ? (
        <ErrorState message={error} />
      ) : currentDetail && registration.renderDetail ? (
        registration.renderDetail({ id, detail: currentDetail as TDetail, refresh, loading: showLoading })
      ) : currentDetail ? (
        <pre>{JSON.stringify(currentDetail, null, 2)}</pre>
      ) : (
        <ErrorState message="未找到详情。" />
      )}
    </div>
  );
}
