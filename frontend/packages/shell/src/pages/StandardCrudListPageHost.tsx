import { useMemo, useRef, useState, type ReactElement } from 'react';
import { useLocalization } from '@icp-platform/core';
import {
  ModuleRegistry,
  applyStandardCrudLookupValueChange,
  createStandardCrudClient,
  normalizeStandardCrudListResult,
  normalizeStandardCrudSubmitValues,
  useStandardCrudLookupRuntime,
} from '@icp-platform/sdk';
import type { StandardCrudFieldDefinition, StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { Button as AntdButton } from 'antd';
import {
  CrudDangerAction,
  CrudDetailDrawer,
  CrudFormDrawer,
  CrudSearchBar,
  CrudTableSection,
  CrudWorkspacePage,
  ErrorState,
  StandardCrudForm,
  createSearchTableRequest,
} from '@icp-platform/ui';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { buildStandardCrudListSchema } from '../metadata/standardCrudSchemaResolver';
import { resolveListViewDocument } from '../metadata/viewMetadataResolver';
import { buildStandardCrudRegistry } from '../registries/standardCrudRegistry';
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';
import {
  buildStandardCrudDetailColumns,
  buildStandardCrudDetailFormValues,
  buildStandardCrudDetailPreviewData,
} from '../standard-crud/standardCrudDetailView';

interface StandardCrudListPageHostProps {
  viewKey: string;
}

type CrudDrawerMode = 'closed' | 'create' | 'view' | 'edit';

interface CrudDrawerState {
  mode: CrudDrawerMode;
  id?: string;
  source: 'user-action' | 'route-compatibility';
}

const CLOSED_DRAWER_STATE: CrudDrawerState = {
  mode: 'closed',
  source: 'user-action',
};

function resolveCreateFormFields(fields: StandardCrudFieldDefinition[]): StandardCrudFieldDefinition[] {
  return fields.map((field) => {
    // SDK 目前没有 create-only readOnly 契约；这里仅放开“创建时必填且非 readonly-kind”的字段，
    // 避免首次录入被 UI 锁死，同时不把详情页只读约束整体打穿到创建态。
    if (field.readOnly === true && field.kind !== 'readonly' && field.required === true) {
      return { ...field, readOnly: false };
    }

    return field;
  });
}

function inferDrawerTitleFromCreateAction(
  actionLabel: string,
  fallbackAction: '新建' | '编辑',
  createActionLabel: string | undefined,
): string | null {
  if (actionLabel !== fallbackAction) {
    return null;
  }

  const normalizedCreateActionLabel = createActionLabel?.trim();
  if (!normalizedCreateActionLabel?.startsWith('新建')) {
    return null;
  }

  const inferredResourceSuffix = normalizedCreateActionLabel.slice('新建'.length).trim();
  if (inferredResourceSuffix.length === 0) {
    return null;
  }

  // 当动作标签只剩“编辑”这类短动词时，优先复用同资源“新建…”标签中的对象后缀，
  // 保持抽屉标题语义完整，同时不把具体资源词汇硬编码进 Shell。
  return `${fallbackAction}${inferredResourceSuffix}`;
}

function resolveDrawerTitle(
  actionLabel: string | undefined,
  fallbackAction: '新建' | '编辑',
  resourceTitle: string,
  createActionLabel?: string,
): string {
  const normalizedActionLabel = actionLabel?.trim();
  if (normalizedActionLabel) {
    return inferDrawerTitleFromCreateAction(normalizedActionLabel, fallbackAction, createActionLabel) ?? normalizedActionLabel;
  }

  return `${fallbackAction}${resourceTitle}`;
}

function hasActionPermission(action: { requiredPermissions?: string[] }, grantedPermissions: string[] = []): boolean {
  return (action.requiredPermissions ?? []).every((permission) => grantedPermissions.includes(permission));
}

export default function StandardCrudListPageHost({ viewKey }: StandardCrudListPageHostProps): ReactElement {
  const runtimeMetadata = useRuntimeMetadata();
  const viewDocument = resolveListViewDocument(runtimeMetadata?.viewDocuments ?? [], viewKey);
  const registration = buildStandardCrudRegistry(ModuleRegistry.getAllModules()).findByListViewKey(viewKey);

  if (!viewDocument) {
    return <div className="sh-error-state">未找到列表视图元数据</div>;
  }

  if (!registration) {
    return <div className="sh-error-state">未找到标准 CRUD 前端资源注册项</div>;
  }

  return <StandardCrudListPageContent view={viewDocument.view as Record<string, unknown>} registration={registration} />;
}

function StandardCrudListPageContent<TFilter extends Record<string, unknown>, TRow extends Record<string, unknown>>({
  view,
  registration,
}: {
  view: Record<string, unknown>;
  registration: StandardCrudResourceRegistration<TFilter, TRow>;
}): ReactElement {
  const { t } = useLocalization();
  const decoratedRegistration = registration as StandardCrudResourceRegistration<TFilter, TRow> & {
    listPanelTitle?: string;
  };
  const schema = useMemo(() => buildStandardCrudListSchema(view, registration), [view, registration]);
  const client = useMemo(() => createStandardCrudClient(schema.apiRoute), [schema.apiRoute]);
  const [filters, setFilters] = useState<TFilter>(() => registration.createEmptyFilters());
  const [appliedFilters, setAppliedFilters] = useState<TFilter>(() => registration.createEmptyFilters());
  const [rows, setRows] = useState<TRow[]>([]);
  const [total, setTotal] = useState(0);
  const [pageInfo, setPageInfo] = useState({ page: 1, pageSize: 20 });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadTick, setReloadTick] = useState(0);
  const [drawerState, setDrawerState] = useState<CrudDrawerState>(CLOSED_DRAWER_STATE);
  const [detailFields, setDetailFields] = useState<Record<string, unknown>>({});
  const [detailError, setDetailError] = useState<string | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [formValues, setFormValues] = useState<Record<string, unknown>>({});
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [deleteActionError, setDeleteActionError] = useState<string | null>(null);
  const [deletingActionKey, setDeletingActionKey] = useState<string | null>(null);
  const detailRequestIdRef = useRef(0);

  const context = {
    filters,
    setFilters,
    applyFilters: () => setAppliedFilters(filters),
    resetFilters: () => {
      const empty = registration.createEmptyFilters();
      setFilters(empty);
      setAppliedFilters(empty);
    },
    refresh: () => setReloadTick((current) => current + 1),
    loading,
    rows,
    total,
  };
  const pageTitle = t(`${schema.dicKey}.${schema.titleKey}`, schema.titleKey);
  const editFields = registration.fields ?? [];
  const createFields = useMemo(() => resolveCreateFormFields(editFields), [editFields]);
  const activeFormFields = drawerState.mode === 'create' ? createFields : drawerState.mode === 'edit' ? editFields : [];
  const { lookupOptions, lookupLoading, lookupErrors } = useStandardCrudLookupRuntime({
    fields: activeFormFields,
    providers: registration.lookupProviders ?? [],
    values: formValues,
    detailFields,
  });

  const invalidateDetailRequests = () => {
    detailRequestIdRef.current += 1;
  };

  const closeDrawer = () => {
    invalidateDetailRequests();
    setDetailLoading(false);
    setDrawerState(CLOSED_DRAWER_STATE);
  };

  const openCreateDrawer = (source: CrudDrawerState['source'] = 'user-action') => {
    invalidateDetailRequests();
    const initialValues = Object.fromEntries(createFields.map((field) => [field.key, field.defaultValue ?? '']));
    setDetailFields({});
    setDetailLoading(false);
    setFormValues(initialValues);
    setDetailError(null);
    setSaveError(null);
    setDrawerState({ mode: 'create', source });
  };

  const openViewDrawer = async (id: string, source: CrudDrawerState['source'] = 'user-action'): Promise<boolean> => {
    const requestId = detailRequestIdRef.current + 1;
    detailRequestIdRef.current = requestId;
    setDrawerState({ mode: 'view', id, source });
    setDetailLoading(true);
    setDetailError(null);
    setSaveError(null);
    try {
      const detail = await client.getDetail(id);
      if (detailRequestIdRef.current !== requestId) {
        return false;
      }

      setDetailFields(detail.fields);
      setFormValues(buildStandardCrudDetailFormValues(detail.fields, editFields));
      return true;
    } catch (loadError) {
      if (detailRequestIdRef.current !== requestId) {
        return false;
      }

      setDetailFields({});
      setFormValues({});
      setDetailError(loadError instanceof Error ? loadError.message : '加载详情失败');
      return false;
    } finally {
      if (detailRequestIdRef.current === requestId) {
        setDetailLoading(false);
      }
    }
  };

  const openEditDrawer = async (id: string, source: CrudDrawerState['source'] = 'user-action') => {
    const loaded = await openViewDrawer(id, source);
    if (!loaded) {
      return;
    }

    setDrawerState((current) => (
      current.mode === 'view' && current.id === id
        ? { mode: 'edit', id, source }
        : current
    ));
  };

  const handleDrawerSave = async () => {
    try {
      setSaving(true);
      setSaveError(null);
      const values = registration.buildMutationValues
        ? registration.buildMutationValues(formValues)
        : normalizeStandardCrudSubmitValues(editFields, formValues);

      if (drawerState.mode === 'create') {
        await client.create(values);
      } else if (drawerState.mode === 'edit' && drawerState.id) {
        await client.update(drawerState.id, values);
      }

      context.refresh();
      closeDrawer();
    } catch (saveFailure) {
      setSaveError(saveFailure instanceof Error ? saveFailure.message : '保存标准资源失败');
    } finally {
      setSaving(false);
    }
  };

  const handleFormChange = (fieldKey: string, value: unknown) => {
    const fields = drawerState.mode === 'create' ? createFields : editFields;
    setFormValues((current) => applyStandardCrudLookupValueChange(current, fieldKey, value, fields));
  };

  const tableRequest = useMemo(
    () =>
      createSearchTableRequest<Record<string, unknown>, TRow, TRow>({
        buildQuery: () => registration.buildListQuery(appliedFilters) as Record<string, unknown>,
        load: async (query) => {
          try {
            setLoading(true);
            setError(null);
            const rawResult = await client.list(query as Record<string, string | number | boolean | null | undefined>);
            const result = normalizeStandardCrudListResult(rawResult, registration.mapListItem);

            setRows(result.items);
            setTotal(result.total);
            setPageInfo({ page: result.page, pageSize: result.pageSize });

            return {
              items: result.items,
              total: result.total,
            };
          } catch (loadError) {
            setRows([]);
            setTotal(0);
            setPageInfo({ page: 1, pageSize: 20 });
            setError(loadError instanceof Error ? loadError.message : '加载标准 CRUD 列表失败');
            throw loadError;
          } finally {
            setLoading(false);
          }
        },
        mapRow: (row) => row,
      }),
    [client, registration, appliedFilters, reloadTick],
  );

  const filterBar = registration.renderFilters ? (
    <CrudSearchBar
      fields={registration.renderFilters(context)}
      loading={loading}
      onSearch={context.applyFilters}
      onReset={context.resetFilters}
      more={<AntdButton type="text" size="middle">更多筛选</AntdButton>}
    />
  ) : undefined;

  const createAction = registration.listActions?.find((action) => action.standardAction === 'create');
  const editAction = registration.listActions?.find((action) => action.standardAction === 'edit');
  const refreshAction = registration.listActions?.find((action) => action.standardAction === 'refresh');
  const deleteActionDefinitions = registration.listActions?.filter((action) => action.standardAction === 'delete') ?? [];
  const rowActionDefinitions = registration.listActions?.filter((action) => (
    !['create', 'refresh', 'view', 'edit', 'delete'].includes(action.standardAction ?? '')
  )) ?? [];
  const shouldRenderActionColumn = Boolean(
    registration.renderRowActions
    || deleteActionDefinitions.length > 0
    || rowActionDefinitions.length > 0
    || registration.getRowId,
  );
  const standardColumns = shouldRenderActionColumn
    ? [
      ...registration.buildColumns(context),
      {
        key: 'actions',
        title: '操作',
        render: (row: TRow) => {
          const rowId = registration.getRowId(row);

          return (
            <div style={{ display: 'grid', gap: 6 }}>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'flex-start' }}>
                <AntdButton type="link" onClick={() => void openViewDrawer(rowId)}>查看</AntdButton>
                <AntdButton type="link" onClick={() => void openEditDrawer(rowId)}>编辑</AntdButton>
                {deleteActionDefinitions
                  .filter((action) => hasActionPermission(action, registration.permissions))
                  .map((action) => {
                    const pendingKey = `${action.key}:${rowId}`;

                    return (
                      <CrudDangerAction
                        key={action.key}
                        confirmMessage={action.confirmMessage ?? `确认${action.labelKey}吗？`}
                        loading={deletingActionKey === pendingKey}
                        disabled={deletingActionKey !== null}
                        onConfirm={async () => {
                          try {
                            setDeletingActionKey(pendingKey);
                            setDeleteActionError(null);

                            const handler = registration.actionHandlers?.[action.key];
                            if (handler) {
                              await handler({ id: rowId, row, refresh: context.refresh });
                            } else {
                              await client.delete(rowId);
                              context.refresh();
                            }

                            if (drawerState.id === rowId) {
                              closeDrawer();
                            }
                          } catch (deleteError) {
                            setDeleteActionError(deleteError instanceof Error ? deleteError.message : '删除标准资源失败');
                          } finally {
                            setDeletingActionKey(null);
                          }
                        }}
                      >
                        {action.labelKey}
                      </CrudDangerAction>
                    );
                  })}
                {registration.renderRowActions ? registration.renderRowActions(row, context) : null}
                {rowActionDefinitions.length > 0 ? (
                  <StandardCrudActions
                    actions={rowActionDefinitions}
                    grantedPermissions={registration.permissions}
                    handlers={registration.actionHandlers}
                    context={{ id: rowId, row, refresh: context.refresh }}
                  />
                ) : null}
              </div>
              {deleteActionError ? (
                <span role="alert" style={{ color: 'var(--sh-color-danger, #dc2626)', fontSize: 12 }}>
                  {deleteActionError}
                </span>
              ) : null}
            </div>
          );
        },
      },
    ]
    : registration.buildColumns(context);
  const columns = standardColumns.map((column) => ({
    key: column.key,
    dataIndex: column.key,
    title: column.title,
    render: (_: unknown, row: TRow) => (column.render ? column.render(row) : readTableCell(row, column.key)),
  }));

  const headerActions = createAction
    ? (
      <AntdButton
        type="primary"
        size="middle"
        onClick={() => openCreateDrawer()}
      >
        {createAction.labelKey}
      </AntdButton>
    )
    : registration.renderHeaderActions ? registration.renderHeaderActions(context) : undefined;

  const handleRefresh = async () => {
    if (refreshAction && registration.actionHandlers?.[refreshAction.key]) {
      await registration.actionHandlers[refreshAction.key]({ refresh: context.refresh });
      return;
    }

    context.refresh();
  };

  const listPanelTitle = decoratedRegistration.listPanelTitle ?? `${pageTitle}列表`;
  const pageCount = Math.max(1, Math.ceil(total / Math.max(1, pageInfo.pageSize)));
  const tablePageSummary = total > 0 ? (
    <span className="np-list-card-page-summary">{`第 ${pageInfo.page} / ${pageCount} 页`}</span>
  ) : null;
  const createDrawerTitle = resolveDrawerTitle(createAction?.labelKey, '新建', pageTitle, createAction?.labelKey);
  const editDrawerTitle = resolveDrawerTitle(editAction?.labelKey, '编辑', pageTitle, createAction?.labelKey);

  const content = (
    <div className="np-standard-crud-table">
      {error ? <ErrorState message={error} /> : null}
      <CrudTableSection<TRow>
        rowKey={registration.getRowId}
        columns={columns}
        request={tableRequest}
        title={listPanelTitle}
        total={total}
        loading={loading}
        onRefresh={() => void handleRefresh()}
        toolbar={tablePageSummary}
      />
    </div>
  );

  return (
    <>
      <CrudWorkspacePage
        title={pageTitle}
        description={t(`${schema.dicKey}.${schema.descriptionKey}`, schema.descriptionKey)}
        primaryActions={headerActions}
        filterBar={filterBar}
      >
        {content}
      </CrudWorkspacePage>
      <CrudDetailDrawer
        open={drawerState.mode === 'view'}
        title={pageTitle}
        dataSource={buildStandardCrudDetailPreviewData(detailFields, editFields)}
        columns={buildStandardCrudDetailColumns(detailFields, editFields)}
        loading={detailLoading}
        error={detailError}
        actions={<AntdButton type="primary" onClick={() => drawerState.id && void openEditDrawer(drawerState.id)}>编辑</AntdButton>}
        onClose={closeDrawer}
      />
      <CrudFormDrawer
        open={drawerState.mode === 'create' || drawerState.mode === 'edit'}
        title={drawerState.mode === 'create' ? createDrawerTitle : editDrawerTitle}
        saving={saving}
        error={saveError}
        onSubmit={() => void handleDrawerSave()}
        onClose={closeDrawer}
      >
        <StandardCrudForm
          formId={`${schema.resourceKey}-drawer-form`}
          mode={drawerState.mode === 'create' ? 'create' : 'edit'}
          fields={drawerState.mode === 'create' ? createFields : editFields}
          sections={registration.formSections ?? []}
          values={formValues}
          lookupOptions={lookupOptions}
          lookupLoading={lookupLoading}
          lookupErrors={lookupErrors}
          onChange={handleFormChange}
          onSubmit={() => void handleDrawerSave()}
          saving={saving}
          error={saveError}
          titleForSection={(titleKey) => titleKey}
        />
      </CrudFormDrawer>
    </>
  );
}

function readTableCell<TRow extends Record<string, unknown>>(row: TRow, key: string): string {
  const value = row[key];
  return value == null ? '' : String(value);
}
