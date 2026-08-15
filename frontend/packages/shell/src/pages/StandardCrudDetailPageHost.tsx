import { Component, lazy, Suspense, useEffect, useMemo, useState, type ErrorInfo, type ReactElement, type ReactNode } from 'react';
import { platformFetch, useAuthorization, useLocalization } from '@icp-platform/core';
import {
  ModuleRegistry,
  createStandardCrudClient,
  normalizeStandardCrudDetail,
  type StandardCrudDetail,
  type StandardCrudFieldDefinition,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';
import { ErrorState, LoadingState, PageHeader } from '@icp-platform/ui/core';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { resolveStandardCrudDetailSchema } from '../metadata/standardCrudSchemaResolver';
import { resolveDetailViewDocument } from '../metadata/viewMetadataResolver';
import { buildStandardCrudRegistry } from '../registries/standardCrudRegistry';
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';
import { buildStandardCrudFormSchemaFromModel } from '../standard-crud/standardCrudMetadataForm';
import StandardCrudResolvedDetailSections from '../standard-crud/StandardCrudResolvedDetailSections';
import { StandardCrudSummaryBlocks } from '../standard-crud/standardCrudSummaryRuntime';

type MetadataRecord = Record<string, unknown>;

const StandardCrudDetailEditor = lazy(() => import('./StandardCrudDetailEditor'));

interface StandardCrudDetailPageHostProps {
  viewKey: string;
  id: string | null;
  registration?: StandardCrudResourceRegistration;
}

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

function readMetadataString(value: unknown, fallback: string): string {
  return typeof value === 'string' && value.trim().length > 0 ? value : fallback;
}

function useOptionalGrantedPermissions(fallbackPermissions: string[] = []): string[] {
  try {
    return useAuthorization().permissions;
  } catch {
    return fallbackPermissions;
  }
}

export default function StandardCrudDetailPageHost({
  viewKey,
  id,
  registration: loadedRegistration,
}: StandardCrudDetailPageHostProps): ReactElement {
  const runtimeMetadata = useRuntimeMetadata();
  const viewDocument = resolveDetailViewDocument(runtimeMetadata?.viewDocuments ?? [], viewKey);
  const registration = loadedRegistration
    ?? buildStandardCrudRegistry(ModuleRegistry.getAllModules()).findByDetailViewKey(viewKey);

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
      viewDocuments={(runtimeMetadata?.viewDocuments ?? []) as unknown as MetadataRecord[]}
      modelDocuments={runtimeMetadata?.modelDocuments ?? []}
    />
    </StandardCrudHostErrorBoundary>
  );
}

function StandardCrudDetailPageContent<TDetail>({
  id,
  view,
  registration,
  viewDocuments,
  modelDocuments,
}: {
  id: string;
  view: Record<string, unknown>;
  registration: StandardCrudResourceRegistration<Record<string, string>, Record<string, unknown>, TDetail>;
  viewDocuments: MetadataRecord[];
  modelDocuments: MetadataRecord[];
}): ReactElement {
  const { t } = useLocalization();
  const grantedPermissions = useOptionalGrantedPermissions(registration.permissions);
  const dicKey = readMetadataString(view.dicKey, registration.resourceKey);
  const titleKey = readMetadataString(view.titleKey, 'Title');
  const descriptionKey = readMetadataString(view.descriptionKey, 'Description');
  const client = useMemo(() => createStandardCrudClient(registration.apiRoute, platformFetch), [registration.apiRoute]);
  const modelDocument = useMemo(() => findStandardCrudModelDocument(modelDocuments, view.model), [modelDocuments, view]);
  const formSchema = useMemo(() => buildStandardCrudFormSchemaFromModel(modelDocument, {
    mode: 'edit',
    enumOptions: registration.enumOptions ?? {},
    lookupProviders: registration.lookupProviders ?? [],
  }), [modelDocument, registration.enumOptions, registration.lookupProviders]);
  const editFields = useMemo(
    () => localizeStandardCrudFields(formSchema.fields, dicKey, t),
    [formSchema.fields, dicKey, t],
  );
  const [loadedDetail, setLoadedDetail] = useState<LoadedDetail<TDetail> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadTick, setReloadTick] = useState(0);
  const [editing, setEditing] = useState(false);

  useEffect(() => {
    setEditing(false);
  }, [id]);

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
  }, [client, editFields, id, registration, reloadTick]);

  const currentDetail = loadedDetail?.id === id ? loadedDetail.value : null;
  const currentNormalizedDetail = loadedDetail?.id === id ? loadedDetail.normalized : null;
  const schema = useMemo(() => currentNormalizedDetail
    ? resolveStandardCrudDetailSchema(viewDocuments, registration, {
        modelDocuments,
        detailResponseFields: Object.keys(currentNormalizedDetail.fields),
      })
    : null, [currentNormalizedDetail, modelDocuments, registration, viewDocuments]);
  const detailSectionPages = schema && currentNormalizedDetail ? (
    <StandardCrudResolvedDetailSections
      schema={schema}
      detailFields={currentNormalizedDetail.fields}
      registration={registration}
      textFor={(key, fallback) => t(`${schema.dicKey}.${key}`, fallback)}
    />
  ) : null;
  const refresh = () => setReloadTick((current) => current + 1);
  const detailActionHandlers = useMemo(() => ({
    ...registration.actionHandlers,
    edit: () => setEditing(true),
  }), [registration.actionHandlers]);
  const showLoading = loading || (loadedDetail !== null && loadedDetail.id !== id);
  const detailHeaderActions = !showLoading && currentDetail && schema
    ? schema.pageActions.length > 0
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
          actionInputs={registration.actionInputRenderers}
          context={{ id, detail: currentDetail as TDetail, refresh }}
        />
      )
      : undefined
    : undefined;

  return (
    <div className="sh-page">
      <PageHeader
        eyebrow={schema?.resourceKey ?? registration.resourceKey}
        title={t(`${dicKey}.${schema?.titleKey ?? titleKey}`, schema?.titleKey ?? titleKey)}
        description={t(`${dicKey}.${schema?.descriptionKey ?? descriptionKey}`, schema?.descriptionKey ?? descriptionKey)}
        actions={detailHeaderActions}
      />

      {showLoading ? (
        <LoadingState message="正在加载详情..." />
      ) : error ? (
        <ErrorState message={error} />
      ) : currentNormalizedDetail && schema ? (
        <div className="sh-page-stack">
          <StandardCrudSummaryBlocks
            blocks={schema.summaryBlocks}
            summaries={currentNormalizedDetail.summaries}
            renderers={registration.summaryRenderers}
            titleForBlock={(key) => t(`${schema.dicKey}.${toDictionaryBlockKey(key)}`, key)}
          />
          {detailSectionPages}
          {editing && editFields.length > 0 ? (
            <Suspense fallback={<div className="sh-loading-state" role="status">正在加载编辑表单…</div>}>
              <StandardCrudDetailEditor
                key={id}
                id={id}
                resourceKey={schema.resourceKey}
                detail={currentNormalizedDetail}
                fields={editFields}
                sections={formSchema.sections}
                registration={registration}
                onSave={(values) => client.update(id, values).then(() => undefined)}
                onSaved={() => {
                  setEditing(false);
                  refresh();
                }}
                titleForSection={(sectionTitleKey) => t(`${schema.dicKey}.${sectionTitleKey}`, sectionTitleKey)}
              />
            </Suspense>
          ) : null}
        </div>
      ) : (
        <ErrorState message="未找到详情。" />
      )}
    </div>
  );
}
