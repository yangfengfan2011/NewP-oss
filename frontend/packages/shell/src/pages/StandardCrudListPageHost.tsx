import { useFeaturePlacement } from '../authorization/FeaturePlacementContext';
import { resolvedFeaturePosition, type FeatureResolution } from '../authorization/featurePlacementResolver';
import { Component, lazy, Suspense, useContext, useEffect, useMemo, useRef, useState, useSyncExternalStore, type ErrorInfo, type ReactElement, type ReactNode } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { formatRequestError, useLocalization, usePlatformQuery } from '@icp-platform/core';
import { DownOutlined, DownloadOutlined } from '@ant-design/icons';
import {
  ModuleRegistry,
  isStandardCrudRecord,
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
  StandardCrudActionState,
} from '@icp-platform/sdk';
import AntdButton from 'antd/es/button';
import Dropdown from 'antd/es/dropdown';
import Input from 'antd/es/input';
import useMessage from 'antd/es/message/useMessage';
import Select from 'antd/es/select';
import Space from 'antd/es/space';
import Tag from 'antd/es/tag';
import { ErrorState } from '@icp-platform/ui/core';
import { CrudLocaleProvider, StandardCrudField } from '@icp-platform/ui/crud';
import {
  CrudSearchBar,
  CrudTableSection,
  CrudWorkspacePage,
  ContextActionMenu,
  isNativeContextMenuTarget,
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
  type StandardCrudPageActionGroupSchema,
} from '../metadata/standardCrudSchemaResolver';
import { resolveListViewDocument } from '../metadata/viewMetadataResolver';
import { buildStandardCrudRegistry } from '../registries/standardCrudRegistry';
import { hasStandardCrudFullDetailPage, resolveExplicitDetailRoute, resolveStandardCrudListRoute } from '../navigation/standardCrudRouteAdapter';
import {
  buildStandardCrudListQuery,
  collectStandardCrudListRequestFields,
  parseStandardCrudListState,
  serializeStandardCrudListState,
} from '../standard-crud/standardCrudMetadataQuery';
import type { StandardCrudListState } from '../standard-crud/standardCrudMetadataQuery';
import {
  createStandardCrudListQueryKey,
  createStandardCrudRuntimeColumnsQueryKey,
  createStandardCrudRuntimeRowsQueryKey,
} from '../standard-crud/standardCrudQueryKeys';
import {
  readStandardCrudFieldDisplayValue,
  reportStandardCrudDetailDiagnostics,
  resolveStandardCrudDetailPresentation,
} from '../standard-crud/standardCrudDetailPresentation';
import { useStandardCrudDetailQuery } from '../standard-crud/standardCrudDetailQuery';
import { useStandardCrudEditSession } from '../standard-crud/useStandardCrudEditSession';
import { StandardCrudActionReviewDialog } from '../standard-crud/StandardCrudActionReviewDialog';
import { StandardCrudDialogTitle } from '../standard-crud/StandardCrudDialogTitle';
import { useLocalizedStandardCrudRegistration } from '../standard-crud/useLocalizedStandardCrudRegistration';
import { useStandardCrudActionExecution } from '../standard-crud/useStandardCrudActionExecution';
import { formatStandardCrudActionReason } from '../standard-crud/standardCrudActionReason';
import { useStandardCrudMutationExecution } from '../standard-crud/useStandardCrudMutationExecution';
import { useStandardCrudHostEnvironment, type StandardCrudHostAddress } from '../standard-crud/standardCrudHostEnvironment';

import { ActionInstanceContext, TreeNodeActivationContext } from '../composition/pageCompositionRuntime';
const noInstanceSubscription = () => () => undefined;
const noInstanceSnapshot = () => 0;
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
  onClick: () => void | boolean | Promise<void | boolean>;
  eligibility?: {
    resourceKey: string;
    objectId: string;
    featurePosition: string;
    resolve: (signal: AbortSignal) => Promise<StandardCrudActionState>;
  };
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
}

const STANDARD_CRUD_RESERVED_COLUMN_KEYS = new Set(['__rowNumber', 'actions']);
const STANDARD_CRUD_RUNTIME_VALUE_KINDS = new Set(['text', 'number', 'date', 'datetime', 'boolean']);
let nextRuntimeColumnGenerationId = 0;

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

