import { Component, useEffect, useMemo, useRef, useState, type ErrorInfo, type ReactElement, type ReactNode } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { platformFetch, useAuthorization, useLocalization } from '@icp-platform/core';
import { DownOutlined, DownloadOutlined } from '@ant-design/icons';
import {
  ModuleRegistry,
  applyStandardCrudLookupValueChange,
  createStandardCrudClient,
  normalizeStandardCrudListResult,
  normalizeStandardCrudSubmitValues,
  useStandardCrudLookupRuntime,
} from '@icp-platform/sdk';
import type {
  StandardCrudExportMode,
  StandardCrudFieldDefinition,
  StandardCrudListQueryRequest,
  StandardCrudResourceRegistration,
} from '@icp-platform/sdk';
import { Button as AntdButton, Drawer, Dropdown, Input, Popconfirm, Popover, Select, Space, Tag, message } from 'antd';
import {
  CrudFormDrawer,
  CrudSearchBar,
  CrudTableSection,
  CrudWorkspacePage,
  ErrorState,
  StandardCrudForm,
  createSearchTableRequest,
  loadStandardCrudColumnPreference,
  saveStandardCrudColumnPreference,
} from '@icp-platform/ui';
import type { SearchTableQueryState, StandardCrudColumnState } from '@icp-platform/ui';
import { useAuth } from '../contexts/AuthContext';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { buildStandardCrudListSchema, type StandardCrudRuntimeActionSchema } from '../metadata/standardCrudSchemaResolver';
import { resolveListViewDocument } from '../metadata/viewMetadataResolver';
import { buildStandardCrudRegistry } from '../registries/standardCrudRegistry';
import { buildStandardCrudFormSchemaFromModel } from '../standard-crud/standardCrudMetadataForm';
import {
  buildStandardCrudListQuery,
  collectStandardCrudListRequestFields,
  parseStandardCrudListState,
  serializeStandardCrudListState,
} from '../standard-crud/standardCrudMetadataQuery';
import type { StandardCrudListState } from '../standard-crud/standardCrudMetadataQuery';
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

const STANDARD_CRUD_INLINE_ACTION_LIMIT = 3;
const STANDARD_CRUD_ACTION_COLUMN_WIDTH = 176;
function readRowActionValue(row: Record<string, unknown>, field: string): unknown {
  return row[field];
}

// 行状态可见性只在 Host 投影阶段解释，metadata 仍只声明静态约束。
function isRowActionVisibleForRow(
  action: Pick<StandardCrudRuntimeActionSchema, 'visibleWhen'>,
  row: Record<string, unknown>,
): boolean {
  if (!action.visibleWhen) {
    return true;
  }
  if (action.visibleWhen.operator !== 'equals') {
    return false;
  }

  return readRowActionValue(row, action.visibleWhen.field) === action.visibleWhen.value;
}

function isDangerRowAction(action: Pick<StandardCrudRuntimeActionSchema, 'standardAction' | 'variant'>): boolean {
  return action.standardAction === 'delete' || action.variant === 'danger';
}

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
  return fields.map((field) => {
    const placeholder = t(`${dicKey}.${field.labelKey}_Placeholder`, '');

    return {
      ...field,
      labelKey: t(`${dicKey}.${field.labelKey}`, field.labelKey),
      placeholder: placeholder.length > 0 ? placeholder : field.placeholder,
    };
  });
}

function readMetadataRecord(value: unknown): MetadataRecord {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as MetadataRecord) : {};
}

function readModelFieldStorageType(modelField: MetadataRecord): string {
  const storage = readMetadataRecord(modelField.storage);
  return typeof storage.type === 'string' ? storage.type : '';
}

