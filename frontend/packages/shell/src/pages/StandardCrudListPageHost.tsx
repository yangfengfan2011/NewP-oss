import { Component, lazy, Suspense, useEffect, useMemo, useRef, useState, type ErrorInfo, type ReactElement, type ReactNode } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useAuthorization, useLocalization, usePlatformQuery } from '@icp-platform/core';
import { DownOutlined, DownloadOutlined } from '@ant-design/icons';
import {
  ModuleRegistry,
  applyStandardCrudLookupValueChange,
  createStandardCrudOperationResultPresenterRegistry,
} from '@icp-platform/sdk';
import type {
  StandardCrudExportMode,
  StandardCrudDetail,
  StandardCrudFieldDefinition,
  StandardCrudFormSection,
  StandardCrudListQueryRequest,
  StandardCrudOperationResult,
  StandardCrudResourceRegistration,
  StandardCrudRuntimeColumn,
} from '@icp-platform/sdk';
import AntdButton from 'antd/es/button';
import Dropdown from 'antd/es/dropdown';
import Input from 'antd/es/input';
import useMessage from 'antd/es/message/useMessage';
import Select from 'antd/es/select';
import Space from 'antd/es/space';
import Tag from 'antd/es/tag';
import { ErrorState } from '@icp-platform/ui/core';
import {
  CrudSearchBar,
  CrudTableSection,
  CrudWorkspacePage,
  loadStandardCrudColumnPreference,
  saveStandardCrudColumnPreference,
} from '@icp-platform/ui/crud-list';
import { useStandardCrudLookupRuntime } from '../standard-crud/standardCrudLookupRuntime';
import type { SearchTableQueryState, StandardCrudColumnState } from '@icp-platform/ui/crud-list';
import { useAuth } from '../contexts/AuthContext';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import {
  buildStandardCrudListSchema,
  resolveStandardCrudDetailSchema,
  type StandardCrudRuntimeActionSchema,
} from '../metadata/standardCrudSchemaResolver';
import { resolveListViewDocument } from '../metadata/viewMetadataResolver';
import { buildStandardCrudRegistry } from '../registries/standardCrudRegistry';
import { resolveStandardCrudListRoute } from '../navigation/standardCrudRouteAdapter';
import {
  buildStandardCrudListQuery,
  collectStandardCrudListRequestFields,
  parseStandardCrudListState,
  serializeStandardCrudListState,
} from '../standard-crud/standardCrudMetadataQuery';
import type { StandardCrudListState } from '../standard-crud/standardCrudMetadataQuery';
import { createStandardCrudListQueryKey } from '../standard-crud/standardCrudQueryKeys';
import {
  reportStandardCrudDetailDiagnostics,
  resolveStandardCrudDetailPresentation,
} from '../standard-crud/standardCrudDetailPresentation';
import { StandardCrudActionReviewDialog } from '../standard-crud/StandardCrudActionReviewDialog';
import { useStandardCrudActionExecution } from '../standard-crud/useStandardCrudActionExecution';
import { useStandardCrudMutationExecution } from '../standard-crud/useStandardCrudMutationExecution';

type MetadataRecord = Record<string, unknown>;

const loadStandardCrudListOverlayRuntime = () => import('./StandardCrudListOverlayRuntime');
const Drawer = lazy(() => loadStandardCrudListOverlayRuntime().then(({ StandardCrudListDrawer }) => ({
  default: StandardCrudListDrawer,
})));
const Modal = lazy(() => loadStandardCrudListOverlayRuntime().then(({ StandardCrudListModal }) => ({
  default: StandardCrudListModal,
})));
const loadStandardCrudListDrawerRuntime = () => import('./StandardCrudListDrawerRuntime');
const loadStandardCrudListFormRuntime = () => import('./StandardCrudListFormRuntime');
const StandardCrudListDrawerRuntime = lazy(loadStandardCrudListDrawerRuntime);

interface StandardCrudListPageHostProps {
  viewKey: string;
  registration?: StandardCrudResourceRegistration;
}

type CrudDrawerMode = 'closed' | 'create' | 'view' | 'edit';

interface CrudDrawerState {
  mode: CrudDrawerMode;
  id?: string;
  source: 'user-action' | 'route-compatibility';
}

interface StandardCrudDrawerFormSchema {
  fields: StandardCrudFieldDefinition[];
  sections: StandardCrudFormSection[];
}

interface StandardCrudDrawerFormSchemas {
  create: StandardCrudDrawerFormSchema;
  edit: StandardCrudDrawerFormSchema;
}

interface PreparedStandardCrudDrawerForms extends StandardCrudDrawerFormSchemas {
  createFields: StandardCrudFieldDefinition[];
  editFields: StandardCrudFieldDefinition[];
  buildDetailFormValues: (
    detailFields: Record<string, unknown>,
    fields: StandardCrudFieldDefinition[],
  ) => Record<string, unknown>;
}

const EMPTY_STANDARD_CRUD_FORM_SCHEMA: StandardCrudDrawerFormSchema = {
  fields: [],
  sections: [],
};

interface RowActionItem {
  key: string;
  label: ReactNode;
  danger?: boolean;
  disabled?: boolean;
  disabledReason?: string;
  loading?: boolean;
  confirmMessage?: string;
  inline?: boolean;
  onClick: () => void | Promise<void>;
}

interface ResolvedStandardCrudListColumn {
  key: string;
  field: string;
  title: string;
  source: 'model' | 'projection' | 'runtime';
  valueKind?: StandardCrudRuntimeColumn['valueKind'];
  width?: number;
  sortable?: boolean;
  hideable?: boolean;
  responsivePriority?: 'high' | 'medium' | 'low';
  standardAction?: 'view';
}

interface StandardCrudRuntimeColumnGeneration {
  id: number;
  columns: StandardCrudRuntimeColumn[];
  contextToken?: string;
  contextKey: string;
  reloadTick: number;
}

const STANDARD_CRUD_RESERVED_COLUMN_KEYS = new Set(['__rowNumber', 'actions']);
const STANDARD_CRUD_RUNTIME_VALUE_KINDS = new Set(['text', 'number', 'date', 'datetime', 'boolean']);

