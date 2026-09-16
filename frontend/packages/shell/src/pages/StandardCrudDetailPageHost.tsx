import { useFeaturePlacement } from '../authorization/FeaturePlacementContext';
import { Component, lazy, Suspense, useEffect, useMemo, useRef, useState, type ErrorInfo, type ReactElement, type ReactNode } from 'react';
import { useLocalization } from '@icp-platform/core';
import {
  ModuleRegistry,
  createStandardCrudOperationResultPresenterRegistry,
  type StandardCrudFieldDefinition,
  type StandardCrudResourceRegistration,
  type StandardCrudOperationResult,
} from '@icp-platform/sdk';
import { ErrorState, LoadingState } from '@icp-platform/ui/core';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { useOptionalAuth } from '../contexts/AuthContext';
import { resolveStandardCrudDetailSchema } from '../metadata/standardCrudSchemaResolver';
import { resolveDetailViewDocument } from '../metadata/viewMetadataResolver';
import { buildStandardCrudRegistry } from '../registries/standardCrudRegistry';
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';
import { useStandardCrudDetailQuery } from '../standard-crud/standardCrudDetailQuery';
import { buildStandardCrudFormSchemaFromModel } from '../standard-crud/standardCrudMetadataForm';
import StandardCrudResolvedDetailSections from '../standard-crud/StandardCrudResolvedDetailSections';
import {
  reportStandardCrudDetailDiagnostics,
  resolveStandardCrudDetailPresentation,
} from '../standard-crud/standardCrudDetailPresentation';
import { StandardCrudSummaryBlocks } from '../standard-crud/standardCrudSummaryRuntime';