function readModelFieldEnumKey(modelField: MetadataRecord): string {
  const dataSource = readMetadataRecord(modelField.dataSource);
  const contract = readMetadataRecord(modelField.contract);
  const dataSourceType = typeof dataSource.type === 'string' ? dataSource.type : '';
  if (dataSourceType !== 'enum' && contract.valueType !== 'enum') {
    return '';
  }

  if (typeof dataSource.key === 'string' && dataSource.key.length > 0) {
    return dataSource.key;
  }
  if (typeof contract.clrType === 'string' && contract.clrType.length > 0) {
    return contract.clrType;
  }

  return '';
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
  textFor: (labelKey: string, fallback?: string) => string,
): ReactElement {
  const fieldLabel = textFor(field.labelKey, field.labelKey);
  const placeholder = textFor(`${field.labelKey}_Placeholder`, '');
  const enumOptions = field.dataSourceKey ? registration.enumOptions?.[field.dataSourceKey] : undefined;
  if (enumOptions) {
    return (
      <label key={field.key} className="np-filter-field">
        <span className="sh-form-label">{fieldLabel}</span>
        <Select
          className="np-filter-select"
          value={typeof value === 'string' && value.length > 0 ? value : undefined}
          allowClear
          placeholder={placeholder}
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
      <label key={field.key} className="np-filter-field">
        <span className="sh-form-label">{fieldLabel}</span>
        <Select
          className="np-filter-select"
          value={selectedValue}
          allowClear
          placeholder={placeholder}
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
    <label key={field.key} className="np-filter-field">
      <span className="sh-form-label">{fieldLabel}</span>
      <Input
        className="np-filter-control"
        value={value == null ? '' : String(value)}
        placeholder={placeholder}
        onChange={(event) => onChange(field.key, event.target.value)}
      />
    </label>
  );
}

function readTableCellValue<TRow extends Record<string, unknown>>(row: TRow, key: string): unknown {
  return row[key];
}

function renderBooleanTableCell(value: unknown): ReactElement | string {
  if (typeof value !== 'boolean') {
    return value == null ? '' : String(value);
  }

  return (
    <Tag color={value ? 'success' : 'default'} bordered={false}>
      {value ? '启用' : '停用'}
    </Tag>
  );
}

function renderMetadataTableCell<TRow extends Record<string, unknown>>(
  row: TRow,
  column: {
    key: string;
    dataIndex?: string;
    metadataField?: string;
    metadataSource?: 'model' | 'projection';
  },
  modelDocument: MetadataRecord,
  registration: StandardCrudResourceRegistration<Record<string, unknown>, Record<string, unknown>>,
): ReactNode {
  const value = readTableCellValue(row, column.dataIndex ?? column.key);
  if (column.metadataSource !== 'model') {
    return value == null ? '' : String(value);
  }

  const modelField = findStandardCrudModelField(modelDocument, column.metadataField);
  const enumKey = readModelFieldEnumKey(modelField);
  if (enumKey.length > 0) {
    const option = registration.enumOptions?.[enumKey]?.find((item) => item.value === value);
    return option?.label ?? (value == null ? '' : String(value));
  }

  if (readModelFieldStorageType(modelField) === 'bool') {
    return renderBooleanTableCell(value);
  }

  return value == null ? '' : String(value);
}

function RowActionButtonGroup({ actions }: { actions: RowActionItem[] }) {
  const inlineCandidates = actions.filter((action) => !action.danger);
  const dangerActions = actions.filter((action) => action.danger);
  const hasOverflow = dangerActions.length > 0 || inlineCandidates.length > STANDARD_CRUD_INLINE_ACTION_LIMIT;
  const inlineLimit = hasOverflow ? STANDARD_CRUD_INLINE_ACTION_LIMIT - 1 : STANDARD_CRUD_INLINE_ACTION_LIMIT;
  const inlineActions = inlineCandidates.slice(0, inlineLimit);
  const overflowActions = [
    ...inlineCandidates.slice(inlineLimit),
    ...dangerActions,
  ];
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

function useOptionalCurrentSubjectId(): string {
  try {
    return useAuth().user?.id ?? 'anonymous';
  } catch {
    return 'anonymous';
  }
}

function resolveResponsiveBreakpoints(priority: 'high' | 'medium' | 'low' | undefined): Array<'xs' | 'sm' | 'md' | 'lg' | 'xl' | 'xxl'> | undefined {
  if (priority === 'high') {
    return ['xs', 'sm', 'md', 'lg', 'xl', 'xxl'];
  }
  if (priority === 'medium') {
    return ['md', 'lg', 'xl', 'xxl'];
  }
  if (priority === 'low') {
    return ['lg', 'xl', 'xxl'];
  }
  return undefined;
}

function hasActionPermission(action: { requiredPermissions?: string[] }, grantedPermissions: string[] = []): boolean {
  return (action.requiredPermissions ?? []).every((permission) => grantedPermissions.includes(permission));
}

function formatTimestampForFileName(date = new Date()): string {
  const pad = (value: number) => String(value).padStart(2, '0');
  return [
    date.getFullYear(),
    pad(date.getMonth() + 1),
    pad(date.getDate()),
    pad(date.getHours()),
    pad(date.getMinutes()),
    pad(date.getSeconds()),
  ].join('');
}

function downloadStandardCrudBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
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
  const navigate = useNavigate();
  const { t } = useLocalization();
  const currentSubjectId = useOptionalCurrentSubjectId();
  const grantedPermissions = useOptionalGrantedPermissions(registration.permissions);
  const schema = useMemo(() => buildStandardCrudListSchema(view, registration, {
    modelDocuments,
  }), [view, registration, modelDocuments]);
  const client = useMemo(() => createStandardCrudClient(schema.apiRoute, platformFetch), [schema.apiRoute]);
  const modelDocument = useMemo(() => findStandardCrudModelDocument(modelDocuments, view.model), [modelDocuments, view]);
  const shouldRenderActionColumn = Boolean(
    registration.renderRowActions
    || schema.rowActions.length > 0
    || registration.getRowId,
  );
  const defaultColumnState = useMemo<StandardCrudColumnState>(() => Object.fromEntries([
    ...schema.columns.map((column, index) => [column.key, {
      order: index,
      ...(column.hideable === false ? { disable: true } : {}),
    }]),
    ...(shouldRenderActionColumn ? [['actions', { order: schema.columns.length, disable: true }]] : []),
  ]), [schema.columns, shouldRenderActionColumn]);
  const [columnState, setColumnState] = useState<StandardCrudColumnState>(() => (
    typeof window === 'undefined'
      ? defaultColumnState
      : loadStandardCrudColumnPreference(window.localStorage, currentSubjectId, schema.viewKey, defaultColumnState)
  ));
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
  const initialListState = useMemo(
    () => parseStandardCrudListState(
      schema.query,
      new URLSearchParams(location.search),
      schema.columns,
      schema.advancedFilter,
    ),
    [location.search, schema.query, schema.columns, schema.advancedFilter],
  );
  const listRequestFields = useMemo(
    () => collectStandardCrudListRequestFields(schema.columns, schema.rowActions),
    [schema.columns, schema.rowActions],
  );
  const [filters, setFilters] = useState<TFilter>(() => initialListState.filters as TFilter);
  const [appliedFilters, setAppliedFilters] = useState<TFilter>(() => initialListState.filters as TFilter);
  const [tableQuery, setTableQuery] = useState<SearchTableQueryState>(() => ({
    current: initialListState.page,
    pageSize: initialListState.pageSize,
    sortBy: initialListState.sortBy,
    sortDirection: initialListState.sortDirection,
  }));
  const [rows, setRows] = useState<TRow[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
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
  const [exportingMode, setExportingMode] = useState<StandardCrudExportMode | null>(null);
  const exportRequestPendingRef = useRef(false);
  const detailRequestIdRef = useRef(0);
  const handledRouteSearchRef = useRef<string | null>(null);
  const handledListRouteSearchRef = useRef<string | null>(null);
  const drawerStateRef = useRef(drawerState);
  const committedListStateRef = useRef<StandardCrudListState>({
    filters: appliedFilters,
    page: tableQuery.current,
    pageSize: tableQuery.pageSize,
    sortBy: tableQuery.sortBy,
    sortDirection: tableQuery.sortDirection,
  });
  drawerStateRef.current = drawerState;
  committedListStateRef.current = {
    filters: appliedFilters,
    page: tableQuery.current,
    pageSize: tableQuery.pageSize,
    sortBy: tableQuery.sortBy,
    sortDirection: tableQuery.sortDirection,
  };

  useEffect(() => {
    setColumnState(typeof window === 'undefined'
      ? defaultColumnState
      : loadStandardCrudColumnPreference(window.localStorage, currentSubjectId, schema.viewKey, defaultColumnState));
  }, [currentSubjectId, schema.viewKey, defaultColumnState]);

  const syncCommittedListState = (
    next: StandardCrudListState,
    options: { replace?: boolean } = {},
  ) => {
    committedListStateRef.current = next;
    setAppliedFilters(next.filters as TFilter);
    setTableQuery({
      current: next.page,
      pageSize: next.pageSize,
      sortBy: next.sortBy,
      sortDirection: next.sortDirection,
    });
    const params = serializeStandardCrudListState(
      schema.query,
      next,
      new URLSearchParams(location.search),
      schema.advancedFilter,
    );
    const search = params.toString().length > 0 ? `?${params.toString()}` : '';
    if (search !== location.search) {
      handledListRouteSearchRef.current = search;
      navigate({ pathname: location.pathname, search }, { replace: options.replace === true });
    }
  };

  const applyFilters = () => {
    syncCommittedListState({
      filters,
      page: 1,
      pageSize: tableQuery.pageSize,
      sortBy: tableQuery.sortBy,
      sortDirection: tableQuery.sortDirection,
    });
  };

  const resetFilters = () => {
    const defaults = parseStandardCrudListState(
      schema.query,
      new URLSearchParams(),
      schema.columns,
      schema.advancedFilter,
    );
    const next = { ...defaults, pageSize: tableQuery.pageSize };
    setFilters(next.filters as TFilter);
    syncCommittedListState(next);
  };

  const context = {
    filters,
    setFilters,
    applyFilters,
    resetFilters,
    refresh: () => setReloadTick((current) => current + 1),
    loading,
    rows,
    total,
  };
  const pageTitle = t(`${schema.dicKey}.${schema.titleKey}`, schema.titleKey);
  // 查看抽屉同样依赖 lookup 当前值标签；只有关闭状态才不启动 provider，避免只读字段退化为原始 ID。
  const activeFormFields = drawerState.mode === 'create' ? createFields : drawerState.mode === 'closed' ? [] : editFields;
  const { lookupOptions, lookupLoading, lookupErrors } = useStandardCrudLookupRuntime({
    fields: activeFormFields,
    providers: registration.lookupProviders ?? [],
    values: formValues,
    detailFields,
  });

  const invalidateDetailRequests = () => {
    detailRequestIdRef.current += 1;
  };

  const buildDrawerSearch = (mode: Exclude<CrudDrawerMode, 'closed'>, id?: string): string => {
    const params = new URLSearchParams(location.search);
    params.set('drawer', mode);
    if (id) {
      params.set('id', id);
    } else {
      params.delete('id');
    }

    const search = params.toString();
    return search.length > 0 ? `?${search}` : '';
  };

  const clearDrawerSearch = (): string => {
    const params = new URLSearchParams(location.search);
    params.delete('drawer');
    params.delete('id');
    const search = params.toString();
    return search.length > 0 ? `?${search}` : '';
  };

  const syncDrawerRoute = (mode: Exclude<CrudDrawerMode, 'closed'>, id?: string) => {
    const search = buildDrawerSearch(mode, id);
    if (search === location.search) {
      return;
    }

    handledRouteSearchRef.current = search;
    navigate({ pathname: location.pathname, search });
  };

  const clearDrawerRoute = () => {
    if (!location.search.includes('drawer=') && !location.search.includes('id=')) {
      return;
    }

    const search = clearDrawerSearch();
    handledRouteSearchRef.current = search;
    navigate({ pathname: location.pathname, search }, { replace: true });
  };

  const closeDrawer = (options: { syncRoute?: boolean } = {}) => {
    invalidateDetailRequests();
    setDetailLoading(false);
    setDrawerState(CLOSED_DRAWER_STATE);
    if (options.syncRoute !== false) {
      clearDrawerRoute();
    }
  };

  useEffect(() => {
    if (handledListRouteSearchRef.current === location.search) {
      handledListRouteSearchRef.current = null;
      return;
    }

    const current = committedListStateRef.current;
    const currentFilters = buildStandardCrudListQuery(schema.query, current.filters, schema.advancedFilter);
    const nextFilters = buildStandardCrudListQuery(schema.query, initialListState.filters, schema.advancedFilter);
    if (
      JSON.stringify(currentFilters) === JSON.stringify(nextFilters)
      && current.page === initialListState.page
      && current.pageSize === initialListState.pageSize
      && current.sortBy === initialListState.sortBy
      && current.sortDirection === initialListState.sortDirection
    ) {
      return;
    }

    committedListStateRef.current = initialListState;
    setFilters(initialListState.filters as TFilter);
    setAppliedFilters(initialListState.filters as TFilter);
    setTableQuery({
      current: initialListState.page,
      pageSize: initialListState.pageSize,
      sortBy: initialListState.sortBy,
      sortDirection: initialListState.sortDirection,
    });
  }, [initialListState, location.search, schema.query, schema.advancedFilter]);

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
    if (source === 'user-action') {
      syncDrawerRoute('create');
    }
  };

  const openViewDrawer = async (
    id: string,
    source: CrudDrawerState['source'] = 'user-action',
    options: { syncRoute?: boolean } = {},
  ): Promise<boolean> => {
    const requestId = detailRequestIdRef.current + 1;
    detailRequestIdRef.current = requestId;
    setDrawerState({ mode: 'view', id, source });
    if (source === 'user-action' && options.syncRoute !== false) {
      syncDrawerRoute('view', id);
    }
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
    if (source === 'user-action') {
      syncDrawerRoute('edit', id);
    }
    const loaded = await openViewDrawer(id, source, { syncRoute: false });
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
      return;
    }
    if (drawer === 'view' && id) {
      void openViewDrawer(id, 'route-compatibility');
      return;
    }
    if (drawer === 'edit' && id) {
      void openEditDrawer(id, 'route-compatibility');
      return;
    }
    if (!drawer) {
      closeDrawer({ syncRoute: false });
    }
    // URL 是抽屉兼容状态的唯一触发源，handler 身份变化不应重复打开或关闭抽屉。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.search]);

  const tableRequest = useMemo(
    () =>
      createSearchTableRequest<Record<string, unknown>, TRow, TRow>({
        buildQuery: () => ({
          ...buildStandardCrudListQuery(
            schema.query,
            appliedFilters as Record<string, unknown>,
            schema.advancedFilter,
          ),
          ...(listRequestFields.length > 0 ? { fields: listRequestFields.join(',') } : {}),
        }),
        load: async (query) => {
          const rawResult = await client.list(query as StandardCrudListQueryRequest);
          const result = normalizeStandardCrudListResult(rawResult, registration.mapListItem);

          return {
            items: result.items,
            total: result.total,
          };
        },
        mapRow: (row) => row,
      }),
    // reloadTick 是显式请求失效令牌，即使不进入闭包也必须重建 request。
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [client, registration, appliedFilters, reloadTick, schema.query, schema.advancedFilter, listRequestFields],
  );

  const createAction = schema.pageActions.find((action) => action.standardAction === 'create');
  const editAction = schema.rowActions.find((action) => action.standardAction === 'edit');
  const refreshAction = schema.pageActions.find((action) => action.standardAction === 'refresh');
  const rowActionDefinitions = schema.rowActions;
  const registeredColumns = registration.buildColumns?.(context) ?? [];
  const registeredColumnByKey = new Map(registeredColumns.map((column) => [column.key, column]));
  const metadataColumns = schema.columns.map((column) => {
    const registeredColumn = registeredColumnByKey.get(column.key);
    return {
      key: column.key,
      dataIndex: column.field,
      title: t(`${schema.dicKey}.${column.labelKey}`, column.labelKey),
      width: column.width,
      fixed: undefined,
      sorter: column.sortable === true,
      responsive: resolveResponsiveBreakpoints(column.responsivePriority),
      render: registeredColumn?.render,
      metadataField: column.field,
      metadataSource: column.source,
    };
  });
  const exportColumns = schema.columns
    .filter((column) => columnState[column.key]?.show !== false)
    .sort((left, right) => (columnState[left.key]?.order ?? 0) - (columnState[right.key]?.order ?? 0))
    .map((column) => ({
      key: column.key,
      field: column.field,
      title: t(`${schema.dicKey}.${column.labelKey}`, column.labelKey),
    }));
  const standardColumns = shouldRenderActionColumn
    ? [
      ...metadataColumns,
      {
        key: 'actions',
        dataIndex: 'actions',
        title: '操作',
        width: STANDARD_CRUD_ACTION_COLUMN_WIDTH,
        fixed: 'right' as const,
        sorter: false,
        responsive: undefined,
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
            .filter((action) => isRowActionVisibleForRow(action, row as Record<string, unknown>))
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
                  danger: isDangerRowAction(action),
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
                danger: isDangerRowAction(action),
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
    fixed: column.fixed,
    sorter: column.sorter,
    responsive: column.responsive,
    render: (_: unknown, row: TRow) => (column.render
      ? column.render(row)
      : renderMetadataTableCell(
        row,
        column,
        modelDocument,
        registration as StandardCrudResourceRegistration<Record<string, unknown>, Record<string, unknown>>,
      )),
  }));

  const visiblePageActions = schema.pageActions.filter((action) => hasActionPermission(action, grantedPermissions));
  const visibleHeaderActions = visiblePageActions.filter((action) => action.standardAction === 'create');
  const handleExport = async (mode: StandardCrudExportMode) => {
    if (exportRequestPendingRef.current) {
      return;
    }

    try {
      exportRequestPendingRef.current = true;
      setExportingMode(mode);
      const filtersQuery = buildStandardCrudListQuery(
        schema.query,
        appliedFilters as Record<string, unknown>,
        schema.advancedFilter,
      );
      const response = await client.exportList({
        mode,
        columns: exportColumns,
        query: {
          pageIndex: tableQuery.current,
          pageSize: tableQuery.pageSize,
          sortBy: tableQuery.sortBy,
          sortDirection: tableQuery.sortDirection,
          filters: filtersQuery,
        },
      });
      const fallbackModeLabel = mode === 'current' ? '当前页' : '全部';
      downloadStandardCrudBlob(
        response.blob,
        response.fileName ?? `${pageTitle}-${fallbackModeLabel}-${formatTimestampForFileName()}.xlsx`,
      );
    } catch (exportError) {
      void message.error(exportError instanceof Error ? exportError.message : '导出标准列表失败');
    } finally {
      exportRequestPendingRef.current = false;
      setExportingMode(null);
    }
  };
  const exportToolbar = schema.exportModes.length > 0 ? (
    <Dropdown
      trigger={['click']}
      disabled={exportingMode !== null}
      menu={{
        items: [
          ...(schema.exportModes.includes('current') ? [{ key: 'current', label: t(`${schema.dicKey}.Export_Current`, '导出本页') }] : []),
          ...(schema.exportModes.includes('all') ? [{ key: 'all', label: t(`${schema.dicKey}.Export_All`, '导出筛选结果') }] : []),
        ],
        onClick: ({ key }) => void handleExport(key as StandardCrudExportMode),
      }}
    >
      <AntdButton
        size="small"
        icon={<DownloadOutlined />}
        loading={exportingMode !== null}
        disabled={exportingMode !== null}
      >
        导出 <DownOutlined />
      </AntdButton>
    </Dropdown>
  ) : undefined;
  const headerActions = visibleHeaderActions.length > 0
    ? (
      <>
        {visibleHeaderActions.map((action) => (
          <AntdButton
            key={action.key}
            type={action.variant === 'primary' ? 'primary' : 'default'}
            size="middle"
            onClick={() => openCreateDrawer()}
          >
            {t(`${schema.dicKey}.${action.labelKey}`, action.labelKey)}
          </AntdButton>
        ))}
      </>
    )
    : schema.pageActions.length === 0 && registration.renderHeaderActions
      ? registration.renderHeaderActions(context)
      : undefined;

  const filterBar = (
    <CrudSearchBar
      fields={(
        <div className="np-standard-crud-filter-grid">
          {schema.query.fields.map((field) => renderMetadataFilterField(
            field,
            modelDocument,
            registration as StandardCrudResourceRegistration<Record<string, unknown>, Record<string, unknown>>,
            filters[field.key],
            (key, value) => setFilters((current) => ({ ...current, [key]: value })),
            (labelKey, fallback) => t(`${schema.dicKey}.${labelKey}`, fallback ?? labelKey),
          ))}
        </div>
      )}
      loading={loading}
      onSearch={context.applyFilters}
      onReset={context.resetFilters}
      more={schema.advancedFilter.enabled ? (
        <AntdButton type="text" size="middle" onClick={() => setAdvancedFilterOpen(true)}>
          {t(`${schema.dicKey}.${schema.advancedFilter.titleKey}`, schema.advancedFilter.titleKey)}
        </AntdButton>
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
  const createDrawerTitle = t(
    `${schema.dicKey}.Drawer_Create`,
    resolveDrawerTitle(createActionLabel, '新建', pageTitle, createActionLabel),
  );
  const editDrawerTitle = t(
    `${schema.dicKey}.Drawer_Edit`,
    resolveDrawerTitle(editActionLabel, '编辑', pageTitle, createActionLabel),
  );
  const viewDrawerTitle = t(
    `${schema.dicKey}.Drawer_View`,
    resolveDrawerTitle(undefined, '查看', pageTitle, createActionLabel),
  );
  const drawerFormId = `${schema.resourceKey}-drawer-form`;

  const handleTableQueryChange = (nextQuery: SearchTableQueryState) => {
    const sortBy = nextQuery.sortBy ?? schema.query.defaultSort?.field;
    const sortDirection = nextQuery.sortDirection ?? (sortBy ? schema.query.defaultSort?.direction ?? 'asc' : undefined);
    syncCommittedListState({
      filters: appliedFilters,
      page: nextQuery.current,
      pageSize: nextQuery.pageSize,
      sortBy,
      sortDirection,
    });
  };

  const handleTableResult = (result: { data: TRow[]; total: number; success: boolean }) => {
    const lastPage = Math.max(1, Math.ceil(result.total / tableQuery.pageSize));
    if (tableQuery.current > lastPage) {
      syncCommittedListState({
        filters: appliedFilters,
        page: lastPage,
        pageSize: tableQuery.pageSize,
        sortBy: tableQuery.sortBy,
        sortDirection: tableQuery.sortDirection,
      }, { replace: true });
      return;
    }

    setRows(result.data);
    setTotal(result.total);
  };

  const content = (
    <div className="np-standard-crud-table">
      <CrudTableSection<TRow>
        rowKey={registration.getRowId}
        columns={columns}
        request={tableRequest}
        query={tableQuery}
        onQueryChange={handleTableQueryChange}
        onLoadResult={handleTableResult}
        onLoadError={() => {
          setRows([]);
          setTotal(0);
        }}
        onLoadingChange={setLoading}
        columnState={columnState}
        defaultColumnState={defaultColumnState}
        onColumnStateChange={(next) => {
          const protectedState = Object.fromEntries(Object.entries(defaultColumnState).map(([key, defaultItem]) => [
            key,
            {
              ...(next[key] ?? defaultItem),
              ...(defaultItem.disable === true ? { disable: true, show: true } : {}),
            },
          ]));
          setColumnState(protectedState);
          if (typeof window !== 'undefined') {
            saveStandardCrudColumnPreference(window.localStorage, currentSubjectId, schema.viewKey, protectedState);
          }
        }}
        filteredEmpty={Object.values(appliedFilters).some((value) => value !== undefined && value !== null && value !== '')}
        loading={loading}
        toolbar={exportToolbar}
        onRefresh={() => void handleRefresh()}
      />
    </div>
  );

  return (
    <>
      <CrudWorkspacePage
        title={pageTitle}
        primaryActions={headerActions}
        filterBar={filterBar}
      >
        {content}
      </CrudWorkspacePage>
      {schema.advancedFilter.enabled ? (
        <Drawer
          open={advancedFilterOpen}
          title={t(`${schema.dicKey}.${schema.advancedFilter.titleKey}`, schema.advancedFilter.titleKey)}
          width={420}
          placement="right"
          destroyOnClose
          onClose={() => setAdvancedFilterOpen(false)}
        >
          <div className="np-advanced-filter-drawer">
            <Space className="np-advanced-filter-drawer-actions">
              <AntdButton
                onClick={() => {
                  context.resetFilters();
                }}
                disabled={loading}
              >
                {t(`${schema.dicKey}.Action_ResetAdvanced`, '重置条件')}
              </AntdButton>
              <AntdButton
                type="primary"
                loading={loading}
                onClick={() => {
                  context.applyFilters();
                  setAdvancedFilterOpen(false);
                }}
              >
                {t(`${schema.dicKey}.Action_ApplyAdvanced`, '应用筛选')}
              </AntdButton>
            </Space>
            {schema.advancedFilter.sections.map((section) => (
              <section key={section.key} className="np-advanced-filter-section">
                <h3 className="np-advanced-filter-section-title">
                  {t(`${schema.dicKey}.${section.titleKey}`, section.titleKey)}
                </h3>
                <div className="np-advanced-filter-grid">
                  {section.fields.map((field) => renderMetadataFilterField(
                    field,
                    modelDocument,
                    registration as StandardCrudResourceRegistration<Record<string, unknown>, Record<string, unknown>>,
                    filters[field.key],
                    (key, value) => setFilters((current) => ({ ...current, [key]: value })),
                    (labelKey, fallback) => t(`${schema.dicKey}.${labelKey}`, fallback ?? labelKey),
                  ))}
                </div>
              </section>
            ))}
          </div>
        </Drawer>
      ) : null}
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
