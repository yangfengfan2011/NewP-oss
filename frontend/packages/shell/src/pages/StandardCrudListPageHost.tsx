import { useEffect, useMemo, useRef, useState, type ReactElement, type ReactNode } from 'react';
import { useLocation } from 'react-router-dom';
import { useLocalization } from '@icp-platform/core';
import { UserOutlined } from '@ant-design/icons';
import {
  ModuleRegistry,
  applyStandardCrudLookupValueChange,
  createStandardCrudClient,
  normalizeStandardCrudListResult,
  normalizeStandardCrudSubmitValues,
  useStandardCrudLookupRuntime,
} from '@icp-platform/sdk';
import type { StandardCrudFieldDefinition, StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { Button as AntdButton, Popconfirm, Popover } from 'antd';
import {
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

interface DeleteActionErrorState {
  key: string;
  message: string;
}

interface RowActionItem {
  key: string;
  label: ReactNode;
  danger?: boolean;
  disabled?: boolean;
  loading?: boolean;
  confirmMessage?: string;
  onClick: () => void | Promise<void>;
}

const CLOSED_DRAWER_STATE: CrudDrawerState = {
  mode: 'closed',
  source: 'user-action',
};

const MAX_ROW_ACTION_BUTTONS = 3;

function RowActionButtonGroup({ actions }: { actions: RowActionItem[] }) {
  const hasOverflow = actions.length > MAX_ROW_ACTION_BUTTONS;
  const inlineLimit = hasOverflow ? MAX_ROW_ACTION_BUTTONS - 1 : MAX_ROW_ACTION_BUTTONS;
  const inlineActions = actions.slice(0, inlineLimit);
  const overflowActions = actions.slice(inlineLimit);
  const renderActionButton = (action: RowActionItem, className: string) => {
    const button = (
      <AntdButton
        key={action.key}
        className={className}
        type="link"
        danger={action.danger}
        loading={action.loading}
        disabled={action.disabled}
        onClick={action.confirmMessage ? undefined : () => {
          void action.onClick();
        }}
      >
        {action.loading ? '处理中...' : action.label}
      </AntdButton>
    );

    return action.confirmMessage ? (
      <Popconfirm
        key={action.key}
        title={action.confirmMessage}
        okText="确认"
        cancelText="取消"
        okButtonProps={{ danger: action.danger, loading: action.loading }}
        onConfirm={action.onClick}
      >
        {button}
      </Popconfirm>
    ) : button;
  };

  return (
    <div className="np-row-actions">
      {inlineActions.map((action) => renderActionButton(action, 'np-row-action-button'))}
      {overflowActions.length > 0 ? (
        <Popover
          placement="bottomRight"
          trigger="click"
          content={(
            <div className="np-row-actions-overflow-menu">
              {overflowActions.map((action) => renderActionButton(action, 'np-row-action-menu-button'))}
            </div>
          )}
        >
          <AntdButton className="np-row-actions-more" type="link">
            更多
          </AntdButton>
        </Popover>
      ) : null}
    </div>
  );
}

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
  const location = useLocation();
  const { t } = useLocalization();
  const schema = useMemo(() => buildStandardCrudListSchema(view, registration), [view, registration]);
  const client = useMemo(() => createStandardCrudClient(schema.apiRoute), [schema.apiRoute]);
  const [filters, setFilters] = useState<TFilter>(() => registration.createEmptyFilters());
  const [appliedFilters, setAppliedFilters] = useState<TFilter>(() => registration.createEmptyFilters());
  const [rows, setRows] = useState<TRow[]>([]);
  const [total, setTotal] = useState(0);
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
  const [deleteActionError, setDeleteActionError] = useState<DeleteActionErrorState | null>(null);
  const [deletingActionKey, setDeletingActionKey] = useState<string | null>(null);
  const [rowActionError, setRowActionError] = useState<DeleteActionErrorState | null>(null);
  const [rowActionPendingKey, setRowActionPendingKey] = useState<string | null>(null);
  const detailRequestIdRef = useRef(0);
  const handledRouteSearchRef = useRef<string | null>(null);
  const drawerStateRef = useRef(drawerState);
  drawerStateRef.current = drawerState;

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

  const closeDrawerForRow = (rowId: string) => {
    if (drawerStateRef.current.id !== rowId) {
      return;
    }

    closeDrawer();
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

  useEffect(() => {
    if (handledRouteSearchRef.current === location.search) {
      return;
    }

    handledRouteSearchRef.current = location.search;
    const params = new URLSearchParams(location.search);
    const drawer = params.get('drawer');
    const id = params.get('id') ?? undefined;

    // 旧入口只负责把 URL 查询参数投影成当前抽屉态；实际详情加载和错误边界仍由 Host 统一处理。
    if (drawer === 'create') {
      openCreateDrawer('route-compatibility');
    }
    if (drawer === 'view' && id) {
      void openViewDrawer(id, 'route-compatibility');
    }
    if (drawer === 'edit' && id) {
      void openEditDrawer(id, 'route-compatibility');
    }
  }, [location.search]);

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

            return {
              items: result.items,
              total: result.total,
            };
          } catch (loadError) {
            setRows([]);
            setTotal(0);
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
          const rowDeleteActionError = deleteActionDefinitions.some((action) => deleteActionError?.key === `${action.key}:${rowId}`)
            ? deleteActionError
            : null;
          const rowExtraActionError = rowActionDefinitions.some((action) => rowActionError?.key === `${action.key}:${rowId}`)
            ? rowActionError
            : null;
          const rowActions: RowActionItem[] = [
            {
              key: 'view',
              label: '查看',
              onClick: () => {
                void openViewDrawer(rowId);
              },
            },
            {
              key: 'edit',
              label: '编辑',
              onClick: () => {
                void openEditDrawer(rowId);
              },
            },
            ...deleteActionDefinitions
              .filter((action) => hasActionPermission(action, registration.permissions))
              .map((action): RowActionItem => {
                const pendingKey = `${action.key}:${rowId}`;

                return {
                  key: action.key,
                  label: action.labelKey,
                  danger: true,
                  confirmMessage: action.confirmMessage ?? `确认${action.labelKey}吗？`,
                  loading: deletingActionKey === pendingKey,
                  disabled: deletingActionKey !== null,
                  onClick: async () => {
                    try {
                      setDeletingActionKey(pendingKey);
                      setDeleteActionError(null);

                      const handler = registration.actionHandlers?.[action.key];
                      if (handler) {
                        // 删除成功后的刷新由 Host 统一收口；这里保留 refresh 形参仅兼容既有 handler 签名。
                        await handler({ id: rowId, row, refresh: () => undefined });
                      } else {
                        await client.delete(rowId);
                      }

                      context.refresh();
                      closeDrawerForRow(rowId);
                    } catch (deleteError) {
                      setDeleteActionError({
                        key: pendingKey,
                        message: deleteError instanceof Error ? deleteError.message : '删除标准资源失败',
                      });
                    } finally {
                      setDeletingActionKey(null);
                    }
                  },
                };
              }),
            ...rowActionDefinitions
              .filter((action) => hasActionPermission(action, registration.permissions))
              .map((action): RowActionItem => {
                const pendingKey = `${action.key}:${rowId}`;
                const handler = registration.actionHandlers?.[action.key];

                return {
                  key: action.key,
                  label: action.labelKey,
                  danger: action.variant === 'danger',
                  confirmMessage: action.confirmMessage,
                  loading: rowActionPendingKey === pendingKey,
                  disabled: rowActionPendingKey !== null || !handler,
                  onClick: async () => {
                    if (!handler) {
                      return;
                    }

                    try {
                      setRowActionPendingKey(pendingKey);
                      setRowActionError(null);
                      await handler({ id: rowId, row, refresh: context.refresh });
                    } catch (actionError) {
                      setRowActionError({
                        key: pendingKey,
                        message: actionError instanceof Error ? actionError.message : '执行操作失败',
                      });
                    } finally {
                      setRowActionPendingKey(null);
                    }
                  },
                };
              }),
          ];

          return (
            <div style={{ display: 'grid', gap: 6 }}>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'flex-start' }}>
                <RowActionButtonGroup actions={rowActions} />
                {registration.renderRowActions ? registration.renderRowActions(row, context) : null}
              </div>
              {rowDeleteActionError || rowExtraActionError ? (
                <span role="alert" style={{ color: 'var(--sh-color-danger, #dc2626)', fontSize: 12 }}>
                  {(rowDeleteActionError ?? rowExtraActionError)?.message}
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

  const filterBar = registration.renderFilters ? (
    <CrudSearchBar
      fields={registration.renderFilters(context)}
      loading={loading}
      onSearch={context.applyFilters}
      onReset={context.resetFilters}
      more={<AntdButton type="text" size="middle">更多筛选</AntdButton>}
    />
  ) : undefined;

  const handleRefresh = async () => {
    if (refreshAction && registration.actionHandlers?.[refreshAction.key]) {
      await registration.actionHandlers[refreshAction.key]({ refresh: context.refresh });
      return;
    }

    context.refresh();
  };

  const createDrawerTitle = resolveDrawerTitle(createAction?.labelKey, '新建', pageTitle, createAction?.labelKey);
  const editDrawerTitle = resolveDrawerTitle(editAction?.labelKey, '编辑', pageTitle, createAction?.labelKey);

  const content = (
    <div className="np-standard-crud-table">
      {error ? <ErrorState message={error} /> : null}
      <CrudTableSection<TRow>
        rowKey={registration.getRowId}
        columns={columns}
        request={tableRequest}
        loading={loading}
        selectableRows
        showRowNumber
        rowIcon={<UserOutlined />}
        onRefresh={() => void handleRefresh()}
      />
    </div>
  );

  return (
    <>
      <CrudWorkspacePage
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
