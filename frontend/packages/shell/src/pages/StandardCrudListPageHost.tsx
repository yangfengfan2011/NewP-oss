import { Component, useEffect, useMemo, useRef, useState, type ErrorInfo, type ReactElement, type ReactNode } from 'react';
import { useLocation } from 'react-router-dom';
import { useAuthorization, useLocalization } from '@icp-platform/core';
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
import { Button as AntdButton, Popconfirm, Popover, Select } from 'antd';
import {
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
import { buildStandardCrudFormSchemaFromModel } from '../standard-crud/standardCrudMetadataForm';
import { buildStandardCrudDefaultFilters, buildStandardCrudListQuery } from '../standard-crud/standardCrudMetadataQuery';
import {
  buildStandardCrudDetailFormValues,
} from '../standard-crud/standardCrudDetailView';

type MetadataRecord = Record<string, unknown>;

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

const CLOSED_DRAWER_STATE: CrudDrawerState = {
  mode: 'closed',
  source: 'user-action',
};

const MAX_ROW_ACTION_BUTTONS = 3;

function findStandardCrudModelDocument(modelDocuments: MetadataRecord[], modelKey: unknown): MetadataRecord {
  if (typeof modelKey !== 'string' || modelKey.trim().length === 0) {
    return { fields: [] };
  }

  const document = modelDocuments.find((item) => item?.type === 'model' && Object.prototype.hasOwnProperty.call(item, modelKey));
  const model = document?.[modelKey];
  return typeof model === 'object' && model !== null && !Array.isArray(model) ? (model as MetadataRecord) : { fields: [] };
}

function findStandardCrudModelField(model: MetadataRecord, fieldName: string | undefined): MetadataRecord {
  if (!fieldName) {
    return {};
  }

  const fields = Array.isArray(model.fields) ? model.fields : [];
  return fields.find((field) => (
    typeof field === 'object'
    && field !== null
    && !Array.isArray(field)
    && (field as Record<string, unknown>).name === fieldName
  )) as MetadataRecord ?? {};
}

function localizeStandardCrudFields(
  fields: StandardCrudFieldDefinition[],
  dicKey: string,
  t: (key: string, fallback?: string) => string,
): StandardCrudFieldDefinition[] {
  return fields.map((field) => ({
    ...field,
    labelKey: t(`${dicKey}.${field.labelKey}`, field.labelKey),
  }));
}

function renderMetadataFilterField(
  field: {
    key: string;
    labelKey: string;
    dataSourceKey?: string;
    field?: string;
  },
  modelDocument: MetadataRecord,
  registration: StandardCrudResourceRegistration<Record<string, unknown>, Record<string, unknown>>,
  value: unknown,
  onChange: (key: string, value: unknown) => void,
  labelFor: (labelKey: string) => string,
): ReactElement {
  const fieldLabel = labelFor(field.labelKey);
  const enumOptions = field.dataSourceKey ? registration.enumOptions?.[field.dataSourceKey] : undefined;
  if (enumOptions) {
    return (
      <label key={field.key} className="np-filter-control">
        <span className="sh-form-label">{fieldLabel}</span>
        <Select
          className="np-filter-select"
          value={typeof value === 'string' && value.length > 0 ? value : undefined}
          allowClear
          options={enumOptions}
          onChange={(nextValue) => onChange(field.key, nextValue ?? '')}
        />
      </label>
    );
  }

  const modelField = findStandardCrudModelField(modelDocument, field.field);
  const storageType = typeof modelField.storage === 'object' && modelField.storage !== null
    ? (modelField.storage as Record<string, unknown>).type
    : undefined;
  if (storageType === 'bool') {
    const selectedValue = typeof value === 'boolean' ? String(value) : undefined;
    return (
      <label key={field.key} className="np-filter-control">
        <span className="sh-form-label">{fieldLabel}</span>
        <Select
          className="np-filter-select"
          value={selectedValue}
          allowClear
          options={[
            { value: 'true', label: '是' },
            { value: 'false', label: '否' },
          ]}
          onChange={(nextValue) => {
            if (nextValue === 'true') {
              onChange(field.key, true);
              return;
            }
            if (nextValue === 'false') {
              onChange(field.key, false);
              return;
            }
            onChange(field.key, '');
          }}
        />
      </label>
    );
  }

  return (
    <label key={field.key} className="np-filter-control">
      <span className="sh-form-label">{fieldLabel}</span>
      <input
        className="sh-form-input"
        value={value == null ? '' : String(value)}
        onChange={(event) => onChange(field.key, event.target.value)}
      />
    </label>
  );
}

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
  fallbackAction: '新建' | '编辑' | '查看',
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
  fallbackAction: '新建' | '编辑' | '查看',
  resourceTitle: string,
  createActionLabel?: string,
): string {
  const normalizedActionLabel = actionLabel?.trim();
  if (normalizedActionLabel) {
    return inferDrawerTitleFromCreateAction(normalizedActionLabel, fallbackAction, createActionLabel) ?? normalizedActionLabel;
  }

  const inferredTitle = inferDrawerTitleFromCreateAction(fallbackAction, fallbackAction, createActionLabel);
  if (inferredTitle) {
    return inferredTitle;
  }

  return `${fallbackAction}${resourceTitle}`;
}