function renderRuntimeTableCell(row: Record<string, unknown>, column: ResolvedStandardCrudListColumn, t: ReturnType<typeof useLocalization>['t']): ReactNode {
  const value = row[column.field];
  if (value === null || value === undefined || value === '') return '-';
  if (column.valueKind === 'boolean') return value === true ? t('Platform.Yes', '是') : value === false ? t('Platform.No', '否') : String(value);
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

function hasActiveFilterValue(value: unknown): boolean {
  if (Array.isArray(value)) {
    return value.some(hasActiveFilterValue);
  }

  return value !== undefined && value !== null && value !== '';
}

function renderMetadataFilterField(
  field: {
    key: string;
    labelKey: string;
    dataSourceKey?: string;
    field?: string;
    operator?: string;
  },
  modelDocument: MetadataRecord,
  registration: StandardCrudResourceRegistration<Record<string, unknown>, Record<string, unknown>>,
  value: unknown,
  onChange: (key: string, value: unknown) => void,
  textFor: (labelKey: string, fallback?: string) => string,
  lookupOptions: Record<string, Array<{ value: string; label: string; disabled?: boolean }>>,
  lookupLoading: Record<string, boolean>,
  lookupErrors: Record<string, string | null>,
  t: ReturnType<typeof useLocalization>['t'],
): ReactElement {
  const fieldLabel = textFor(field.labelKey, field.labelKey);
  const placeholder = textFor(`${field.labelKey}_Placeholder`, '');
  const enumOptions = field.dataSourceKey ? registration.enumOptions?.[field.dataSourceKey] : undefined;
  const providerOptions = field.dataSourceKey ? lookupOptions[field.key] : undefined;
  const usesProvider = Boolean(field.dataSourceKey && registration.lookupProviders?.some(
    (provider) => provider.key === field.dataSourceKey,
  ));
  if (field.operator === 'dateRange') {
    return (
      <StandardCrudField
        key={field.key}
        field={{
          key: field.key,
          labelKey: fieldLabel,
          modelField: field.field ?? field.key,
          kind: 'dateRange',
          placeholder,
        }}
        value={value}
        mode="edit"
        onChange={onChange}
      />
    );
  }
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
            { value: 'true', label: t('Platform.Yes', '是') },
            { value: 'false', label: t('Platform.No', '否') },
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

function BooleanTableCell({ value }: { value: unknown }): ReactElement | string {
  const { t } = useLocalization();
  if (typeof value !== 'boolean') {
    return value == null ? '' : String(value);
  }

  return (
    <Tag color={value ? 'success' : 'default'} bordered={false}>
      {value ? t('Platform.Enabled', '启用') : t('Platform.Disabled', '停用')}
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
  const fieldKey = column.dataIndex ?? column.key;
  const value = readStandardCrudFieldDisplayValue(row, fieldKey);
  const renderer = registration.listFieldRenderers?.[fieldKey];
  if (renderer) return renderer({ row, value });
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
    return <BooleanTableCell value={value} />;
  }

  return value == null ? '' : String(value);
}

function RowActionButtonGroup({ actions, onConfirmRequested, requested = false, active = true, close, contextMenu, closeContextMenu, contextKey = '', compact = false }: {
  actions: RowActionItem[]; onConfirmRequested: (key: string) => void; requested?: boolean; active?: boolean; close?: () => void;
  contextMenu?: { actionKeys: string[]; point: { x: number; y: number; focusTarget: HTMLElement } };
  closeContextMenu?: () => void;
  contextKey?: string;
  compact?: boolean;
}) {
  const { t } = useLocalization();
  const requestedOnce = useRef(false);
  const [openedContext, setOpenedContext] = useState<string | null>(null);
  const menuOpen = openedContext === contextKey;
  useEffect(() => setOpenedContext(null), [contextKey]);
  const menuExecutable = useRef(false);
  menuExecutable.current = active && (menuOpen || Boolean(contextMenu));
  const currentActions = useRef(actions); currentActions.current = actions;
  const [eligibilityEpoch, setEligibilityEpoch] = useState(0);
  const inlineActions = compact ? [] : actions.filter((action) => action.inline && !action.eligibility).slice(0, 1);
  const overflowActions = contextMenu ? contextMenu.actionKeys.flatMap(key => actions.filter(action => action.key === key))
    : actions.filter((action) => !inlineActions.includes(action));
  const qualifiedActions = (contextMenu ? overflowActions : actions).filter((action) => action.eligibility);
  const contextPoint = contextMenu?.point;
  useEffect(() => { if (contextPoint) setEligibilityEpoch(current => current + 1); }, [contextPoint]);
  const eligibility = usePlatformQuery<Record<string, StandardCrudActionState>>({
    key: ['standard-crud-row-action-eligibility',
      JSON.stringify(qualifiedActions.map((item) => [item.eligibility?.resourceKey,
        item.eligibility?.objectId, item.key, item.eligibility?.featurePosition])), eligibilityEpoch],
    enabled: active && (menuOpen || Boolean(contextMenu) || requested) && qualifiedActions.length > 0,
    load: async ({ signal }) => {
      const states: Record<string, StandardCrudActionState> = {};
      // 只检查用户展开的一行；关闭菜单或行卸载后由查询生命周期取消未完成请求。
      for (const item of qualifiedActions) {
        const qualification = item.eligibility!;
        states[item.key] = await qualification.resolve(signal);
      }
      return states;
    },
  });
  const visibleOverflowActions = overflowActions.filter((action) => !action.eligibility
    || (eligibility.status === 'success' && eligibility.data?.[action.key]?.visible));
  const actionReason = (action: RowActionItem | undefined) => action
    ? formatStandardCrudActionReason(action.eligibility ? eligibility.data?.[action.key] : undefined, t) ?? action.disabledReason
    : undefined;
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
        {action.loading ? t('Platform.Processing', '处理中...') : action.label}
      </AntdButton>
    );

    return button;
  };

  const runOverflowAction = (action: RowActionItem) => {
    const current = currentActions.current.find(item => item.key === action.key);
    if (!menuExecutable.current || !current || current.disabled || current.loading) return;
    if (action.eligibility && (eligibility.status !== 'success'
      || !eligibility.data?.[action.key]?.visible || !eligibility.data[action.key]?.available)) return;
    if (!action.confirmMessage) {
      void action.onClick();
      return;
    }
    onConfirmRequested(action.key);
  };

  useEffect(() => {
    if (!requested || !active || requestedOnce.current || !actions[0]) return;
    if (actions[0].eligibility && eligibility.status !== 'success' && eligibility.status !== 'error') return;
    requestedOnce.current = true;
    if (!actions[0].disabled && (!actions[0].eligibility
      || (eligibility.status === 'success' && eligibility.data?.[actions[0].key]?.visible
        && eligibility.data[actions[0].key]?.available))) {
      if (actions[0].confirmMessage) onConfirmRequested(actions[0].key);
      else void actions[0].onClick();
    }
    // 一个内嵌动作实例只请求一次；后续 pending/错误渲染不能重放写入。
  }, [requested, active, actions, eligibility.status, eligibility.data, onConfirmRequested]);

  return (
    <div className="np-row-actions">
      {requested && (!actions[0] || (actions[0].disabled && !actions[0].loading)
        || (actions[0].eligibility && (eligibility.status === 'error'
          || (eligibility.status === 'success' && !eligibility.data?.[actions[0].key]?.available))))
        && <div role="alert">{eligibility.status === 'error' ? t('Platform.EligibilityUnavailable', '对象动作资格暂时无法判定。')
          : actionReason(actions[0]) ?? t('Platform.ActionUnavailable', '当前操作不可用。')}<AntdButton onClick={close}>{t('Platform.Close', '关闭')}</AntdButton></div>}
      {!requested && inlineActions.map((action) => renderActionButton(action, 'np-row-action-button'))}
      {!requested && overflowActions.length > 0 ? (
        <ContextActionMenu
          trigger={['click']}
          open={active && (menuOpen || Boolean(contextMenu))}
          point={contextMenu?.point}
          onOpenChange={(open) => {
            if (open && !menuOpen) setEligibilityEpoch((current) => current + 1);
            closeContextMenu?.();
            setOpenedContext(open ? contextKey : null);
          }}
          menu={{
            items: [...visibleOverflowActions.map((action) => {
              const reason = actionReason(action);
              return {
                key: action.key,
                label: action.loading
                  ? t('Platform.Processing', '处理中...')
                  : reason
                    ? `${String(action.label)}（${reason}）`
                    : action.label,
                danger: action.danger,
                disabled: action.disabled || action.loading
                  || (action.eligibility && eligibility.data?.[action.key]?.available === false),
              };
            }), ...(qualifiedActions.length > 0 && eligibility.status === 'pending'
              ? [{ key: '__eligibility_pending', label: t('Platform.CheckingEligibility', '正在检查对象操作资格…'), disabled: true }]
              : []), ...(qualifiedActions.length > 0 && eligibility.status === 'error'
              ? [{ key: '__eligibility_error', label: t('Platform.EligibilityUnavailable', '对象动作资格暂时无法判定。'), disabled: true }]
              : [])],
            onClick: ({ key }) => {
              const action = visibleOverflowActions.find((item) => item.key === key);
              if (action) runOverflowAction(action);
            },
          }}
        >
          <AntdButton className="np-row-actions-more" type="link" aria-label={t('Platform.More', '更多')} title={t('Platform.More', '更多')} onClick={contextMenu ? () => { closeContextMenu?.(); setOpenedContext(contextKey); } : undefined}>
            {compact ? <span aria-hidden="true" style={{ fontSize: 20, lineHeight: 1 }}>⋯</span> : t('Platform.More', '更多')}
          </AntdButton>
        </ContextActionMenu>
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


function useOptionalCurrentSubjectId(): string {
  try {
    return useAuth().user?.id ?? 'anonymous';
  } catch {
    return 'anonymous';
  }
}

function useOptionalSessionTimeZoneId(): string | undefined {
  try {
    return useAuth().user?.timeZoneId;
  } catch {
    return undefined;
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
  const { t } = useLocalization();
  const runtimeMetadata = useRuntimeMetadata();
  const viewDocument = resolveListViewDocument(runtimeMetadata?.viewDocuments ?? [], viewKey);
  const registration = loadedRegistration
    ?? buildStandardCrudRegistry(ModuleRegistry.getAllModules()).findByListViewKey(viewKey);
  const listRoute = resolveStandardCrudListRoute(runtimeMetadata?.navDocuments ?? [], viewKey);

  if (!viewDocument) {
    return <div className="sh-error-state">{t('Platform.ListMetadataMissing', '未找到列表视图元数据')}</div>;
  }

  if (!registration) {
    return <div className="sh-error-state">{t('Platform.CrudRegistrationMissing', '未找到标准 CRUD 前端资源注册项')}</div>;
  }

  return (
    <StandardCrudHostErrorBoundary message={`标准 CRUD 列表元数据解析失败（${viewKey}）`}>
    <CrudLocaleProvider><StandardCrudListPageContent
      view={viewDocument.view as Record<string, unknown>}
      registration={registration}
      viewDocuments={(runtimeMetadata?.viewDocuments ?? []) as unknown as MetadataRecord[]}
      modelDocuments={runtimeMetadata?.modelDocuments ?? []}
      detailRoutePath={registration.detailViewKey
        && hasStandardCrudFullDetailPage(ModuleRegistry.getAllModules(), registration.detailViewKey)
        ? resolveExplicitDetailRoute(runtimeMetadata?.navDocuments ?? [], registration.detailViewKey)?.path
          ?? (listRoute ? `${listRoute.path.replace(/\/$/, '')}/:id` : undefined)
        : undefined}
    /></CrudLocaleProvider>
    </StandardCrudHostErrorBoundary>
  );
}

function StandardCrudListPageContent<TFilter extends Record<string, unknown>, TRow extends Record<string, unknown>>({
  view,
  registration: sourceRegistration,
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
  const registration = useLocalizedStandardCrudRegistration(sourceRegistration);
  const actionInstance = useContext(ActionInstanceContext);
  useSyncExternalStore(actionInstance?.subscribe ?? noInstanceSubscription, actionInstance?.getSnapshot ?? noInstanceSnapshot);
  // Router hook 无条件调用；是否真正拥有地址由宿主环境决定。
  const routeLocation = useLocation();
  const navigate = useNavigate();
  const activateTreeNode = useContext(TreeNodeActivationContext);
  const routePathname = routeLocation.pathname;
  const routeSearch = routeLocation.search;
  const routeAddress = useMemo<StandardCrudHostAddress>(() => ({
    search: routeSearch,
    write: (search, options) => navigate({ pathname: routePathname, search }, { replace: options?.replace === true }),
  }), [navigate, routePathname, routeSearch]);
  const host = useStandardCrudHostEnvironment(routeAddress);
  const runtimeMetadata = useRuntimeMetadata();
  const features = useFeaturePlacement();
  const readResolution = features.read();
  const readPosition = features.placement ? resolvedFeaturePosition(readResolution) : undefined;
  // 不拥有地址时查询串恒为空：列表状态与 Drawer 参数都不进入外层 URL。
  const location = { search: host.address?.search ?? '' };
  const [messageApi, messageContextHolder] = useMessage();
  const [exportFailure, setExportFailure] = useState<{ failure: unknown } | null>(null);
  const { t } = useLocalization();
  useEffect(() => {
    if (exportFailure) void messageApi.error({ key: 'standard-crud-export-error', content: formatRequestError(exportFailure.failure, t), duration: 5, onClose: () => setExportFailure(null) });
  }, [exportFailure, messageApi, t]);
  const currentSubjectId = useOptionalCurrentSubjectId();
  const sessionTimeZoneId = useOptionalSessionTimeZoneId();
  const resolveActionFeature = (action: StandardCrudRuntimeActionSchema, surface = 'list.rowActions'): FeatureResolution => (
    action.navigationTargetKey
      ? features.navigation(action.navigationTargetKey)
      : (action.standardAction === 'refresh' || action.standardAction === 'view')
        ? readResolution
        : features.action(action.key, surface)
  );
  const allowAction = (action: StandardCrudRuntimeActionSchema, surface = 'list.rowActions') => (
    resolveActionFeature(action, surface).kind === 'allowed'
  );
  const schema = useMemo(() => buildStandardCrudListSchema(view, registration, {
    modelDocuments,
  }), [view, registration, modelDocuments]);
  const dataAdapter = registration.dataAdapter;
  const [runtimeColumnGeneration, setRuntimeColumnGeneration] = useState<StandardCrudRuntimeColumnGeneration | null>(null);
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
  const shouldRenderActionColumn = schema.rowActions.length > 0;
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
  const [preparedDrawerForms, setPreparedDrawerForms] = useState<PreparedStandardCrudDrawerForms | null>(null);
  const createFormSchema = preparedDrawerForms?.create ?? EMPTY_STANDARD_CRUD_FORM_SCHEMA;
  const editFormSchema = preparedDrawerForms?.edit ?? EMPTY_STANDARD_CRUD_FORM_SCHEMA;
  const createFields = useMemo(
    () => localizeStandardCrudFields(resolveCreateFormFields(createFormSchema.fields).map(field =>
      host.input.fixedCreateValues && Object.prototype.hasOwnProperty.call(host.input.fixedCreateValues, field.modelField)
        ? { ...field, readOnly: true, defaultValue: host.input.fixedCreateValues[field.modelField] } : field), schema.dicKey, t),
    [createFormSchema.fields, host.input.fixedCreateValues, schema.dicKey, t],
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
    () => collectStandardCrudListRequestFields(resolvedColumns, schema.rowActions, schema.idField),
    [resolvedColumns, schema.rowActions, schema.idField],
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
    featurePosition: readPosition,
    resourceKey: registration.resourceKey,
    fields: host.presentation.kind === 'action-drawer' ? [] : queryLookupFields,
    providers: registration.lookupProviders ?? [],
    values: filters,
  });
  const [tableQuery, setTableQuery] = useState<SearchTableQueryState>(() => ({
    current: initialListState.page,
    pageSize: initialListState.pageSize,
    sortBy: initialListState.sortBy,
    sortDirection: initialListState.sortDirection,
  }));
  const [drawerState, setDrawerState] = useState<CrudDrawerState>(CLOSED_DRAWER_STATE);
  const drawerActionResolution = features.action(
    drawerState.mode === 'create' ? 'create' : 'edit',
    drawerState.mode === 'create' ? 'list.pageActions' : 'list.rowActions',
  );
  const drawerDetailQuery = useStandardCrudDetailQuery({
    resourceKey: registration.resourceKey,
    id: drawerState.mode === 'view' || drawerState.mode === 'edit' ? drawerState.id : null,
    dataAdapter,
    featurePosition: drawerState.mode === 'edit'
      ? resolvedFeaturePosition(drawerActionResolution)
      : readPosition,
  });
  const detailFields = useMemo(
    () => drawerDetailQuery.detail?.normalized.fields ?? {},
    [drawerDetailQuery.detail],
  );
  const [embeddedActionRequest, setEmbeddedActionRequest] = useState<{ action: StandardCrudRuntimeActionSchema; id: string } | null>(null);
  const embeddedActionQuery = useStandardCrudDetailQuery({
    resourceKey: registration.resourceKey,
    id: embeddedActionRequest?.id,
    dataAdapter,
    featurePosition: readPosition,
    queryScope: host.input.queryScope,
  });
  const embeddedAction = embeddedActionRequest && (!embeddedActionRequest.id || embeddedActionQuery.detail)
    ? { ...embeddedActionRequest, row: (embeddedActionQuery.detail?.normalized.fields ?? {}) as TRow }
    : null;
  const [embeddedActionError, setEmbeddedActionError] = useState<string | null>(null);
  // 确认依据属于宿主交互；语言重读可以卸载行，但不能丢失确认框或重放动作。
  const [rowConfirmation, setRowConfirmation] = useState<{ row: TRow; rowId: string; action: StandardCrudRuntimeActionSchema; closeHost?: boolean } | null>(null);
  useEffect(() => { setRowConfirmation(null); }, [currentSubjectId, schema.resourceKey, schema.viewKey]);
  const [drawerPreparationError, setDrawerPreparationError] = useState<{ failure: unknown } | null>(null);
  const detailError = drawerPreparationError ? formatRequestError(drawerPreparationError.failure, t) : drawerDetailQuery.error;
  const detailLoading = drawerDetailQuery.loading;
  const [createFormValues, setCreateFormValues] = useState<Record<string, unknown>>({});
  const editSession = useStandardCrudEditSession({
    identity: drawerState.mode === 'edit' ? drawerState : null,
    snapshot: !detailLoading && !drawerDetailQuery.refreshing && !drawerDetailQuery.error
      ? drawerDetailQuery.detail?.normalized.fields : undefined,
    fields: editFields, buildValues: preparedDrawerForms?.buildDetailFormValues,
    reload: drawerDetailQuery.reload,
  });
  const formValues = drawerState.mode === 'create' ? createFormValues : editSession.draft?.values ?? {};
  const [presentedOperationResult, setPresentedOperationResult] = useState<StandardCrudOperationResult | null>(null);
  const [rowInputAction, setRowInputAction] = useState<{
    action: StandardCrudRuntimeActionSchema;
    actionScope: 'list.pageActions' | 'list.rowActions';
    row?: TRow;
    selectedRows?: readonly TRow[];
    rowId: string;
    pendingKey: string;
    label: string;
  } | null>(null);
  const [rowInputValue, setRowInputValue] = useState<unknown>(undefined);
  const [discardConfirmOpen, setDiscardConfirmOpen] = useState(false);
  const [advancedFilterOpen, setAdvancedFilterOpen] = useState(false);
  const [exportingMode, setExportingMode] = useState<StandardCrudExportMode | null>(null);
  const exportRequestPendingRef = useRef(false);
  const handledRouteSearchRef = useRef<string | null>(null);
  const handledListRouteSearchRef = useRef<string | null>(null);
  const drawerStateRef = useRef(drawerState);
  const createValuesRef = useRef(createFormValues);
  const initialCreateValuesRef = useRef<Record<string, unknown>>({});
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
  createValuesRef.current = createFormValues;
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
    if (!host.address) return;
    const params = serializeStandardCrudListState(
      schema.query,
      next,
      new URLSearchParams(location.search),
      schema.advancedFilter,
    );
    const search = params.toString().length > 0 ? `?${params.toString()}` : '';
    if (search !== location.search) {
      handledListRouteSearchRef.current = search;
      host.address.write(search, { replace: options.replace === true });
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
    () => createStandardCrudListQueryKey(registration.resourceKey, { ...metadataListRequest, queryScope: JSON.stringify(host.input.queryScope), instanceId: host.input.instanceId }),
    [host.input.instanceId, host.input.queryScope, metadataListRequest, registration.resourceKey],
  );
  const metadataListQuery = usePlatformQuery<{ items: TRow[]; total: number }>({
    key: [...metadataListQueryKey, ...(readPosition ? [readPosition] : [])],
    enabled: schema.columnSource !== 'runtime' && host.presentation.kind !== 'action-drawer',
    load: async ({ signal }) => {
      const result = await dataAdapter.list(metadataListRequest, { signal, queryScope: host.input.queryScope, ...(readPosition ? { featurePosition: readPosition } : {}) });
      return { items: result.items, total: result.total };
    },
  });
  const runtimeColumnsQueryKey = useMemo(
    () => createStandardCrudRuntimeColumnsQueryKey({
      resourceKey: registration.resourceKey,
      filters: appliedFilters,
    }),
    [appliedFilters, registration.resourceKey],
  );
  const runtimeColumnsQuery = usePlatformQuery<StandardCrudRuntimeColumnGeneration>({
    key: [...runtimeColumnsQueryKey, ...(readPosition ? [readPosition] : [])],
    enabled: schema.columnSource === 'runtime' && registration.columnProvider !== undefined,
    load: async ({ signal }) => {
      const provider = registration.columnProvider;
      if (!provider) throw new Error('runtime column source requires a column provider');
      const result = validateRuntimeColumnSet(await provider.load({ filters: appliedFilters, signal, ...(readPosition ? { featurePosition: readPosition } : {}) }));
      nextRuntimeColumnGenerationId += 1;
      return {
        id: nextRuntimeColumnGenerationId,
        columns: result.columns,
        ...(result.contextToken ? { contextToken: result.contextToken } : {}),
      };
    },
  });

  useEffect(() => {
    if (
      schema.columnSource !== 'runtime'
      || runtimeColumnsQuery.status !== 'success'
      || runtimeColumnsQuery.isFetching
      || !runtimeColumnsQuery.data
    ) {
      setRuntimeColumnGeneration(null);
      return;
    }

    const normalizedListState = parseStandardCrudListState(
      schema.query,
      new URLSearchParams(location.search),
      runtimeColumnsQuery.data.columns,
      schema.advancedFilter,
    );
    syncCommittedListState({
      ...normalizedListState,
      filters: appliedFilters,
    }, { replace: true });
    setRuntimeColumnGeneration(runtimeColumnsQuery.data);
    // 列 Query 的成功结果先完成排序归一化，随后才成为行 Query 可消费的完整列代际。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [runtimeColumnsQuery.data, runtimeColumnsQuery.isFetching, runtimeColumnsQuery.status, schema.columnSource]);

  const runtimeListRequest = useMemo<StandardCrudListQueryRequest>(() => {
    const fields = collectStandardCrudListRequestFields(
      runtimeColumnGeneration?.columns ?? [],
      schema.rowActions,
    );
    return {
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
    };
  }, [appliedFilters, runtimeColumnGeneration?.columns, schema.advancedFilter, schema.query,
    schema.rowActions, tableQuery]);
  const runtimeRowsQueryKey = useMemo(
    () => createStandardCrudRuntimeRowsQueryKey({
      resourceKey: registration.resourceKey,
      request: runtimeListRequest,
      columnContextToken: runtimeColumnGeneration?.contextToken,
      columnGeneration: runtimeColumnGeneration?.id ?? 0,
    }),
    [registration.resourceKey, runtimeColumnGeneration?.contextToken, runtimeColumnGeneration?.id,
      runtimeListRequest],
  );
  const runtimeRowsEnabled = schema.columnSource === 'runtime'
    && runtimeColumnsQuery.status === 'success'
    && !runtimeColumnsQuery.isFetching
    && runtimeColumnGeneration !== null
    && runtimeColumnGeneration.id === runtimeColumnsQuery.data?.id;
  const runtimeRowsQuery = usePlatformQuery<{ items: TRow[]; total: number }>({
    key: [...runtimeRowsQueryKey, ...(readPosition ? [readPosition] : [])],
    enabled: runtimeRowsEnabled,
    load: async ({ signal }) => {
      if (!runtimeColumnGeneration) throw new Error('runtime rows require a successful column generation');
      const result = await dataAdapter.list(runtimeListRequest, {
        ...(runtimeColumnGeneration.contextToken
          ? { columnContextToken: runtimeColumnGeneration.contextToken }
          : {}),
        signal,
        ...(readPosition ? { featurePosition: readPosition } : {}),
      });
      return { items: result.items, total: result.total };
    },
  });
  const metadataListError = metadataListQuery.status === 'error'
    ? formatRequestError(metadataListQuery.error, t)
    : null;
  const runtimeColumnsError = runtimeColumnsQuery.status === 'error'
    ? formatRequestError(runtimeColumnsQuery.error, t)
    : null;
  const runtimeRowsError = runtimeRowsQuery.status === 'error'
    ? formatRequestError(runtimeRowsQuery.error, t)
    : null;
  const effectiveRows = useMemo(
    () => schema.columnSource === 'runtime' ? runtimeRowsQuery.data?.items ?? [] : metadataListQuery.data?.items ?? [],
    [metadataListQuery.data?.items, runtimeRowsQuery.data?.items, schema.columnSource],
  );
  const selectionScope = JSON.stringify([currentSubjectId, readPosition, host.input.queryScope, tableQuery, appliedFilters]);
  const rowMenuScope = JSON.stringify([selectionScope, schema.viewKey, host.input.instanceId, host.lifecycle.active,
    features.placement, routePathname, host.address?.search]);
  const [rowMenu, setRowMenu] = useState<{ row: TRow; scope: string; point: { x: number; y: number; focusTarget: HTMLElement } } | null>(null);
  useEffect(() => setRowMenu(null), [rowMenuScope, effectiveRows]);
  const [selection, setSelection] = useState<{ scope: string; data: TRow[]; rows: TRow[] } | null>(null);
  // 选择仅属于当前查询快照；切页、换位置、换主体及刷新数据均同步失效。
  const selectedRows = selection?.scope === selectionScope && selection.data === effectiveRows ? selection.rows : [];
  const effectiveTotal = schema.columnSource === 'runtime'
    ? runtimeRowsQuery.data?.total ?? 0
    : metadataListQuery.data?.total ?? 0;
  const effectiveLoading = schema.columnSource === 'runtime'
    ? runtimeColumnsQuery.status === 'idle'
      || runtimeColumnsQuery.isFetching
      || (runtimeColumnsQuery.status === 'success' && (
        !runtimeRowsEnabled
        || runtimeRowsQuery.status === 'idle'
        || runtimeRowsQuery.isFetching
      ))
    : metadataListQuery.status === 'idle' || metadataListQuery.isFetching;
  useEffect(() => { setSelection(null); }, [selectionScope, effectiveRows, effectiveLoading]);
  const effectiveHasLoaded = schema.columnSource === 'runtime'
    ? runtimeColumnsQuery.status === 'error'
      || runtimeRowsQuery.status === 'success'
      || runtimeRowsQuery.status === 'error'
    : metadataListQuery.status === 'success' || metadataListQuery.status === 'error';
  const effectiveListError = schema.columnSource === 'runtime'
    ? runtimeColumnsError ?? runtimeRowsError
    : metadataListError;

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

  useEffect(() => {
    if (schema.columnSource !== 'runtime' || runtimeRowsQuery.status !== 'success' || !runtimeRowsQuery.data) return;
    const lastPage = Math.max(1, Math.ceil(runtimeRowsQuery.data.total / tableQuery.pageSize));
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
  }, [runtimeRowsQuery.data, runtimeRowsQuery.status, schema.columnSource,
    tableQuery.current, tableQuery.pageSize, tableQuery.sortBy, tableQuery.sortDirection]);

  const refreshRuntimeGeneration = () => {
    if (runtimeColumnsQuery.status === 'error') runtimeColumnsQuery.retry();
    else runtimeColumnsQuery.refresh();
  };

  const refreshList = () => {
    if (schema.columnSource === 'runtime') refreshRuntimeGeneration();
    else metadataListQuery.refresh();
  };

  const committedListSnapshot = useRef<unknown>();
  const [committedReadKey, setCommittedReadKey] = useState<string | null>(null);
  const currentReadKey = JSON.stringify([metadataListQueryKey, runtimeRowsQueryKey, readPosition, host.input.instanceId, currentSubjectId]);
  useEffect(() => setCommittedReadKey(null), [currentReadKey]);
  const currentListSnapshot = schema.columnSource === 'runtime' ? runtimeRowsQuery.data : metadataListQuery.data;
  useEffect(() => {
    if (!effectiveLoading && !effectiveListError && currentListSnapshot !== committedListSnapshot.current) setCommittedReadKey(null);
  }, [currentListSnapshot, effectiveListError, effectiveLoading]);
  const recordCommittedRead = () => {
    committedListSnapshot.current = currentListSnapshot;
    setCommittedReadKey(currentReadKey);
  };

  useEffect(() => { if (host.sync.refreshVersion > 0) refreshList();
    // refreshVersion 是组合成功事实的唯一触发源。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [host.sync.refreshVersion]);
  const actionExecution = useStandardCrudActionExecution<StandardCrudDetail, TRow>({
    refresh: refreshList,
    resourceKey: registration.resourceKey,
    onCommitted: host.sync.committed,
    onResult: (result) => {
      recordCommittedRead();
      if (operationResultPresenterRegistry.resolve(result)) {
        setPresentedOperationResult(result);
      }
    },
  });
  // 动作失败统一弹出提示，避免在操作列撑高行；稳定 key 防止重渲染叠加相同消息。
  useEffect(() => {
    if (actionExecution.error) void messageApi.error({ key: 'standard-crud-action-error',
      content: formatRequestError(actionExecution.error.failure, t), duration: 5, onClose: actionExecution.clearError });
  }, [actionExecution.error, actionExecution.clearError, messageApi, t]);
  const mutationExecution = useStandardCrudMutationExecution({
    registration,
    fixedCreateValues: host.input.fixedCreateValues,
    onCommitted: host.sync.committed,
    refresh: refreshList,
    onResult: (result) => {
      recordCommittedRead();
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
  const formLookupRuntime = useStandardCrudLookupRuntime({
    featurePosition: resolvedFeaturePosition(drawerActionResolution),
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
    if (!host.address) return;
    const search = buildDrawerSearch(mode, id);
    if (search === location.search) {
      return;
    }

    handledRouteSearchRef.current = search;
    host.address.write(search);
  };

  const clearDrawerRoute = () => {
    if (!host.address) return;
    if (!location.search.includes('drawer=') && !location.search.includes('id=')) {
      return;
    }

    const search = clearDrawerSearch();
    handledRouteSearchRef.current = search;
    host.address.write(search, { replace: true });
  };

  const closeDrawer = (options: { syncRoute?: boolean } = {}) => {
    setDrawerPreparationError(null);
    setDrawerState(CLOSED_DRAWER_STATE);
    host.lifecycle.close?.();
    mutationExecution.clearError();
    if (options.syncRoute !== false) {
      clearDrawerRoute();
    }
  };

  const isDrawerDirty = () => (
    drawerStateRef.current.mode === 'edit' ? editSession.dirty
      : drawerStateRef.current.mode === 'create'
        && JSON.stringify(createValuesRef.current) !== JSON.stringify(initialCreateValuesRef.current)
  );

  // 所有离开编辑态的入口共用同一个 Ant Modal，避免关闭、切换记录和浏览器返回出现不同保护语义。
  const confirmDiscardIfNeeded = (proceed: () => void): void => {
    if (mutationExecution.pending) return;
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
        timeZoneId: sessionTimeZoneId,
      }),
      edit: runtime.buildStandardCrudFormSchemaFromModel(modelDocument, {
        mode: 'edit',
        enumOptions: registration.enumOptions ?? {},
        lookupProviders: registration.lookupProviders ?? [],
        timeZoneId: sessionTimeZoneId,
      }),
    };
    const prepared = {
      ...nextSchemas,
      createFields: localizeStandardCrudFields(
        resolveCreateFormFields(nextSchemas.create.fields),
        schema.dicKey,
        t,
      ),
      editFields: localizeStandardCrudFields(nextSchemas.edit.fields, schema.dicKey, t),
      buildDetailFormValues: runtime.buildStandardCrudDetailFormValues,
    };
    setPreparedDrawerForms(prepared);
    return prepared;
  };

  const openCreateDrawer = async (source: CrudDrawerState['source'] = 'user-action') => {
    const prepared = await prepareDrawerForms();
    const initialValues = Object.fromEntries(prepared.createFields.map((field) => [field.key,
      host.input.fixedCreateValues && Object.prototype.hasOwnProperty.call(host.input.fixedCreateValues, field.modelField)
        ? host.input.fixedCreateValues[field.modelField] : field.defaultValue ?? '']));
    if (source === 'user-action') drawerTriggerRef.current = document.activeElement as HTMLElement | null;
    setCreateFormValues(initialValues);
    initialCreateValuesRef.current = initialValues;
    setDrawerPreparationError(null);
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
    setDrawerState({ mode: options.targetMode ?? 'view', id, source });
    setCreateFormValues({});
    initialCreateValuesRef.current = {};
    if (source === 'user-action') drawerTriggerRef.current = document.activeElement as HTMLElement | null;
    if (source === 'user-action' && options.syncRoute !== false) {
      syncDrawerRoute('view', id);
    }
    setDrawerPreparationError(null);
    mutationExecution.clearError();
    try {
      await prepareDrawerForms();
      return true;
    } catch (loadError) {
      setCreateFormValues({});
      setDrawerPreparationError({ failure: loadError });
      if (options.targetMode === 'edit') {
        setDrawerState({ mode: 'view', id, source });
        syncDrawerRoute('view', id);
      }
      return false;
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

  useEffect(() => {
    if (drawerState.mode !== 'edit' || !drawerState.id || !drawerDetailQuery.error) return;
    setDrawerState({ mode: 'view', id: drawerState.id, source: drawerState.source });
    syncDrawerRoute('view', drawerState.id);
    // URL 同步 helper 的身份变化不应重复处理同一次 query failure。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [drawerDetailQuery.error, drawerState.id, drawerState.mode, drawerState.source]);

  const handleDrawerSave = async () => {
    if (features.placement && drawerActionResolution.kind !== 'allowed') {
      return;
    }
    if (drawerState.mode === 'edit'
      && (!editSession.draft || detailLoading || drawerDetailQuery.refreshing || editSession.reloading)) return;
    const succeeded = await mutationExecution.execute({
      mode: drawerState.mode === 'create' ? 'create' : 'edit',
      featurePosition: resolvedFeaturePosition(drawerActionResolution),
      id: drawerState.id,
      fields: drawerState.mode === 'create' ? createFields : editFields,
      ...(drawerState.mode === 'edit' ? editSession.draft! : { values: formValues, detailFields }),
    });
    if (succeeded) {
      closeDrawer();
    }
  };

  const handleFormChange = (fieldKey: string, value: unknown) => {
    const fields = drawerState.mode === 'create' ? createFields : editFields;
    if (drawerState.mode === 'edit') editSession.change(fieldKey, value);
    else setCreateFormValues((current) => applyStandardCrudLookupValueChange(current, fieldKey, value, fields));
    if (mutationExecution.fieldErrors[fieldKey] || mutationExecution.conflict) {
      mutationExecution.clearError();
    }
  };

  useEffect(() => {
    if (!host.address) return;
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
          host.address?.write(searchWithoutDrawer, { replace: true });
        });
        if (current.mode !== 'closed') syncDrawerRoute(current.mode, current.id);
      } else {
        closeDrawer({ syncRoute: false });
      }
    }
    // URL 是抽屉兼容状态的唯一触发源，handler 身份变化不应重复打开或关闭抽屉。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.search]);

  useEffect(() => {
    host.lifecycle.reportState({ dirty: isDrawerDirty() || (rowInputAction !== null && rowInputValue !== undefined), submitting: mutationExecution.pending || actionExecution.pendingKey !== null }, () => { setRowInputAction(null); setRowInputValue(undefined); closeDrawer(); });
  });
  const presentation = host.presentation;
  const actionDrawerStandardAction = presentation.kind === 'action-drawer' ? presentation.standardAction : undefined;
  const actionDrawerActionKey = presentation.kind === 'action-drawer' ? presentation.actionKey : undefined;
  useEffect(() => {
    if (presentation.kind !== 'action-drawer') return;
    const action = [...schema.pageActions, ...schema.rowActions, ...(schema.featureActions ?? [])].find(item => actionDrawerActionKey ? item.key === actionDrawerActionKey : item.standardAction === actionDrawerStandardAction);
    if (!action || !allowAction(action)) { setEmbeddedActionError('Platform.ActionUnavailable'); return; }
    if (action.standardAction === 'create') { void openCreateDrawer(); return; }
    const id = host.input.recordId;
    if (!id && (schema.rowActions.includes(action) || schema.featureActions?.includes(action))) { setEmbeddedActionError('Platform.MissingRecordContext'); return; }
    if (action.standardAction === 'edit' && id) { void openEditDrawer(id); return; }
    if (action.standardAction === 'view' && id) { void openViewDrawer(id); return; }
    setEmbeddedActionRequest({ action, id: id ?? '' });
    // 外层页面实例与操作参数拥有首次打开；表单更新不重复打开 Drawer。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [host.input.recordId, actionDrawerStandardAction, actionDrawerActionKey]);

  // SearchTablePage 仍保留旧 request Interface；controlledResult 生效时该兼容函数不会执行远程读取。
  const tableRequest = useMemo(() => async () => ({
    data: effectiveRows,
    total: effectiveTotal,
    success: effectiveListError === null,
  }), [effectiveListError, effectiveRows, effectiveTotal]);

  const createAction = schema.pageActions.find((action) => action.standardAction === 'create');
  const editAction = schema.rowActions.find((action) => action.standardAction === 'edit');
  const rowActionDefinitions = schema.rowActions;
  // 列表菜单与组合动作入口共享资源动作解析与执行，Tree 不持有业务 handler。
  function buildRowActions(row: TRow, definitions: StandardCrudRuntimeActionSchema[], rowId: string): RowActionItem[] {
    return definitions
      .map((action) => ({ action, featureResolution: resolveActionFeature(action) }))
      .filter(({ featureResolution }) => featureResolution.kind === 'allowed')
      .filter(({ action }) => isRowActionVisibleForRow(action, row as Record<string, unknown>))
      .map(({ action, featureResolution }) => ({
        action,
        featureResolution,
        actionState: registration.actionStateResolver?.({ action, row, instance: actionInstance, queryScope: host.input.queryScope }),
      }))
      .filter(({ actionState }) => actionState?.visible !== false)
      .map(({ action, actionState, featureResolution }): RowActionItem => {
        const pendingKey = `${action.key}:${rowId}`;
        const label = actionState?.labelKey ? t(actionState.labelKey, '') : t(`${schema.dicKey}.${action.labelKey}`, action.labelKey);
        const metadataDisabledReason = resolveRowActionDisabledReason(
          action,
          row as Record<string, unknown>,
          (key) => t(`${schema.dicKey}.${key}`, key),
        );
        const disabledReason = actionState?.available === false
          ? formatStandardCrudActionReason(actionState, t) ?? t('Platform.ActionStateUnavailable', '当前状态不允许执行该操作。')
          : metadataDisabledReason;
        const confirmMessage = action.confirmMessageKey
          ? t(`${schema.dicKey}.${action.confirmMessageKey}`, action.confirmMessageKey)
          : host.presentation.kind === 'action-drawer' && action.standardAction === 'delete' ? t('Platform.ConfirmDelete', '确认删除当前记录？') : undefined;
        const handler = registration.actionHandlers?.[action.handlerKey ?? action.key]
          ?? (action.standardAction === 'refresh' ? () => refreshList() : undefined);
        const inputRenderer = action.inputKey ? registration.actionInputRenderers?.[action.inputKey] : undefined;

        if (action.standardAction === 'view') {
          return {
            key: action.key,
            label,
            onClick: () => confirmDiscardIfNeeded(() => { openRowProperties(row); }),
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
                setRowInputAction({ action, actionScope: 'list.rowActions', row, rowId, pendingKey, label });
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
              if (featureResolution.kind !== 'allowed') return false;
              const deleteHandler = handler ?? (async () => {
                if (!dataAdapter.remove) {
                  throw new Error(`标准资源 "${registration.resourceKey}" 未提供删除能力。`);
                }
                return dataAdapter.remove(rowId, isStandardCrudRecord(row) ? row : undefined, {
                  featurePosition: featureResolution.position,
                });
              });
              const outcome = await actionExecution.execute({
                key: pendingKey,
                handler: deleteHandler,
                context: { instance: actionInstance, action, id: rowId, row, featurePosition: featureResolution.position },
                refreshOnConflict: false,
                fallbackErrorMessage: '删除标准资源失败',
              });
              if (outcome.status === 'succeeded') {
                closeDrawerForRow(rowId);
                host.lifecycle.close?.();
              }
              return outcome.status === 'succeeded';
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
            if (featureResolution.kind !== 'allowed') return false;
            if (!handler) {
              return;
            }

            if (inputRenderer) {
              actionExecution.clearError();
              setRowInputValue(undefined);
              setRowInputAction({ action, actionScope: 'list.rowActions', row, rowId, pendingKey, label });
              return;
            }

            const outcome = await actionExecution.execute({
              key: pendingKey,
              handler,
              context: { instance: actionInstance, action, id: rowId, row, queryScope: host.input.queryScope, featurePosition: featureResolution.position },
            });
            if (outcome.status === 'succeeded') host.lifecycle.close?.();
            return outcome.status === 'succeeded';
          },
        };
      })
      .map((item) => {
        const action = definitions.find((definition) => definition.key === item.key);
        const resolver = action && registration.actionEligibilityResolvers?.[action.key];
        const position = action && resolvedFeaturePosition(resolveActionFeature(action));
        return action && resolver && position ? {
          ...item,
          eligibility: { resourceKey: registration.resourceKey, objectId: rowId,
            featurePosition: position,
            resolve: (signal: AbortSignal) => resolver({ id: rowId, row, action, featurePosition: position, signal }) },
        } : item;
      });
  }
  // 绑定只返回正式 View 身份；路由与目标准入继续由 owning module 的标准页面负责。
  function openRowProperties(row: TRow): void {
    const target = dataAdapter.getDetailTarget?.(row);
    if (!target) { void openViewDrawer(dataAdapter.getDetailId?.(row) ?? dataAdapter.getRowId(row)); return; }
    const route = resolveExplicitDetailRoute(runtimeMetadata?.navDocuments ?? [], target.detailViewKey);
    if (!target.id || !route?.path || !hasStandardCrudFullDetailPage(ModuleRegistry.getAllModules(), target.detailViewKey)) {
      void messageApi.error(t('Platform.ActionUnavailable', '当前操作不可用。')); return;
    }
    navigate(route.path.replace(':id', encodeURIComponent(target.id)));
  }
  function activateRow(row: TRow): void {
    try {
      const target = dataAdapter.getRowActivationTarget?.(row, { queryScope: host.input.queryScope });
      if (!target) { openRowProperties(row); return; }
      if (target.kind === 'tree-node' && target.nodeKey && activateTreeNode) { activateTreeNode(target.nodeKey); return; }
      void messageApi.error(t('Platform.ActionUnavailable', '当前操作不可用。'));
    } catch (error) { void messageApi.error(formatRequestError(error, t)); }
  }
  const metadataColumns = resolvedColumns.map((column) => {
    const renderColumn = column.standardAction === 'view'
      ? (row: TRow) => (
        <AntdButton
          className="np-standard-crud-cell-action"
          type="link"
          onClick={() => confirmDiscardIfNeeded(() => { activateRow(row); })}
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
  const compactRowMenu = Boolean(schema.rowContextMenu?.length
    && schema.rowActions.every((action, index) => schema.rowContextMenu?.[index] === action.key)
    && schema.rowContextMenu.length === schema.rowActions.length);
  const standardColumns = shouldRenderActionColumn
    ? [
      ...metadataColumns,
      {
        key: 'actions',
        dataIndex: 'actions',
        title: t('Platform.Actions', '操作'),
        width: compactRowMenu ? 64 : STANDARD_CRUD_ACTION_COLUMN_WIDTH,
        fixed: 'right' as const,
        sorter: false,
        responsive: undefined,
        metadataField: undefined,
        metadataSource: undefined,
        runtimeValueKind: undefined,
        render: (row: TRow) => {
          const rowId = dataAdapter.getRowId(row);
          const rowActions = buildRowActions(row, rowActionDefinitions, rowId);

          return (
            <div style={{ display: 'grid', gap: 6 }}>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'flex-start' }}>
                <RowActionButtonGroup actions={rowActions} compact={compactRowMenu} onConfirmRequested={key => {
                  const action = rowActionDefinitions.find(item => item.key === key);
                  if (action) setRowConfirmation({ row, rowId, action });
                }} active={host.lifecycle.active} contextKey={JSON.stringify([rowMenuScope, row])}
                  contextMenu={rowMenu?.row === row && rowMenu.scope === rowMenuScope && !effectiveLoading
                    && effectiveRows.includes(row) ? { actionKeys: schema.rowContextMenu ?? [], point: rowMenu.point } : undefined}
                  closeContextMenu={() => setRowMenu(null)} />
              </div>
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
          }, t)
        : renderMetadataTableCell(
          row,
          column,
          modelDocument,
          registration as StandardCrudResourceRegistration<Record<string, unknown>, Record<string, unknown>>,
        )),
  }));

  const visiblePageActions = schema.pageActions
    .map(action => ({ action, featureResolution: resolveActionFeature(action, 'list.pageActions') }))
    .filter(({ featureResolution }) => featureResolution.kind === 'allowed')
    .filter(({ action }) => registration.actionStateResolver?.({ action, instance: actionInstance, queryScope: host.input.queryScope })?.visible !== false);
  const visibleHeaderActions = visiblePageActions
    .filter(({ action }) => action.standardAction === 'create' || Boolean(action.handlerKey))
    .map(({ action }) => action);
  const exportResolution = features.action('export', 'list');
  const handleExport = async (mode: StandardCrudExportMode) => {
    if (exportResolution.kind !== 'allowed') return;
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
      }, { featurePosition: exportResolution.position });
      const fallbackModeLabel = mode === 'current' ? '当前页' : '全部';
      downloadStandardCrudBlob(
        response.blob,
        response.fileName ?? `${pageTitle}-${fallbackModeLabel}-${formatTimestampForFileName()}.xlsx`,
      );
    } catch (exportError) {
      setExportFailure({ failure: exportError });
    } finally {
      exportRequestPendingRef.current = false;
      setExportingMode(null);
    }
  };
  const exportToolbar = schema.exportModes.length > 0 && exportResolution.kind === 'allowed' ? (
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
        {t('Platform.Export', '导出')} <DownOutlined />
      </AntdButton>
    </Dropdown>
  ) : undefined;
  // 分组只改变入口呈现；每次执行仍以子动作的当前权限和原有生命周期为准。
  function runHeaderAction(action: StandardCrudRuntimeActionSchema): void {
    if (actionExecution.pendingKey !== null || mutationExecution.pending || (action.requiresSelection && (effectiveLoading || selectedRows.length === 0))) return;
    const state = registration.actionStateResolver?.({ action, instance: actionInstance, queryScope: host.input.queryScope });
    if (state?.visible === false || state?.available === false) return;
    const feature = resolveActionFeature(action, 'list.pageActions');
    if (feature.kind !== 'allowed') return;
    if (action.standardAction === 'create') { void openCreateDrawer(); return; }
    const handler = registration.actionHandlers?.[action.handlerKey ?? action.key];
    if (!handler) return;
    confirmDiscardIfNeeded(() => {
      if (action.inputKey) {
        actionExecution.clearError(); setRowInputValue(undefined);
        setRowInputAction({ action, actionScope: 'list.pageActions', rowId: '', pendingKey: action.key, label: action.labelKey, selectedRows: action.requiresSelection ? [...selectedRows] : undefined });
      } else {
        void actionExecution.execute({ key: action.key, handler,
          context: { instance: actionInstance, action, queryScope: host.input.queryScope, featurePosition: feature.position, selectedRows: action.requiresSelection ? [...selectedRows] : undefined } });
      }
    });
  }
  const groupByAction = new Map((schema.pageActionGroups ?? []).flatMap(group => group.actionKeys.map(key => [key, group] as const)));
  const emittedGroups = new Set<string>();
  const headerEntries: Array<{ key: string; group?: StandardCrudPageActionGroupSchema; actions: StandardCrudRuntimeActionSchema[] }> = [];
  for (const action of visibleHeaderActions) {
    const group = groupByAction.get(action.key);
    if (!group) { headerEntries.push({ key: action.key, actions: [action] }); continue; }
    if (emittedGroups.has(group.key)) continue;
    emittedGroups.add(group.key);
    headerEntries.push({ key: group.key, group,
      actions: group.actionKeys.flatMap(key => visibleHeaderActions.filter(child => child.key === key)) });
  }
  const headerPending = actionExecution.pendingKey !== null || mutationExecution.pending;
  const headerActionLabel = (action: StandardCrudRuntimeActionSchema): string => {
    const state = registration.actionStateResolver?.({ action, instance: actionInstance, queryScope: host.input.queryScope });
    return state?.labelKey ? t(state.labelKey, '') : t(`${schema.dicKey}.${action.labelKey}`, action.labelKey);
  };
  const headerActions = headerEntries.length > 0 ? <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
    {headerEntries.map(({ key, group, actions }) => group ? (
      <Dropdown key={key} trigger={['click']} disabled={headerPending} menu={{
        items: actions.map(action => ({ key: action.key, label: headerActionLabel(action),
          disabled: headerPending || registration.actionStateResolver?.({ action, instance: actionInstance, queryScope: host.input.queryScope })?.available === false || (action.requiresSelection && (effectiveLoading || selectedRows.length === 0)), danger: action.variant === 'danger' })),
        onClick: ({ key: childKey }) => { const action = actions.find(child => child.key === childKey); if (action) runHeaderAction(action); },
      }}>
        <AntdButton type={group.variant === 'primary' ? 'primary' : 'default'} danger={group.variant === 'danger'}
          size="middle" disabled={headerPending}>
          {t(`${schema.dicKey}.${group.labelKey}`, group.labelKey)} <DownOutlined aria-hidden />
        </AntdButton>
      </Dropdown>
    ) : (
      <AntdButton key={key} type={actions[0].variant === 'primary' ? 'primary' : 'default'} size="middle"
        danger={actions[0].variant === 'danger'} disabled={headerPending || registration.actionStateResolver?.({ action: actions[0], instance: actionInstance, queryScope: host.input.queryScope })?.available === false || (actions[0].requiresSelection && (effectiveLoading || selectedRows.length === 0))} onClick={() => runHeaderAction(actions[0])}>
        {headerActionLabel(actions[0])}
      </AntdButton>
    ))}
  </div> : undefined;
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
            t,
          ))}
        </div>
      )}
      loading={effectiveLoading}
      onSearch={applyFilters}
      onReset={resetFilters}
      more={schema.advancedFilter.enabled ? (
        <AntdButton type="text" size="middle" onClick={() => setAdvancedFilterOpen(true)}>
          {t(`${schema.dicKey}.${schema.advancedFilter.titleKey}`, schema.advancedFilter.titleKey)}
        </AntdButton>
      ) : undefined}
    />
  );

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
    && !drawerDetailQuery.refreshing
    && allowAction(editAction)
    && drawerEditActionState?.visible !== false
    && drawerEditActionState?.available !== false,
  );

  const reloadConflictedRecord = async () => {
    if (drawerState.mode !== 'edit' || !drawerState.id) return;
    if (await editSession.reload()) mutationExecution.clearError();
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

  const content = (
    <div className="np-standard-crud-table">
      <CrudTableSection<TRow>
        onRow={schema.rowContextMenu?.length ? row => ({
          tabIndex: 0,
          onContextMenu: event => {
            if (isNativeContextMenuTarget(event.target) || effectiveLoading || !host.lifecycle.active) return;
            event.preventDefault(); event.stopPropagation();
            setRowMenu({ row, scope: rowMenuScope, point: { x: event.clientX, y: event.clientY, focusTarget: event.currentTarget } });
          },
          onKeyDown: event => {
            if (isNativeContextMenuTarget(event.target) || effectiveLoading || !host.lifecycle.active) return;
            if (event.key !== 'ContextMenu' && !(event.shiftKey && event.key === 'F10')) return;
            event.preventDefault(); event.stopPropagation();
            const bounds = event.currentTarget.getBoundingClientRect();
            setRowMenu({ row, scope: rowMenuScope, point: { x: bounds.left, y: bounds.bottom, focusTarget: event.currentTarget } });
          },
        }) : undefined}
        headerTitle={headerActions}
        selectableRows={visibleHeaderActions.some(action => action.requiresSelection)}
        selection={{ keys: effectiveLoading ? [] : selectedRows.map(dataAdapter.getRowId), disabled: effectiveLoading || headerPending || rowInputAction !== null,
          onChange: rows => setSelection({ scope: selectionScope, data: effectiveRows, rows }) }}
        rowKey={dataAdapter.getRowId}
        columns={columns}
        request={tableRequest}
        query={tableQuery}
        onQueryChange={handleTableQueryChange}
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
        filteredEmpty={Object.values(appliedFilters).some(hasActiveFilterValue)}
        loading={effectiveLoading}
        controlledResult={{
          data: effectiveRows,
          total: effectiveTotal,
          loading: effectiveLoading,
          hasLoaded: effectiveHasLoaded,
          error: effectiveListError,
          onRetry: schema.columnSource === 'runtime'
            ? refreshRuntimeGeneration
            : metadataListQuery.retry,
        }}
        toolbar={exportToolbar}
        onRefresh={refreshList}
      />
    </div>
  );

  // 来源与 key 共同标识已打开动作，本地化重建配置对象不能改变会话的判权位置。
  const rowInputFeatureResolution: FeatureResolution = rowInputAction
    ? features.action(rowInputAction.action.key, rowInputAction.actionScope)
    : { kind: 'unresolved' };
  const confirmationAction = rowConfirmation
    ? buildRowActions(rowConfirmation.row, [rowConfirmation.action], rowConfirmation.rowId)[0]
    : undefined;

  return (
    <>
      {messageContextHolder}
      {embeddedActionQuery.error && <div role="alert">{embeddedActionQuery.error}<AntdButton onClick={embeddedActionQuery.retry}>{t('Platform.RetryRead', '重试读取')}</AntdButton></div>}
      {embeddedActionError && <div role="alert">{t(embeddedActionError, t('Platform.ActionUnavailable', '当前操作不可用。'))}<AntdButton onClick={host.lifecycle.close}>{t('Platform.Close', '关闭')}</AntdButton></div>}
      {embeddedAction && <RowActionButtonGroup requested active={host.lifecycle.active} close={host.lifecycle.close}
        onConfirmRequested={() => setRowConfirmation({ row: embeddedAction.row, rowId: embeddedAction.id, action: embeddedAction.action, closeHost: true })}
        actions={buildRowActions(embeddedAction.row, [embeddedAction.action], embeddedAction.id)} />}
      {rowConfirmation && <Suspense fallback={<span role="status">{t('Platform.LoadingConfirmation', '正在加载确认框…')}</span>}>
        <Modal open={host.lifecycle.active}
          title={<StandardCrudDialogTitle>{confirmationAction?.confirmMessage ?? t('Platform.ActionUnavailable', '当前操作不可用。')}</StandardCrudDialogTitle>}
          okText={t('Platform.Confirm', '确认')} cancelText={t('Platform.Cancel', '取消')}
          confirmLoading={confirmationAction?.loading}
          okButtonProps={{ danger: confirmationAction?.danger, disabled: !confirmationAction || confirmationAction.disabled }}
          destroyOnHidden
          onCancel={() => { if (confirmationAction?.loading) return; setRowConfirmation(null); if (rowConfirmation.closeHost) host.lifecycle.close?.(); }}
          onOk={async () => {
            if (!confirmationAction || confirmationAction.disabled) return;
            if (await confirmationAction.onClick() !== false) setRowConfirmation(null);
          }} />
      </Suspense>}
      {presentedOperationResult
        ? operationResultPresenterRegistry.resolve(presentedOperationResult)?.render({
            result: presentedOperationResult,
            close: () => setPresentedOperationResult(null),
          })
        : null}
      {host.presentation.kind !== 'action-drawer' && <CrudWorkspacePage
        embedded={host.presentation.kind === 'workspace' && host.presentation.composed}
        filterBar={filterBar}
      >
        {content}
      </CrudWorkspacePage>}
      {(actionExecution.synchronizationError || committedReadKey === currentReadKey && effectiveListError) && <div role="alert">{t('Platform.SavedRefreshFailed', '已保存，刷新失败。请重新加载。')}</div>}
      {mutationExecution.synchronizationError && <div role="alert">{mutationExecution.synchronizationError}</div>}
      {schema.advancedFilter.enabled && advancedFilterOpen && host.lifecycle.active ? (
        <Suspense fallback={<div className="sh-loading-state" role="status">{t('Platform.LoadingAdvancedFilter', '正在加载高级筛选…')}</div>}>
          <Drawer
            open
            title={<StandardCrudDialogTitle>{t(`${schema.dicKey}.${schema.advancedFilter.titleKey}`, schema.advancedFilter.titleKey)}</StandardCrudDialogTitle>}
            width={420}
            placement="right"
            destroyOnClose
            onClose={() => setAdvancedFilterOpen(false)}
          >
          <div className="np-advanced-filter-drawer">
            <Space className="np-advanced-filter-drawer-actions">
              <AntdButton
                onClick={() => {
                  resetFilters();
                }}
                disabled={effectiveLoading}
              >
                {t(`${schema.dicKey}.Action_ResetAdvanced`, '重置条件')}
              </AntdButton>
              <AntdButton
                type="primary"
                loading={effectiveLoading}
                onClick={() => {
                  applyFilters();
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
                    t,
                  ))}
                </div>
              </section>
            ))}
            </div>
          </Drawer>
        </Suspense>
      ) : null}
      {drawerState.mode !== 'closed' && host.lifecycle.active ? (
        <Suspense fallback={<div className="sh-loading-state" role="status">{t('Platform.LoadingActionPanel', '正在加载操作面板…')}</div>}>
          <StandardCrudListDrawerRuntime
            mode={drawerState.mode}
            viewTitle={viewDrawerTitle}
            mutationTitle={drawerState.mode === 'create' ? createDrawerTitle : editDrawerTitle}
            detailLoading={detailLoading}
            detailError={detailError}
            saving={mutationExecution.pending || drawerDetailQuery.refreshing || editSession.reloading}
            saveError={mutationExecution.error}
            saveFieldErrors={mutationExecution.fieldErrors}
            formId={drawerFormId}
            fields={drawerState.mode === 'create' ? createFields : editFields}
            sections={drawerState.mode === 'create' ? createFormSchema.sections : editFormSchema.sections}
            detailPresentation={drawerDetailPresentation}
            registration={registration}
            values={formValues}
            lookupOptions={formLookupRuntime.lookupOptions}
            lookupLoading={formLookupRuntime.lookupLoading}
            lookupErrors={formLookupRuntime.lookupErrors}
            lookupHasMore={formLookupRuntime.lookupHasMore}
            onLookupSearch={formLookupRuntime.search}
            onLookupLoadMore={formLookupRuntime.loadMore}
            onLookupRetry={formLookupRuntime.retry}
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
            detailErrorActions={drawerDetailQuery.error ? (
              <AntdButton onClick={drawerDetailQuery.retry}>{t('Platform.Retry', '重试')}</AntdButton>
            ) : undefined}
            saveErrorActions={mutationExecution.conflict ? (
              <AntdButton
                onClick={reloadConflictedRecord}
                disabled={mutationExecution.pending || drawerDetailQuery.refreshing || editSession.reloading}
              >
                {t('Platform.ReloadData', '重新加载当前数据')}
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
        <Suspense fallback={<div className="sh-loading-state" role="status">{t('Platform.LoadingActionConfirmation', '正在加载操作确认…')}</div>}>
          <StandardCrudActionReviewDialog
            open={host.lifecycle.active}
            action={{ ...rowInputAction.action,
              confirmLabel: rowInputAction.action.confirmLabelKey
                ? t(`${schema.dicKey}.${rowInputAction.action.confirmLabelKey}`, rowInputAction.action.confirmLabelKey)
                : undefined }}
            title={t(`${schema.dicKey}.${rowInputAction.action.labelKey}`, rowInputAction.action.labelKey)}
            confirmMessage={rowInputAction.action.confirmMessageKey
              ? t(
                  `${schema.dicKey}.${rowInputAction.action.confirmMessageKey}`,
                  rowInputAction.action.confirmMessageKey,
                )
              : undefined}
            inputRenderer={registration.actionInputRenderers[rowInputAction.action.inputKey]}
            featurePosition={resolvedFeaturePosition(rowInputFeatureResolution)}
            queryScope={host.input.queryScope}
            value={rowInputValue}
            row={rowInputAction.row}
            selectedRows={rowInputAction.selectedRows}
            error={actionExecution.error?.key === rowInputAction.pendingKey ? formatRequestError(actionExecution.error.failure, t) : null}
            pending={actionExecution.pendingKey === rowInputAction.pendingKey}
            onChange={setRowInputValue}
            onCancel={() => {
              if (actionExecution.pendingKey === null) { setRowInputAction(null); host.lifecycle.close?.(); }
            }}
            onConfirm={() => {
              const { action, row, rowId, pendingKey } = rowInputAction;
              if (rowInputFeatureResolution.kind !== 'allowed') return;
              const handler = registration.actionHandlers?.[action.handlerKey ?? action.key];
              if (!handler) return;
              void (async () => {
                const outcome = await actionExecution.execute({
                  key: pendingKey,
                  handler,
                  context: { instance: actionInstance, action: rowInputAction.action, id: rowId, row, selectedRows: rowInputAction.selectedRows, input: rowInputValue, queryScope: host.input.queryScope, featurePosition: rowInputFeatureResolution.position },
                });
                if (outcome.status === 'succeeded') {
                  setRowInputAction(null);
                  host.lifecycle.close?.();
                } else if (outcome.status === 'failed' && outcome.conflict) {
                  setRowInputAction(null);
                }
              })();
            }}
          />
        </Suspense>
      ) : null}
      {discardConfirmOpen ? (
        <Suspense fallback={<div className="sh-loading-state" role="status">{t('Platform.LoadingDiscardConfirmation', '正在加载放弃确认…')}</div>}>
          <Modal
            open
            title={<StandardCrudDialogTitle>{t('Platform.DiscardTitle', '放弃未保存的更改？')}</StandardCrudDialogTitle>}
            okText={t('Platform.DiscardChanges', '放弃更改')}
            cancelText={t('Platform.KeepEditing', '继续编辑')}
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
              initialCreateValuesRef.current = createValuesRef.current;
              proceed?.();
            }}
          >
            {t('Platform.DiscardDescription', '当前修改尚未保存，继续将丢失这些内容。')}
          </Modal>
        </Suspense>
      ) : null}
    </>
  );
}