import { useStandardCrudHostEnvironment } from '../standard-crud/standardCrudHostEnvironment';

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
  // 详情宿主从不拥有地址；独立路由下也传 null。
  const host = useStandardCrudHostEnvironment(null);
  const features = useFeaturePlacement();
  const composed = host.presentation.kind !== 'workspace' || host.presentation.composed;
  const sessionTimeZoneId = useOptionalAuth()?.user?.timeZoneId;
  const dicKey = readMetadataString(view.dicKey, registration.resourceKey);
  const dataAdapter = registration.dataAdapter;
  const operationResultPresenterRegistry = useMemo(
    () => createStandardCrudOperationResultPresenterRegistry(registration.operationResultPresenters ?? []),
    [registration.operationResultPresenters],
  );
  const modelDocument = useMemo(() => findStandardCrudModelDocument(modelDocuments, view.model), [modelDocuments, view]);
  const formSchema = useMemo(() => buildStandardCrudFormSchemaFromModel(modelDocument, {
    mode: 'edit',
    enumOptions: registration.enumOptions ?? {},
    lookupProviders: registration.lookupProviders ?? [],
    timeZoneId: sessionTimeZoneId,
  }), [modelDocument, registration.enumOptions, registration.lookupProviders, sessionTimeZoneId]);
  const editFields = useMemo(
    () => localizeStandardCrudFields(formSchema.fields, dicKey, t),
    [formSchema.fields, dicKey, t],
  );
  const [editing, setEditing] = useState(false);
  const [presentedOperationResult, setPresentedOperationResult] = useState<StandardCrudOperationResult | null>(null);
  const detailQuery = useStandardCrudDetailQuery({
    resourceKey: registration.resourceKey,
    id,
    dataAdapter,
    featurePosition: features.readPosition,
  });

  useEffect(() => {
    setEditing(false);
  }, [id]);

  const currentDetail = detailQuery.detail?.value ?? null;
  const currentNormalizedDetail = detailQuery.detail?.normalized ?? null;
  const schema = useMemo(
    () => resolveStandardCrudDetailSchema(viewDocuments, registration, { modelDocuments }),
    [modelDocuments, registration, viewDocuments],
  );
  const detailFeatureActions = useMemo(() => {
    if (!features.placement || !Array.isArray(view.featureActions)) return undefined;
    return Object.fromEntries(view.featureActions.flatMap((action) => {
      if (typeof action !== 'object' || action === null || Array.isArray(action)) return [];
      const key = (action as Record<string, unknown>).key;
      if (typeof key !== 'string') return [];
      const position = features.action(key, 'detail.custom');
      return position ? [[key, { position, allowed: features.hasFeature(position) }]] : [];
    }));
  }, [features, view.featureActions]);
  const presentation = useMemo(() => currentNormalizedDetail
    ? resolveStandardCrudDetailPresentation({ schema, fields: currentNormalizedDetail.fields, expectedId: id })
    : null, [currentNormalizedDetail, id, schema]);
  const readyPresentation = presentation?.kind === 'ready' ? presentation : null;

  useEffect(() => {
    if (!presentation) return;
    reportStandardCrudDetailDiagnostics(
      schema,
      presentation.kind === 'fatal' ? [presentation.diagnostic] : presentation.diagnostics,
    );
  }, [presentation, schema]);

  const detailSectionPages = readyPresentation ? (
    <StandardCrudResolvedDetailSections
      presentation={readyPresentation}
      registration={registration}
      textFor={(key, fallback) => t(`${schema.dicKey}.${key}`, fallback)}
    />
  ) : null;
  const refresh = detailQuery.refresh;
  const embeddedRefreshVersion = host.sync.refreshVersion;
  const consumedRefreshVersion = useRef(0);
  useEffect(() => {
    if (!embeddedRefreshVersion || consumedRefreshVersion.current === embeddedRefreshVersion) return;
    // 先消费组合通知；查询快照更新产生的新 refresh 函数不能重放同一次读取。
    consumedRefreshVersion.current = embeddedRefreshVersion;
    refresh();
  }, [embeddedRefreshVersion, refresh]);
  const detailActionHandlers = useMemo(() => ({
    ...registration.actionHandlers,
    edit: () => setEditing(true),
  }), [registration.actionHandlers]);
  const showLoading = detailQuery.loading;
  // 背景刷新保留 Action Execution 子树及其冲突反馈，同时禁用旧快照上的再次操作。
  const detailHeaderActions = currentDetail && readyPresentation
    ? readyPresentation.pageActions.length > 0
      ? (
        <StandardCrudActions
          resourceKey={registration.resourceKey}
          onCommitted={host.sync.committed}
          actions={readyPresentation.pageActions.map((action) => ({
            ...action,
            labelKey: t(`${schema.dicKey}.${action.labelKey}`, action.labelKey),
            runtimeDisabled: detailQuery.refreshing || action.disabled,
            disabledReason: action.disabledReasonKey
              ? t(`${schema.dicKey}.${action.disabledReasonKey}`, action.disabledReasonKey)
              : undefined,
            confirmMessage: action.confirmMessageKey
              ? t(`${schema.dicKey}.${action.confirmMessageKey}`, action.confirmMessageKey)
              : undefined,
          }))}
          featurePositionForAction={(action) => action.navigationTargetKey ? features.navigation(action.navigationTargetKey) : features.action(action.key, 'detail.pageActions')}
          hasFeature={features.hasFeature}
          handlers={detailActionHandlers}
          actionInputs={registration.actionInputRenderers}
          actionStateResolver={registration.actionStateResolver}
          onResult={(result) => {
            if (operationResultPresenterRegistry.resolve(result)) setPresentedOperationResult(result);
          }}
          refresh={refresh}
          context={{ id, detail: currentDetail as TDetail }}
        />
      )
      : undefined
    : undefined;

  return (
    <>
      {presentedOperationResult
        ? operationResultPresenterRegistry.resolve(presentedOperationResult)?.render({
            result: presentedOperationResult,
            close: () => setPresentedOperationResult(null),
          })
        : null}
      <div className={composed ? undefined : "sh-page"}>
      {!composed && detailHeaderActions && <div className="sh-page-actions">{detailHeaderActions}</div>}

      {showLoading ? (
        <LoadingState message="正在加载详情..." />
      ) : detailQuery.error ? (
        <ErrorState>
          <span>{detailQuery.error}</span>
          <button type="button" className="sh-btn sh-btn-primary" onClick={detailQuery.retry}>重试</button>
        </ErrorState>
      ) : presentation?.kind === 'fatal' ? (
        <ErrorState message={`详情数据契约失败：${presentation.diagnostic.code}`} />
      ) : currentNormalizedDetail && readyPresentation ? (
        <div className="sh-page-stack">
          <StandardCrudSummaryBlocks
            blocks={schema.summaryBlocks}
            summaries={currentNormalizedDetail.summaries}
            renderers={registration.summaryRenderers}
            featureActions={detailFeatureActions}
            titleForBlock={(key) => t(`${schema.dicKey}.${toDictionaryBlockKey(key)}`, key)}
          />
          {detailSectionPages}
          {editing && editFields.length > 0 && (!features.placement || features.hasFeature(features.action('edit', 'detail.pageActions') ?? '')) ? (
            <Suspense fallback={<div className="sh-loading-state" role="status">正在加载编辑表单…</div>}>
              <StandardCrudDetailEditor
                key={id}
                id={id}
                resourceKey={schema.resourceKey}
                featurePosition={features.action('edit', 'detail.pageActions')}
                detail={currentNormalizedDetail}
                fields={editFields}
                sections={formSchema.sections}
                registration={registration}
                onSaved={() => {
                  setEditing(false);
                }}
                onRefresh={refresh}
                onResult={(result) => {
                  if (operationResultPresenterRegistry.resolve(result)) {
                    setPresentedOperationResult(result);
                  }
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
    </>
  );
}