function useOptionalGrantedPermissions(fallbackPermissions: string[] = []): string[] {
  try {
    return useAuthorization().permissions;
  } catch {
    return fallbackPermissions;
  }
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

  return (
    <StandardCrudHostErrorBoundary message={`标准 CRUD 列表元数据解析失败（${viewKey}）`}>
    <StandardCrudListPageContent
      view={viewDocument.view as Record<string, unknown>}
      registration={registration}
      modelDocuments={runtimeMetadata?.modelDocuments ?? []}
    />
    </StandardCrudHostErrorBoundary>
  );
}

function StandardCrudListPageContent<TFilter extends Record<string, unknown>, TRow extends Record<string, unknown>>({
  view,
  registration,
  modelDocuments,
}: {
  view: Record<string, unknown>;
  registration: StandardCrudResourceRegistration<TFilter, TRow>;
  modelDocuments: MetadataRecord[];
}): ReactElement {
  const location = useLocation();
  const { t } = useLocalization();
  const grantedPermissions = useOptionalGrantedPermissions(registration.permissions);
  const schema = useMemo(() => buildStandardCrudListSchema(view, registration, {
    modelDocuments,
  }), [view, registration, modelDocuments]);
  const client = useMemo(() => createStandardCrudClient(schema.apiRoute), [schema.apiRoute]);
  const modelDocument = useMemo(() => findStandardCrudModelDocument(modelDocuments, view.model), [modelDocuments, view]);
  const createFormSchema = useMemo(() => buildStandardCrudFormSchemaFromModel(modelDocument, {
    mode: 'create',
    enumOptions: registration.enumOptions ?? {},
    lookupProviders: registration.lookupProviders ?? [],
  }), [modelDocument, registration.enumOptions, registration.lookupProviders]);
  const editFormSchema = useMemo(() => buildStandardCrudFormSchemaFromModel(modelDocument, {
    mode: 'edit',
    enumOptions: registration.enumOptions ?? {},
    lookupProviders: registration.lookupProviders ?? [],
  }), [modelDocument, registration.enumOptions, registration.lookupProviders]);
  const createFields = useMemo(
    () => localizeStandardCrudFields(resolveCreateFormFields(createFormSchema.fields), schema.dicKey, t),
    [createFormSchema.fields, schema.dicKey, t],
  );
  const editFields = useMemo(
    () => localizeStandardCrudFields(editFormSchema.fields, schema.dicKey, t),
    [editFormSchema.fields, schema.dicKey, t],
  );
  const initialFilters = useMemo(
    () => buildStandardCrudDefaultFilters(schema.query, schema.advancedFilter),
    [schema.query, schema.advancedFilter],
  );
  const [filters, setFilters] = useState<TFilter>(() => initialFilters as TFilter);
  const [appliedFilters, setAppliedFilters] = useState<TFilter>(() => initialFilters as TFilter);
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
  const [advancedFilterOpen, setAdvancedFilterOpen] = useState(false);
  const detailRequestIdRef = useRef(0);
  const handledRouteSearchRef = useRef<string | null>(null);
  const drawerStateRef = useRef(drawerState);
  drawerStateRef.current = drawerState;

  const context = {
    filters,
    setFilters,
    applyFilters: () => setAppliedFilters(filters),
    resetFilters: () => {
      setFilters(initialFilters as TFilter);
      setAppliedFilters(initialFilters as TFilter);
    },
    refresh: () => setReloadTick((current) => current + 1),
    loading,
    rows,
    total,
  };
  const pageTitle = t(`${schema.dicKey}.${schema.titleKey}`, schema.titleKey);
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

  useEffect(() => {
    setFilters(initialFilters as TFilter);
    setAppliedFilters(initialFilters as TFilter);
  }, [initialFilters]);

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
      const mutationFields = drawerState.mode === 'create' ? createFields : editFields;
      const values = registration.buildMutationValues
        ? registration.buildMutationValues(formValues)
        : normalizeStandardCrudSubmitValues(mutationFields, formValues);

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
        buildQuery: () => buildStandardCrudListQuery(
          schema.query,
          appliedFilters as Record<string, unknown>,
          schema.advancedFilter,
        ),
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
    [client, registration, appliedFilters, reloadTick, schema.query, schema.advancedFilter],
  );

  const createAction = schema.pageActions.find((action) => action.standardAction === 'create');
  const editAction = schema.rowActions.find((action) => action.standardAction === 'edit');
  const refreshAction = schema.pageActions.find((action) => action.standardAction === 'refresh');
  const rowActionDefinitions = schema.rowActions;
  const shouldRenderActionColumn = Boolean(
    registration.renderRowActions
    || rowActionDefinitions.length > 0
    || registration.getRowId,
  );
  const registeredColumns = registration.buildColumns?.(context) ?? [];
  const registeredColumnByKey = new Map(registeredColumns.map((column) => [column.key, column]));
  const metadataColumns = schema.columns.map((column) => {
    const registeredColumn = registeredColumnByKey.get(column.key);
    return {
      key: column.key,
      dataIndex: column.field,
      title: t(`${schema.dicKey}.${column.labelKey}`, column.labelKey),
      width: column.width,
      sorter: column.sortable === true,
      render: registeredColumn?.render,
      metadataField: column.field,
    };
  });
  const standardColumns = shouldRenderActionColumn
    ? [
      ...metadataColumns,
      {
        key: 'actions',
        dataIndex: 'actions',
        title: '操作',
        width: undefined,
        sorter: false,
        render: (row: TRow) => {
          const rowId = registration.getRowId(row);
          const rowDeleteActionError = rowActionDefinitions.some((action) => action.standardAction === 'delete' && deleteActionError?.key === `${action.key}:${rowId}`)
            ? deleteActionError
            : null;
          const rowExtraActionError = rowActionDefinitions.some((action) => action.standardAction !== 'delete' && rowActionError?.key === `${action.key}:${rowId}`)
            ? rowActionError
            : null;
          const rowActions: RowActionItem[] = rowActionDefinitions
            .filter((action) => hasActionPermission(action, grantedPermissions))
            .map((action): RowActionItem => {
              const pendingKey = `${action.key}:${rowId}`;
              const label = t(`${schema.dicKey}.${action.labelKey}`, action.labelKey);
              const confirmMessage = action.confirmMessageKey
                ? t(`${schema.dicKey}.${action.confirmMessageKey}`, action.confirmMessageKey)
                : undefined;

              if (action.standardAction === 'view') {
                return {
                  key: action.key,
                  label,
                  onClick: () => {
                    void openViewDrawer(rowId);
                  },
                };
              }
              if (action.standardAction === 'edit') {
                return {
                  key: action.key,
                  label,
                  onClick: () => {
                    void openEditDrawer(rowId);
                  },
                };
              }
              if (action.standardAction === 'delete') {
                return {
                  key: action.key,
                  label,
                  danger: true,
                  confirmMessage,
                  loading: deletingActionKey === pendingKey,
                  disabled: deletingActionKey !== null,
                  onClick: async () => {
                    try {
                      setDeletingActionKey(pendingKey);
                      setDeleteActionError(null);

                      const handler = registration.actionHandlers?.[action.handlerKey ?? action.key];
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
              }

              const handler = registration.actionHandlers?.[action.handlerKey ?? action.key];

              return {
                key: action.key,
                label,
                danger: action.variant === 'danger',
                confirmMessage,
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
            });

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
    : metadataColumns;
  const columns = standardColumns.map((column) => ({
    key: column.key,
    dataIndex: column.dataIndex,
    title: column.title,
    width: column.width,
    sorter: column.sorter,
    render: (_: unknown, row: TRow) => (column.render ? column.render(row) : readTableCell(row, column.dataIndex ?? column.key)),
  }));

  const visiblePageActions = schema.pageActions.filter((action) => hasActionPermission(action, grantedPermissions));
  const headerActions = visiblePageActions.length > 0
    ? (
      <>
        {visiblePageActions.map((action) => {
          if (action.standardAction === 'create') {
            return (
              <AntdButton
                key={action.key}
                type={action.variant === 'primary' ? 'primary' : 'default'}
                size="middle"
                onClick={() => openCreateDrawer()}
              >
                {t(`${schema.dicKey}.${action.labelKey}`, action.labelKey)}
              </AntdButton>
            );
          }

          if (action.standardAction === 'refresh') {
            return (
              <AntdButton
                key={action.key}
                size="middle"
                onClick={() => void handleRefresh()}
              >
                {t(`${schema.dicKey}.${action.labelKey}`, action.labelKey)}
              </AntdButton>
            );
          }

          return null;
        })}
      </>
    )
    : registration.renderHeaderActions ? registration.renderHeaderActions(context) : undefined;

  const filterBar = (
    <CrudSearchBar
      fields={(
        <div className="np-person-filter-grid">
          {schema.query.fields.map((field) => renderMetadataFilterField(
            field,
            modelDocument,
            registration as StandardCrudResourceRegistration<Record<string, unknown>, Record<string, unknown>>,
            filters[field.key],
            (key, value) => setFilters((current) => ({ ...current, [key]: value })),
            (labelKey) => t(`${schema.dicKey}.${labelKey}`, labelKey),
          ))}
        </div>
      )}
      loading={loading}
      onSearch={context.applyFilters}
      onReset={context.resetFilters}
      more={schema.advancedFilter.enabled ? (
        <Popover
          placement="bottomRight"
          trigger="click"
          open={advancedFilterOpen}
          onOpenChange={setAdvancedFilterOpen}
          content={(
            <div className="np-person-filter-grid">
              {schema.advancedFilter.sections.flatMap((section) => section.fields).map((field) => renderMetadataFilterField(
                field,
                modelDocument,
                registration as StandardCrudResourceRegistration<Record<string, unknown>, Record<string, unknown>>,
                filters[field.key],
                (key, value) => setFilters((current) => ({ ...current, [key]: value })),
                (labelKey) => t(`${schema.dicKey}.${labelKey}`, labelKey),
              ))}
            </div>
          )}
        >
          <AntdButton type="text" size="middle">
            {t(`${schema.dicKey}.${schema.advancedFilter.titleKey}`, schema.advancedFilter.titleKey)}
          </AntdButton>
        </Popover>
      ) : undefined}
    />
  );

  const handleRefresh = async () => {
    const handler = refreshAction ? registration.actionHandlers?.[refreshAction.handlerKey ?? refreshAction.key] : undefined;
    if (refreshAction && handler) {
      await handler({ refresh: context.refresh });
      return;
    }

    context.refresh();
  };

  const createActionLabel = createAction ? t(`${schema.dicKey}.${createAction.labelKey}`, createAction.labelKey) : undefined;
  const editActionLabel = editAction ? t(`${schema.dicKey}.${editAction.labelKey}`, editAction.labelKey) : undefined;
  const createDrawerTitle = resolveDrawerTitle(createActionLabel, '新建', pageTitle, createActionLabel);
  const editDrawerTitle = resolveDrawerTitle(editActionLabel, '编辑', pageTitle, createActionLabel);
  const viewDrawerTitle = resolveDrawerTitle(undefined, '查看', pageTitle, createActionLabel);
  const drawerFormId = `${schema.resourceKey}-drawer-form`;

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
      <CrudFormDrawer
        open={drawerState.mode === 'view'}
        title={viewDrawerTitle}
        loading={detailLoading}
        error={detailError}
        primaryLabel="编辑"
        primaryAction={() => {
          if (drawerState.id) {
            void openEditDrawer(drawerState.id);
          }
        }}
        onClose={closeDrawer}
      >
        {!detailError ? (
          <StandardCrudForm
            formId={`${drawerFormId}-view`}
            mode="view"
            fields={editFields}
            sections={editFormSchema.sections}
            values={formValues}
            lookupOptions={lookupOptions}
            lookupLoading={lookupLoading}
            lookupErrors={lookupErrors}
            onChange={handleFormChange}
            onSubmit={() => undefined}
            saving={false}
            showSubmitAction={false}
            titleForSection={(titleKey) => t(`${schema.dicKey}.${titleKey}`, titleKey)}
          />
        ) : null}
      </CrudFormDrawer>
      <CrudFormDrawer
        open={drawerState.mode === 'create' || drawerState.mode === 'edit'}
        title={drawerState.mode === 'create' ? createDrawerTitle : editDrawerTitle}
        saving={saving}
        error={saveError}
        submitFormId={drawerFormId}
        primaryLabel={drawerState.mode === 'create' ? '保存并关闭' : '保存修改'}
        onClose={closeDrawer}
      >
        <StandardCrudForm
          formId={drawerFormId}
          mode={drawerState.mode === 'create' ? 'create' : 'edit'}
          fields={drawerState.mode === 'create' ? createFields : editFields}
          sections={drawerState.mode === 'create' ? createFormSchema.sections : editFormSchema.sections}
          values={formValues}
          lookupOptions={lookupOptions}
          lookupLoading={lookupLoading}
          lookupErrors={lookupErrors}
          onChange={handleFormChange}
          onSubmit={() => void handleDrawerSave()}
          saving={saving}
          error={saveError}
          showSubmitAction={false}
          titleForSection={(titleKey) => t(`${schema.dicKey}.${titleKey}`, titleKey)}
        />
      </CrudFormDrawer>
    </>
  );
}

function readTableCell<TRow extends Record<string, unknown>>(row: TRow, key: string): string {
  const value = row[key];
  return value == null ? '' : String(value);
}