function validateRuntimeColumnSet(
  value: { columns: StandardCrudRuntimeColumn[]; contextToken?: string },
): { columns: StandardCrudRuntimeColumn[]; contextToken?: string } {
  if (!Array.isArray(value.columns) || value.columns.length === 0) {
    throw new Error('runtime column provider returned an empty column set');
  }
  if (value.contextToken !== undefined && value.contextToken.trim().length === 0) {
    throw new Error('runtime column provider returned an empty context token');
  }

  const keys = new Set<string>();
  const columns = value.columns.map((column, index) => {
    const key = column.key?.trim();
    const field = column.field?.trim();
    const title = column.title?.trim();
    if (!key) throw new Error(`runtime column at index ${index} must declare key`);
    if (STANDARD_CRUD_RESERVED_COLUMN_KEYS.has(key)) throw new Error(`runtime column key "${key}" is reserved`);
    if (keys.has(key)) throw new Error(`runtime column key "${key}" is duplicated`);
    if (!field) throw new Error(`runtime column "${key}" must declare field`);
    if (!title) throw new Error(`runtime column "${key}" must declare title`);
    if (column.valueKind && !STANDARD_CRUD_RUNTIME_VALUE_KINDS.has(column.valueKind)) {
      throw new Error(`runtime column "${key}" has unsupported valueKind "${column.valueKind}"`);
    }
    keys.add(key);
    return { ...column, key, field, title };
  });

  return { columns, ...(value.contextToken ? { contextToken: value.contextToken } : {}) };
}

function renderRuntimeTableCell(row: Record<string, unknown>, column: ResolvedStandardCrudListColumn): ReactNode {
  const value = row[column.field];
  if (value === null || value === undefined || value === '') return '-';
  if (column.valueKind === 'boolean') return value === true ? '是' : value === false ? '否' : String(value);
  if (column.valueKind === 'date') {
    const isoDate = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(value));
    if (isoDate) {
      const year = Number(isoDate[1]);
      const month = Number(isoDate[2]);
      const day = Number(isoDate[3]);
      const localDate = new Date(year, month - 1, day);
      if (localDate.getFullYear() === year && localDate.getMonth() === month - 1 && localDate.getDate() === day) {
        return localDate.toLocaleDateString();
      }
    }
  }
  if (column.valueKind === 'date' || column.valueKind === 'datetime') {
    const date = new Date(String(value));
    if (!Number.isNaN(date.getTime())) {
      return column.valueKind === 'date' ? date.toLocaleDateString() : date.toLocaleString();
    }
  }
  return String(value);
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

function resolveRowActionDisabledReason(
  action: Pick<StandardCrudRuntimeActionSchema, 'disabledWhen'>,
  row: Record<string, unknown>,
  textFor: (key: string) => string,
): string | undefined {
  if (!action.disabledWhen) {
    return undefined;
  }

  const value = readRowActionValue(row, action.disabledWhen.field);
  const isNotEmpty = value !== null && value !== undefined && String(value).trim().length > 0;
  return action.disabledWhen.operator === 'notEmpty' && isNotEmpty
    ? textFor(action.disabledWhen.reasonKey)
    : undefined;
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
  lookupOptions: Record<string, Array<{ value: string; label: string; disabled?: boolean }>>,
  lookupLoading: Record<string, boolean>,
  lookupErrors: Record<string, string | null>,
): ReactElement {
  const fieldLabel = textFor(field.labelKey, field.labelKey);
  const placeholder = textFor(`${field.labelKey}_Placeholder`, '');
  const enumOptions = field.dataSourceKey ? registration.enumOptions?.[field.dataSourceKey] : undefined;
  const providerOptions = field.dataSourceKey ? lookupOptions[field.key] : undefined;
  const usesProvider = Boolean(field.dataSourceKey && registration.lookupProviders?.some(
    (provider) => provider.key === field.dataSourceKey,
  ));
  if (enumOptions || usesProvider) {
    return (
      <label key={field.key} className="np-filter-field">
        <span className="sh-form-label">{fieldLabel}</span>
        <Select
          className="np-filter-select"
          value={typeof value === 'string' && value.length > 0 ? value : undefined}
          allowClear
          placeholder={placeholder}
          loading={lookupLoading[field.key] === true}
          status={lookupErrors[field.key] ? 'error' : undefined}
          options={enumOptions ?? providerOptions ?? []}
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
    const selectedValue = typeof value === 'boolean' || value === 'true' || value === 'false'
      ? String(value)
      : undefined;
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
    metadataSource?: 'model' | 'projection' | 'runtime';
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
  const [confirmAction, setConfirmAction] = useState<RowActionItem | null>(null);
  const inlineActions = actions.filter((action) => action.inline).slice(0, 1);
  const overflowActions = actions.filter((action) => !inlineActions.includes(action));
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

    return button;
  };

  const runOverflowAction = (action: RowActionItem) => {
    if (!action.confirmMessage) {
      void action.onClick();
      return;
    }
    setConfirmAction(action);
  };

  return (
    <div className="np-row-actions">
      {inlineActions.map((action) => renderActionButton(action, 'np-row-action-button'))}
      {overflowActions.length > 0 ? (
        <Dropdown
          trigger={['click']}
          menu={{
            items: overflowActions.map((action) => ({
              key: action.key,
              label: action.loading
                ? '处理中...'
                : action.disabledReason
                  ? `${String(action.label)}（${action.disabledReason}）`
                  : action.label,
              danger: action.danger,
              disabled: action.disabled || action.loading,
            })),
            onClick: ({ key }) => {
              const action = overflowActions.find((item) => item.key === key);
              if (action) runOverflowAction(action);
            },
          }}
        >
          <AntdButton className="np-row-actions-more" type="link">
            更多
          </AntdButton>
        </Dropdown>
      ) : null}
      {confirmAction ? (
        <Suspense fallback={<span role="status">正在加载确认框…</span>}>
          <Modal
            open
            title={confirmAction.confirmMessage}
            okText="确认"
            cancelText="取消"
            confirmLoading={confirmAction.loading}
            okButtonProps={{ danger: confirmAction.danger }}
            destroyOnHidden
            onCancel={() => setConfirmAction(null)}
            onOk={async () => {
              await confirmAction.onClick();
              setConfirmAction(null);
            }}
          />
        </Suspense>
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

export default function StandardCrudListPageHost({
  viewKey,
  registration: loadedRegistration,
}: StandardCrudListPageHostProps): ReactElement {
  const runtimeMetadata = useRuntimeMetadata();
  const viewDocument = resolveListViewDocument(runtimeMetadata?.viewDocuments ?? [], viewKey);
  const registration = loadedRegistration
    ?? buildStandardCrudRegistry(ModuleRegistry.getAllModules()).findByListViewKey(viewKey);
  const listRoute = resolveStandardCrudListRoute(runtimeMetadata?.navDocuments ?? [], viewKey);

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
      viewDocuments={(runtimeMetadata?.viewDocuments ?? []) as unknown as MetadataRecord[]}
      modelDocuments={runtimeMetadata?.modelDocuments ?? []}
      detailRoutePath={listRoute && registration.detailViewKey
        ? `${listRoute.path.replace(/\/$/, '')}/:id`
        : undefined}
    />
    </StandardCrudHostErrorBoundary>
  );
}

function StandardCrudListPageContent<TFilter extends Record<string, unknown>, TRow extends Record<string, unknown>>({
  view,
  registration,
  viewDocuments,
  modelDocuments,
  detailRoutePath,
}: {
  view: Record<string, unknown>;
  registration: StandardCrudResourceRegistration<TFilter, TRow>;
  viewDocuments: MetadataRecord[];
  modelDocuments: MetadataRecord[];
  detailRoutePath?: string;
}): ReactElement {
  const location = useLocation();
  const navigate = useNavigate();
  const [messageApi, messageContextHolder] = useMessage();
  const { t } = useLocalization();
  const currentSubjectId = useOptionalCurrentSubjectId();
  const grantedPermissions = useOptionalGrantedPermissions(registration.permissions);
  const schema = useMemo(() => buildStandardCrudListSchema(view, registration, {
    modelDocuments,
  }), [view, registration, modelDocuments]);
  const dataAdapter = registration.dataAdapter;
  const [runtimeColumnGeneration, setRuntimeColumnGeneration] = useState<StandardCrudRuntimeColumnGeneration | null>(null);
  const runtimeGenerationIdRef = useRef(0);
  const runtimeColumnAbortRef = useRef<AbortController | null>(null);
  const runtimeRowRequestIdRef = useRef(0);
  const resolvedColumns = useMemo<ResolvedStandardCrudListColumn[]>(() => (
    schema.columnSource === 'runtime'
      ? (runtimeColumnGeneration?.columns ?? []).map((column) => ({
          ...column,
          source: 'runtime' as const,
        }))
      : schema.columns.map((column) => ({
          key: column.key,
          field: column.field,
          title: t(`${schema.dicKey}.${column.labelKey}`, column.labelKey),
          source: column.source,
          width: column.width,
          sortable: column.sortable,
          hideable: column.hideable,
          responsivePriority: column.responsivePriority,
          standardAction: column.standardAction,
        }))
  ), [runtimeColumnGeneration?.columns, schema.columnSource, schema.columns, schema.dicKey, t]);
  const operationResultPresenterRegistry = useMemo(
    () => createStandardCrudOperationResultPresenterRegistry(registration.operationResultPresenters ?? []),
    [registration.operationResultPresenters],
  );
  const modelDocument = useMemo(() => findStandardCrudModelDocument(modelDocuments, view.model), [modelDocuments, view]);
  const shouldRenderActionColumn = Boolean(
    registration.renderRowActions
    || schema.rowActions.length > 0
    || dataAdapter.getRowId,
  );
  const defaultColumnState = useMemo<StandardCrudColumnState>(() => Object.fromEntries([
    ...resolvedColumns.map((column, index) => [column.key, {
      order: index,
      ...(column.hideable === false ? { disable: true } : {}),
    }]),
    ...(shouldRenderActionColumn ? [['actions', { order: resolvedColumns.length, disable: true }]] : []),
  ]), [resolvedColumns, shouldRenderActionColumn]);
  const [columnState, setColumnState] = useState<StandardCrudColumnState>(() => (
    typeof window === 'undefined'
      ? defaultColumnState
      : loadStandardCrudColumnPreference(window.localStorage, currentSubjectId, schema.viewKey, defaultColumnState)
  ));
  const [drawerFormSchemas, setDrawerFormSchemas] = useState<StandardCrudDrawerFormSchemas | null>(null);
  const createFormSchema = drawerFormSchemas?.create ?? EMPTY_STANDARD_CRUD_FORM_SCHEMA;
  const editFormSchema = drawerFormSchemas?.edit ?? EMPTY_STANDARD_CRUD_FORM_SCHEMA;
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
      resolvedColumns,
      schema.advancedFilter,
    ),
    [location.search, schema.query, resolvedColumns, schema.advancedFilter],
  );
  const listRequestFields = useMemo(
    () => collectStandardCrudListRequestFields(resolvedColumns, schema.rowActions),
    [resolvedColumns, schema.rowActions],
  );
  const [filters, setFilters] = useState<TFilter>(() => initialListState.filters as TFilter);
  const [appliedFilters, setAppliedFilters] = useState<TFilter>(() => initialListState.filters as TFilter);
  const queryLookupFields = useMemo<StandardCrudFieldDefinition[]>(() => (
    [
      ...schema.query.fields,
      ...schema.advancedFilter.sections.flatMap((section) => section.fields),
    ]
      .filter((field) => field.dataSourceKey && registration.lookupProviders?.some(
        (provider) => provider.key === field.dataSourceKey,
      ))
      .map((field) => ({
        key: field.key,
        labelKey: field.labelKey,
        modelField: field.field ?? field.key,
        kind: 'lookup' as const,
        lookup: {
          providerKey: field.dataSourceKey ?? '',
          dependsOn: field.dependsOn ?? [],
        },
      }))
  ), [registration.lookupProviders, schema.advancedFilter.sections, schema.query.fields]);
  const queryLookupRuntime = useStandardCrudLookupRuntime({
    resourceKey: registration.resourceKey,
    fields: queryLookupFields,
    providers: registration.lookupProviders ?? [],
    values: filters,
  });
  const [tableQuery, setTableQuery] = useState<SearchTableQueryState>(() => ({
    current: initialListState.page,
    pageSize: initialListState.pageSize,
    sortBy: initialListState.sortBy,
    sortDirection: initialListState.sortDirection,
  }));
  const [rows, setRows] = useState<TRow[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [listError, setListError] = useState<string | null>(null);
  const [hasLoaded, setHasLoaded] = useState(false);
  const [reloadTick, setReloadTick] = useState(0);
  const [drawerState, setDrawerState] = useState<CrudDrawerState>(CLOSED_DRAWER_STATE);
  const [detailFields, setDetailFields] = useState<Record<string, unknown>>({});
  const [detailError, setDetailError] = useState<string | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [formValues, setFormValues] = useState<Record<string, unknown>>({});
  const [presentedOperationResult, setPresentedOperationResult] = useState<StandardCrudOperationResult | null>(null);
  const [rowInputAction, setRowInputAction] = useState<{
    action: StandardCrudRuntimeActionSchema;
    row: TRow;
    rowId: string;
    pendingKey: string;
    label: string;
  } | null>(null);
  const [rowInputValue, setRowInputValue] = useState<unknown>(undefined);
  const [discardConfirmOpen, setDiscardConfirmOpen] = useState(false);
  const [advancedFilterOpen, setAdvancedFilterOpen] = useState(false);
  const [exportingMode, setExportingMode] = useState<StandardCrudExportMode | null>(null);
  const exportRequestPendingRef = useRef(false);
  const detailRequestIdRef = useRef(0);
  const handledRouteSearchRef = useRef<string | null>(null);
  const handledListRouteSearchRef = useRef<string | null>(null);
  const drawerStateRef = useRef(drawerState);
  const formValuesRef = useRef(formValues);
  const initialFormValuesRef = useRef<Record<string, unknown>>({});
  const drawerTriggerRef = useRef<HTMLElement | null>(null);
  const discardProceedRef = useRef<(() => void) | null>(null);
  const committedListStateRef = useRef<StandardCrudListState>({
    filters: appliedFilters,
    page: tableQuery.current,
    pageSize: tableQuery.pageSize,
    sortBy: tableQuery.sortBy,
    sortDirection: tableQuery.sortDirection,
  });
  drawerStateRef.current = drawerState;
  formValuesRef.current = formValues;
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
      resolvedColumns,
      schema.advancedFilter,
    );
    const next = { ...defaults, pageSize: tableQuery.pageSize };
    setFilters(next.filters as TFilter);
    syncCommittedListState(next);
  };

  const metadataListRequest = useMemo<StandardCrudListQueryRequest>(() => ({
    ...buildStandardCrudListQuery(
      schema.query,
      appliedFilters as Record<string, unknown>,
      schema.advancedFilter,
    ),
    ...(listRequestFields.length > 0 ? { fields: listRequestFields.join(',') } : {}),
    pageIndex: tableQuery.current,
    pageSize: tableQuery.pageSize,
    ...(tableQuery.sortBy && tableQuery.sortDirection
      ? { sortBy: tableQuery.sortBy, sortDirection: tableQuery.sortDirection }
      : {}),
  }), [appliedFilters, listRequestFields, schema.advancedFilter, schema.query, tableQuery]);
  const metadataListQueryKey = useMemo(
    () => createStandardCrudListQueryKey(registration.resourceKey, metadataListRequest),
    [metadataListRequest, registration.resourceKey],
  );
  const metadataListQuery = usePlatformQuery<{ items: TRow[]; total: number }>({
    key: metadataListQueryKey,
    enabled: schema.columnSource !== 'runtime',
    load: async ({ signal }) => {
      const result = await dataAdapter.list(metadataListRequest, { signal });
      return { items: result.items, total: result.total };
    },
  });
  const metadataListError = metadataListQuery.status === 'error'
    ? metadataListQuery.error instanceof Error ? metadataListQuery.error.message : '加载列表失败'
    : null;
  const effectiveRows = useMemo(
    () => schema.columnSource === 'runtime' ? rows : metadataListQuery.data?.items ?? [],
    [metadataListQuery.data?.items, rows, schema.columnSource],
  );
  const effectiveTotal = schema.columnSource === 'runtime' ? total : metadataListQuery.data?.total ?? 0;
  const effectiveLoading = schema.columnSource === 'runtime'
    ? loading
    : metadataListQuery.status === 'idle' || metadataListQuery.isFetching;
  const effectiveHasLoaded = schema.columnSource === 'runtime'
    ? hasLoaded
    : metadataListQuery.status === 'success' || metadataListQuery.status === 'error';
  const effectiveListError = schema.columnSource === 'runtime' ? listError : metadataListError;

  useEffect(() => {
    if (schema.columnSource === 'runtime' || metadataListQuery.status !== 'success' || !metadataListQuery.data) return;
    const lastPage = Math.max(1, Math.ceil(metadataListQuery.data.total / tableQuery.pageSize));
    if (tableQuery.current <= lastPage) return;
    syncCommittedListState({
      filters: appliedFilters,
      page: lastPage,
      pageSize: tableQuery.pageSize,
      sortBy: tableQuery.sortBy,
      sortDirection: tableQuery.sortDirection,
    }, { replace: true });
    // 结果只负责把越界页归一回合法查询；其他 handler 身份变化不得触发二次导航。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [metadataListQuery.data, metadataListQuery.status, schema.columnSource,
    tableQuery.current, tableQuery.pageSize, tableQuery.sortBy, tableQuery.sortDirection]);

  const context = {
    filters,
    setFilters,
    applyFilters,
    resetFilters,
    refresh: () => {
      if (schema.columnSource === 'runtime') setReloadTick((current) => current + 1);
      else metadataListQuery.refresh();
    },
    loading: effectiveLoading,
    rows: effectiveRows,
    total: effectiveTotal,
  };
  const runtimeColumnContextKey = useMemo(() => JSON.stringify(buildStandardCrudListQuery(
    schema.query,
    appliedFilters as Record<string, unknown>,
    schema.advancedFilter,
  )), [appliedFilters, schema.advancedFilter, schema.query]);

  useEffect(() => {
    if (schema.columnSource !== 'runtime') {
      runtimeColumnAbortRef.current?.abort();
      runtimeColumnAbortRef.current = null;
      setRuntimeColumnGeneration(null);
      return undefined;
    }

    const provider = registration.columnProvider;
    if (!provider) return undefined;
    const loadColumns = provider.load;

    const generationId = runtimeGenerationIdRef.current + 1;
    runtimeGenerationIdRef.current = generationId;
    runtimeRowRequestIdRef.current += 1;
    runtimeColumnAbortRef.current?.abort();
    const abortController = new AbortController();
    runtimeColumnAbortRef.current = abortController;
    setRuntimeColumnGeneration(null);
    setRows([]);
    setTotal(0);
    setListError(null);
    setHasLoaded(false);
    setLoading(true);

    async function resolveRuntimeColumns(): Promise<void> {
      try {
        const result = validateRuntimeColumnSet(await loadColumns({
          filters: appliedFilters,
          signal: abortController.signal,
        }));
        if (abortController.signal.aborted || runtimeGenerationIdRef.current !== generationId) return;

        const normalizedListState = parseStandardCrudListState(
          schema.query,
          new URLSearchParams(location.search),
          result.columns,
          schema.advancedFilter,
        );
        syncCommittedListState({
          ...normalizedListState,
          filters: appliedFilters,
        }, { replace: true });
        setRuntimeColumnGeneration({
          id: generationId,
          columns: result.columns,
          contextToken: result.contextToken,
          contextKey: runtimeColumnContextKey,
          reloadTick,
        });
      } catch (error) {
        if (abortController.signal.aborted || runtimeGenerationIdRef.current !== generationId) return;
        setListError(error instanceof Error ? error.message : '加载运行时列失败');
        setHasLoaded(true);
        setLoading(false);
      }
    }

    void resolveRuntimeColumns();
    return () => abortController.abort();
    // 只有完整代际触发器才能重新解析列；分页和排序由独立行请求复用当前列集与 token。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [registration.columnProvider, reloadTick, runtimeColumnContextKey, schema.columnSource]);

  useEffect(() => {
    if (
      schema.columnSource !== 'runtime'
      || !runtimeColumnGeneration
      || runtimeColumnGeneration.contextKey !== runtimeColumnContextKey
      || runtimeColumnGeneration.reloadTick !== reloadTick
    ) {
      return undefined;
    }

    const requestId = runtimeRowRequestIdRef.current + 1;
    runtimeRowRequestIdRef.current = requestId;
    const generationId = runtimeColumnGeneration.id;
    setLoading(true);
    setListError(null);

    async function loadRuntimeRows(): Promise<void> {
      try {
        const fields = collectStandardCrudListRequestFields(runtimeColumnGeneration!.columns, schema.rowActions);
        const result = await dataAdapter.list({
          ...buildStandardCrudListQuery(
            schema.query,
            appliedFilters as Record<string, unknown>,
            schema.advancedFilter,
          ),
          ...(fields.length > 0 ? { fields: fields.join(',') } : {}),
          pageIndex: tableQuery.current,
          pageSize: tableQuery.pageSize,
          ...(tableQuery.sortBy && tableQuery.sortDirection
            ? { sortBy: tableQuery.sortBy, sortDirection: tableQuery.sortDirection }
            : {}),
        }, runtimeColumnGeneration!.contextToken
          ? { columnContextToken: runtimeColumnGeneration!.contextToken }
          : undefined);
        if (runtimeGenerationIdRef.current !== generationId || runtimeRowRequestIdRef.current !== requestId) return;

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
        setRows(result.items);
        setTotal(result.total);
        setHasLoaded(true);
      } catch (error) {
        if (runtimeGenerationIdRef.current !== generationId || runtimeRowRequestIdRef.current !== requestId) return;
        setRows([]);
        setTotal(0);
        setListError(error instanceof Error ? error.message : '加载列表失败');
        setHasLoaded(true);
      } finally {
        if (runtimeGenerationIdRef.current === generationId && runtimeRowRequestIdRef.current === requestId) {
          setLoading(false);
        }
      }
    }

    void loadRuntimeRows();
    return () => {
      if (runtimeRowRequestIdRef.current === requestId) runtimeRowRequestIdRef.current += 1;
    };
    // 行请求只受本代列集和规范查询驱动；handler 身份变化不得产生额外请求。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [runtimeColumnGeneration, runtimeColumnContextKey, reloadTick, schema.columnSource,
    tableQuery.current, tableQuery.pageSize, tableQuery.sortBy, tableQuery.sortDirection]);

  const actionExecution = useStandardCrudActionExecution<StandardCrudDetail, TRow>({
    refresh: context.refresh,
    onResult: (result) => {
      if (operationResultPresenterRegistry.resolve(result)) {
        setPresentedOperationResult(result);
      }
    },
  });
  const mutationExecution = useStandardCrudMutationExecution({
    registration,
    refresh: context.refresh,
    onResult: (result) => {
      if (operationResultPresenterRegistry.resolve(result)) {
        setPresentedOperationResult(result);
      }
    },
  });
  const pageTitle = t(`${schema.dicKey}.${schema.titleKey}`, schema.titleKey);
  const activeFormFields = drawerState.mode === 'create'
    ? createFields
    : drawerState.mode === 'edit'
      ? editFields
      : [];
  const { lookupOptions, lookupLoading, lookupErrors } = useStandardCrudLookupRuntime({
    resourceKey: registration.resourceKey,
    fields: activeFormFields,
    providers: registration.lookupProviders ?? [],
    values: formValues,
    detailFields,
  });
  const resolvedDrawerDetailSchema = useMemo(() => (
    drawerState.mode === 'view'
      ? resolveStandardCrudDetailSchema(viewDocuments, registration, { modelDocuments })
      : null
  ), [drawerState.mode, modelDocuments, registration, viewDocuments]);
  const drawerDetailPresentation = useMemo(() => (
    drawerState.mode === 'view'
      && drawerState.id
      && resolvedDrawerDetailSchema
      && !detailLoading
      && !detailError
      ? resolveStandardCrudDetailPresentation({
          schema: resolvedDrawerDetailSchema,
          fields: detailFields,
          expectedId: drawerState.id,
        })
      : null
  ), [detailError, detailFields, detailLoading, drawerState.id, drawerState.mode, resolvedDrawerDetailSchema]);

  useEffect(() => {
    if (!resolvedDrawerDetailSchema || !drawerDetailPresentation) return;
    reportStandardCrudDetailDiagnostics(
      resolvedDrawerDetailSchema,
      drawerDetailPresentation.kind === 'fatal'
        ? [drawerDetailPresentation.diagnostic]
        : drawerDetailPresentation.diagnostics,
    );
  }, [drawerDetailPresentation, resolvedDrawerDetailSchema]);

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
    mutationExecution.clearError();
    if (options.syncRoute !== false) {
      clearDrawerRoute();
    }
  };

  const isDrawerDirty = () => (
    (drawerStateRef.current.mode === 'create' || drawerStateRef.current.mode === 'edit')
    && JSON.stringify(formValuesRef.current) !== JSON.stringify(initialFormValuesRef.current)
  );

  // 所有离开编辑态的入口共用同一个 Ant Modal，避免关闭、切换记录和浏览器返回出现不同保护语义。
  const confirmDiscardIfNeeded = (proceed: () => void): void => {
    if (!isDrawerDirty()) {
      proceed();
      return;
    }
    discardProceedRef.current = proceed;
    setDiscardConfirmOpen(true);
  };

  const requestCloseDrawer = () => confirmDiscardIfNeeded(() => closeDrawer());

  const restoreDrawerTriggerFocus = (open: boolean) => {
    if (!open) {
      drawerTriggerRef.current?.focus();
      drawerTriggerRef.current = null;
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

  const prepareDrawerForms = async (): Promise<PreparedStandardCrudDrawerForms> => {
    const runtime = await loadStandardCrudListFormRuntime();
    const nextSchemas = {
      create: runtime.buildStandardCrudFormSchemaFromModel(modelDocument, {
        mode: 'create',
        enumOptions: registration.enumOptions ?? {},
        lookupProviders: registration.lookupProviders ?? [],
      }),
      edit: runtime.buildStandardCrudFormSchemaFromModel(modelDocument, {
        mode: 'edit',
        enumOptions: registration.enumOptions ?? {},
        lookupProviders: registration.lookupProviders ?? [],
      }),
    };
    setDrawerFormSchemas(nextSchemas);

    return {
      ...nextSchemas,
      createFields: localizeStandardCrudFields(
        resolveCreateFormFields(nextSchemas.create.fields),
        schema.dicKey,
        t,
      ),
      editFields: localizeStandardCrudFields(nextSchemas.edit.fields, schema.dicKey, t),
      buildDetailFormValues: runtime.buildStandardCrudDetailFormValues,
    };
  };

  const openCreateDrawer = async (source: CrudDrawerState['source'] = 'user-action') => {
    const prepared = await prepareDrawerForms();
    invalidateDetailRequests();
    const initialValues = Object.fromEntries(prepared.createFields.map((field) => [field.key, field.defaultValue ?? '']));
    if (source === 'user-action') drawerTriggerRef.current = document.activeElement as HTMLElement | null;
    setDetailFields({});
    setDetailLoading(false);
    setFormValues(initialValues);
    initialFormValuesRef.current = initialValues;
    setDetailError(null);
    mutationExecution.clearError();
    setDrawerState({ mode: 'create', source });
    if (source === 'user-action') {
      syncDrawerRoute('create');
    }
  };

  const openViewDrawer = async (
    id: string,
    source: CrudDrawerState['source'] = 'user-action',
    options: { syncRoute?: boolean; targetMode?: 'view' | 'edit' } = {},
  ): Promise<boolean> => {
    const requestId = detailRequestIdRef.current + 1;
    detailRequestIdRef.current = requestId;
    setDrawerState({ mode: options.targetMode ?? 'view', id, source });
    setDetailFields({});
    if (source === 'user-action') drawerTriggerRef.current = document.activeElement as HTMLElement | null;
    if (source === 'user-action' && options.syncRoute !== false) {
      syncDrawerRoute('view', id);
    }
    setDetailLoading(true);
    setDetailError(null);
    mutationExecution.clearError();
    try {
      const [loadedDetail, prepared] = await Promise.all([
        dataAdapter.get(id),
        prepareDrawerForms(),
      ]);
      if (detailRequestIdRef.current !== requestId) {
        return false;
      }

      const nextFormValues = prepared.buildDetailFormValues(loadedDetail.normalized.fields, prepared.editFields);
      setDetailFields(loadedDetail.normalized.fields);
      setFormValues(nextFormValues);
      initialFormValuesRef.current = nextFormValues;
      return true;
    } catch (loadError) {
      if (detailRequestIdRef.current !== requestId) {
        return false;
      }

      setDetailFields({});
      setFormValues({});
      setDetailError(loadError instanceof Error ? loadError.message : '加载详情失败');
      if (options.targetMode === 'edit') {
        setDrawerState({ mode: 'view', id, source });
        syncDrawerRoute('view', id);
      }
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
    const loaded = await openViewDrawer(id, source, { syncRoute: false, targetMode: 'edit' });
    if (!loaded) {
      return;
    }
  };

  const handleDrawerSave = async () => {
    const succeeded = await mutationExecution.execute({
      mode: drawerState.mode === 'create' ? 'create' : 'edit',
      id: drawerState.id,
      fields: drawerState.mode === 'create' ? createFields : editFields,
      values: formValues,
      detailFields,
    });
    if (succeeded) {
      closeDrawer();
    }
  };

  const handleFormChange = (fieldKey: string, value: unknown) => {
    const fields = drawerState.mode === 'create' ? createFields : editFields;
    setFormValues((current) => applyStandardCrudLookupValueChange(current, fieldKey, value, fields));
    if (mutationExecution.fieldErrors[fieldKey] || mutationExecution.conflict) {
      mutationExecution.clearError();
    }
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
      void openCreateDrawer('route-compatibility');
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
      if (isDrawerDirty()) {
        const current = drawerStateRef.current;
        const searchWithoutDrawer = location.search;
        confirmDiscardIfNeeded(() => {
          closeDrawer({ syncRoute: false });
          handledRouteSearchRef.current = searchWithoutDrawer;
          navigate({ pathname: location.pathname, search: searchWithoutDrawer }, { replace: true });
        });
        if (current.mode !== 'closed') syncDrawerRoute(current.mode, current.id);
      } else {
        closeDrawer({ syncRoute: false });
      }
    }
    // URL 是抽屉兼容状态的唯一触发源，handler 身份变化不应重复打开或关闭抽屉。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.search]);

  // SearchTablePage 仍保留旧 request Interface；controlledResult 生效时该兼容函数不会执行远程读取。
  const tableRequest = useMemo(() => async () => ({
    data: effectiveRows,
    total: effectiveTotal,
    success: effectiveListError === null,
  }), [effectiveListError, effectiveRows, effectiveTotal]);

  const createAction = schema.pageActions.find((action) => action.standardAction === 'create');
  const editAction = schema.rowActions.find((action) => action.standardAction === 'edit');
  const rowActionDefinitions = schema.rowActions;
  const metadataColumns = resolvedColumns.map((column) => {
    const renderColumn = column.standardAction === 'view'
      ? (row: TRow) => (
        <AntdButton
          className="np-standard-crud-cell-action"
          type="link"
          onClick={() => confirmDiscardIfNeeded(() => { void openViewDrawer(dataAdapter.getRowId(row)); })}
        >
          {String(readRowActionValue(row, column.field) ?? '-')}
        </AntdButton>
      )
      : undefined;
    return {
      key: column.key,
      dataIndex: column.field,
      title: column.title,
      width: column.width,
      fixed: undefined,
      sorter: column.sortable === true,
      responsive: resolveResponsiveBreakpoints(column.responsivePriority),
      render: renderColumn,
      metadataField: column.field,
      metadataSource: column.source,
      runtimeValueKind: column.valueKind,
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
        metadataField: undefined,
        metadataSource: undefined,
        runtimeValueKind: undefined,
        render: (row: TRow) => {
          const rowId = dataAdapter.getRowId(row);
          const rowExecutionError = rowActionDefinitions.some((action) => actionExecution.error?.key === `${action.key}:${rowId}`)
            ? actionExecution.error
            : null;
          const rowActions: RowActionItem[] = rowActionDefinitions
            .filter((action) => hasActionPermission(action, grantedPermissions))
            .filter((action) => isRowActionVisibleForRow(action, row as Record<string, unknown>))
            .map((action) => ({
              action,
              actionState: registration.actionStateResolver?.({ action, row }),
            }))
            .filter(({ actionState }) => actionState?.visible !== false)
            .map(({ action, actionState }): RowActionItem => {
              const pendingKey = `${action.key}:${rowId}`;
              const label = t(`${schema.dicKey}.${action.labelKey}`, action.labelKey);
              const metadataDisabledReason = resolveRowActionDisabledReason(
                action,
                row as Record<string, unknown>,
                (key) => t(`${schema.dicKey}.${key}`, key),
              );
              const disabledReason = actionState?.available === false
                ? actionState.reason ?? '当前状态不允许执行该操作。'
                : metadataDisabledReason;
              const confirmMessage = action.confirmMessageKey
                ? t(`${schema.dicKey}.${action.confirmMessageKey}`, action.confirmMessageKey)
                : undefined;
              const handler = registration.actionHandlers?.[action.handlerKey ?? action.key];
              const inputRenderer = action.inputKey ? registration.actionInputRenderers?.[action.inputKey] : undefined;

              if (action.standardAction === 'view') {
                return {
                  key: action.key,
                  label,
                  onClick: () => confirmDiscardIfNeeded(() => { void openViewDrawer(rowId); }),
                };
              }
              if (action.standardAction === 'edit') {
                return {
                  key: action.key,
                  label,
                  inline: true,
                  onClick: () => confirmDiscardIfNeeded(() => { void openEditDrawer(rowId); }),
                };
              }
              if (action.standardAction === 'delete') {
                if (inputRenderer) {
                  return {
                    key: action.key,
                    label,
                    danger: true,
                    loading: actionExecution.pendingKey === pendingKey,
                    disabled: actionExecution.pendingKey !== null || !handler || disabledReason !== undefined,
                    disabledReason,
                    onClick: async () => {
                      actionExecution.clearError();
                      setRowInputValue(undefined);
                      setRowInputAction({ action, row, rowId, pendingKey, label });
                    },
                  };
                }
                return {
                  key: action.key,
                  label,
                  danger: isDangerRowAction(action),
                  confirmMessage,
                  loading: actionExecution.pendingKey === pendingKey,
                  disabled: actionExecution.pendingKey !== null || disabledReason !== undefined,
                  disabledReason,
                  onClick: async () => {
                    const deleteHandler = handler ?? (async () => {
                      if (!dataAdapter.remove) {
                        throw new Error(`标准资源 "${registration.resourceKey}" 未提供删除能力。`);
                      }
                      await dataAdapter.remove(rowId);
                    });
                    const outcome = await actionExecution.execute({
                      key: pendingKey,
                      handler: deleteHandler,
                      context: { id: rowId, row },
                      fallbackErrorMessage: '删除标准资源失败',
                    });
                    if (outcome.status === 'succeeded') {
                      closeDrawerForRow(rowId);
                    }
                  },
                };
              }

              return {
                key: action.key,
                label,
                danger: isDangerRowAction(action),
                confirmMessage: inputRenderer ? undefined : confirmMessage,
                loading: actionExecution.pendingKey === pendingKey,
                disabled: actionExecution.pendingKey !== null || !handler || disabledReason !== undefined,
                disabledReason,
                onClick: async () => {
                  if (!handler) {
                    return;
                  }

                  if (inputRenderer) {
                    actionExecution.clearError();
                    setRowInputValue(undefined);
                    setRowInputAction({ action, row, rowId, pendingKey, label });
                    return;
                  }

                  await actionExecution.execute({
                    key: pendingKey,
                    handler,
                    context: { id: rowId, row },
                  });
                },
              };
            });

          return (
            <div style={{ display: 'grid', gap: 6 }}>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'flex-start' }}>
                <RowActionButtonGroup actions={rowActions} />
                {registration.renderRowActions ? registration.renderRowActions(row, context) : null}
              </div>
              {rowExecutionError ? (
                <span role="alert" style={{ color: 'var(--sh-color-danger, #dc2626)', fontSize: 12 }}>
                  {rowExecutionError.message}
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
      : column.metadataSource === 'runtime'
        ? renderRuntimeTableCell(row, {
            key: column.key,
            field: String(column.dataIndex),
            title: String(column.title),
            source: 'runtime',
            valueKind: column.runtimeValueKind,
          })
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
      if (!dataAdapter.exportList) {
        throw new Error(`标准资源 "${registration.resourceKey}" 未提供导出能力。`);
      }
      const response = await dataAdapter.exportList({
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
      void messageApi.error(exportError instanceof Error ? exportError.message : '导出标准列表失败');
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
            onClick={() => void openCreateDrawer()}
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
            (key, value) => setFilters((current) => applyStandardCrudLookupValueChange(
              current,
              key,
              value,
              queryLookupFields,
            ) as TFilter),
            (labelKey, fallback) => t(`${schema.dicKey}.${labelKey}`, fallback ?? labelKey),
            queryLookupRuntime.lookupOptions,
            queryLookupRuntime.lookupLoading,
            queryLookupRuntime.lookupErrors,
          ))}
        </div>
      )}
      loading={effectiveLoading}
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
  const drawerRow = drawerState.id
    ? effectiveRows.find((row) => dataAdapter.getRowId(row) === drawerState.id)
    : undefined;
  const drawerEditActionState = editAction && drawerDetailPresentation?.kind === 'ready'
    ? registration.actionStateResolver?.({
        action: editAction,
        detail: { fields: drawerDetailPresentation.sourceFields, summaries: {} },
        row: drawerRow,
      })
    : undefined;
  const canEdit = Boolean(
    editAction
    && drawerDetailPresentation?.kind === 'ready'
    && hasActionPermission(editAction, grantedPermissions)
    && drawerEditActionState?.visible !== false
    && drawerEditActionState?.available !== false,
  );

  const reloadConflictedRecord = async () => {
    if (drawerState.mode !== 'edit' || !drawerState.id) return;
    const id = drawerState.id;
    const loaded = await openViewDrawer(id, 'route-compatibility', { syncRoute: false, targetMode: 'edit' });
    if (loaded) {
      setDrawerState({ mode: 'edit', id, source: 'route-compatibility' });
      mutationExecution.clearError();
    }
  };

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
        rowKey={dataAdapter.getRowId}
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
        loading={effectiveLoading}
        controlledResult={{
          data: effectiveRows,
          total: effectiveTotal,
          loading: effectiveLoading,
          hasLoaded: effectiveHasLoaded,
          error: effectiveListError,
          onRetry: schema.columnSource === 'runtime'
            ? () => setReloadTick((current) => current + 1)
            : metadataListQuery.retry,
        }}
        toolbar={exportToolbar}
        onRefresh={() => void handleRefresh()}
      />
    </div>
  );

  return (
    <>
      {messageContextHolder}
      {presentedOperationResult
        ? operationResultPresenterRegistry.resolve(presentedOperationResult)?.render({
            result: presentedOperationResult,
            close: () => setPresentedOperationResult(null),
          })
        : null}
      <CrudWorkspacePage
        title={pageTitle}
        primaryActions={headerActions}
        filterBar={filterBar}
      >
        {content}
      </CrudWorkspacePage>
      {schema.advancedFilter.enabled && advancedFilterOpen ? (
        <Suspense fallback={<div className="sh-loading-state" role="status">正在加载高级筛选…</div>}>
          <Drawer
            open
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
                    (key, value) => setFilters((current) => applyStandardCrudLookupValueChange(
                      current,
                      key,
                      value,
                      queryLookupFields,
                    ) as TFilter),
                    (labelKey, fallback) => t(`${schema.dicKey}.${labelKey}`, fallback ?? labelKey),
                    queryLookupRuntime.lookupOptions,
                    queryLookupRuntime.lookupLoading,
                    queryLookupRuntime.lookupErrors,
                  ))}
                </div>
              </section>
            ))}
            </div>
          </Drawer>
        </Suspense>
      ) : null}
      {drawerState.mode !== 'closed' ? (
        <Suspense fallback={<div className="sh-loading-state" role="status">正在加载操作面板…</div>}>
          <StandardCrudListDrawerRuntime
            mode={drawerState.mode}
            viewTitle={viewDrawerTitle}
            mutationTitle={drawerState.mode === 'create' ? createDrawerTitle : editDrawerTitle}
            detailLoading={detailLoading}
            detailError={detailError}
            saving={mutationExecution.pending}
            saveError={mutationExecution.error}
            saveFieldErrors={mutationExecution.fieldErrors}
            formId={drawerFormId}
            fields={drawerState.mode === 'create' ? createFields : editFields}
            sections={drawerState.mode === 'create' ? createFormSchema.sections : editFormSchema.sections}
            detailPresentation={drawerDetailPresentation}
            registration={registration}
            values={formValues}
            lookupOptions={lookupOptions}
            lookupLoading={lookupLoading}
            lookupErrors={lookupErrors}
            canEdit={canEdit}
            viewSecondaryAction={drawerState.id && detailRoutePath ? (
              <AntdButton
                onClick={() => {
                  const fullDetailPath = detailRoutePath.replace(':id', encodeURIComponent(drawerState.id ?? ''));
                  if (fullDetailPath) navigate(fullDetailPath);
                }}
              >
                {t(`${schema.dicKey}.Action_ViewFullDetail`, '查看完整详情')}
              </AntdButton>
            ) : undefined}
            saveErrorActions={mutationExecution.conflict ? (
              <AntdButton onClick={() => void reloadConflictedRecord()} disabled={mutationExecution.pending}>
                重新加载当前数据
              </AntdButton>
            ) : undefined}
            onChange={handleFormChange}
            onEdit={() => {
              if (drawerState.id) void openEditDrawer(drawerState.id);
            }}
            onSubmit={() => void handleDrawerSave()}
            onClose={requestCloseDrawer}
            afterOpenChange={restoreDrawerTriggerFocus}
            titleForSection={(titleKey) => t(`${schema.dicKey}.${titleKey}`, titleKey)}
            textForDetail={(key, fallback) => t(`${resolvedDrawerDetailSchema?.dicKey ?? schema.dicKey}.${key}`, fallback)}
          />
        </Suspense>
      ) : null}
      {rowInputAction?.action.inputKey && registration.actionInputRenderers?.[rowInputAction.action.inputKey] ? (
        <Suspense fallback={<div className="sh-loading-state" role="status">正在加载操作确认…</div>}>
          <StandardCrudActionReviewDialog
            action={rowInputAction.action}
            title={rowInputAction.label}
            confirmMessage={rowInputAction.action.confirmMessageKey
              ? t(
                  `${schema.dicKey}.${rowInputAction.action.confirmMessageKey}`,
                  rowInputAction.action.confirmMessageKey,
                )
              : undefined}
            inputRenderer={registration.actionInputRenderers[rowInputAction.action.inputKey]}
            value={rowInputValue}
            row={rowInputAction.row}
            pending={actionExecution.pendingKey === rowInputAction.pendingKey}
            onChange={setRowInputValue}
            onCancel={() => {
              if (actionExecution.pendingKey === null) setRowInputAction(null);
            }}
            onConfirm={() => {
              const { action, row, rowId, pendingKey } = rowInputAction;
              const handler = registration.actionHandlers?.[action.handlerKey ?? action.key];
              if (!handler) return;
              void (async () => {
                const outcome = await actionExecution.execute({
                  key: pendingKey,
                  handler,
                  context: { id: rowId, row, input: rowInputValue },
                });
                if (outcome.status === 'succeeded') {
                  setRowInputAction(null);
                } else if (outcome.status === 'failed' && outcome.conflict) {
                  setRowInputAction(null);
                }
              })();
            }}
          />
        </Suspense>
      ) : null}
      {discardConfirmOpen ? (
        <Suspense fallback={<div className="sh-loading-state" role="status">正在加载放弃确认…</div>}>
          <Modal
            open
            title="放弃未保存的更改？"
            okText="放弃更改"
            cancelText="继续编辑"
            okButtonProps={{ danger: true }}
            destroyOnHidden
            onCancel={() => {
              discardProceedRef.current = null;
              setDiscardConfirmOpen(false);
            }}
            onOk={() => {
              const proceed = discardProceedRef.current;
              discardProceedRef.current = null;
              setDiscardConfirmOpen(false);
              initialFormValuesRef.current = formValuesRef.current;
              proceed?.();
            }}
          >
            当前修改尚未保存，继续将丢失这些内容。
          </Modal>
        </Suspense>
      ) : null}
    </>
  );
}
