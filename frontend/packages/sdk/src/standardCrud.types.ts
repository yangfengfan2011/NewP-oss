import type { ReactNode } from 'react';

/** 标准摘要块状态；平台只解释状态语义，专属展示内容仍由模块扩展承接。 */
export type StandardCrudSummaryStatus = 'ready' | 'notConfigured' | 'dependencyUnavailable';

/** 标准 CRUD 详情摘要块，第一批用于统一状态和数据外形，具体 renderer 留到第二批固化。 */
export interface StandardCrudSummaryBlock<TData = unknown> {
  status: StandardCrudSummaryStatus;
  data: TData | null;
}

/** 标准 CRUD 列表响应的前端稳定外形，Host 依赖这些字段管理分页和排序显示。 */
export interface StandardCrudListResult<TItem> {
  items: TItem[];
  total: number;
  page: number;
  pageSize: number;
  sortBy?: string | null;
  sortDirection?: string | null;
}

/** 标准 CRUD 详情响应的最小公共契约；业务字段保持 unknown，避免 SDK 写入模块语义。 */
export interface StandardCrudDetail {
  fields: Record<string, unknown>;
  summaries: Record<string, StandardCrudSummaryBlock<unknown> | undefined>;
}

/** 标准 create/update 请求体外形，字段含义由模块资源注册和后端 descriptor 共同约束。 */
export type StandardCrudMutationValues = Record<string, unknown>;

/** 标准 CRUD 操作种类只描述通用生命周期，不携带任何模块或资源身份。 */
export type StandardCrudOperationKind = 'create' | 'update' | 'action';

/** 操作结果保持不透明；只有声明支持该结果的模块 presenter 才能解释 payload。 */
export interface StandardCrudOperationResult {
  /** 已确认成功且没有写入；不能与 change 同时出现，也不表示失败或未知结果。 */
  noChange?: true;
  /** 正式词典中的成功提示；共享宿主负责翻译和通知生命周期。 */
  notice?: { reasonKey: string };
  /** 只引用已注册导航入口；Shell 解析路径并执行目标入口的正常准入。 */
  navigation?: { entryKey: string; parameters?: Record<string, string> };
  change?: Omit<import("./pageComposition").ResourceChange, "resourceKey">;
  operation: StandardCrudOperationKind;
  payload: unknown;
}

/** 模块 presenter 获得当前内存结果和一次性关闭入口，不拥有 Host 的其他页面状态。 */
export interface StandardCrudOperationResultPresenterContext {
  result: StandardCrudOperationResult;
  close: () => void;
}

/** 操作结果 presenter 通过公开 shape 判定支持范围，并在模块边界内完成具体展示。 */
export interface StandardCrudOperationResultPresenter {
  supports: (result: StandardCrudOperationResult) => boolean;
  render: (context: StandardCrudOperationResultPresenterContext) => ReactNode;
}

/** 共享 registry 只解析已注册 presenter，不解释 payload 或引入模块特判。 */
export interface StandardCrudOperationResultPresenterRegistry {
  resolve: (result: StandardCrudOperationResult) => StandardCrudOperationResultPresenter | undefined;
}

/** 标准 CRUD 字段类型只描述渲染意图，具体控件流程由 Shell 运行时按资源注册装配。 */
export type StandardCrudFieldKind =
  | 'text'
  | 'textarea'
  | 'number'
  | 'boolean'
  | 'switch'
  | 'checkbox'
  | 'enum'
  | 'select'
  | 'lookup'
  | 'date'
  | 'dateTime'
  | 'dateRange'
  | 'readonly';

/** 标准 CRUD 选项外形；SDK 不解释业务枚举，只提供 lookup 和 enum 的稳定值/标签契约。 */
export interface StandardCrudOption {
  value: string;
  label: string;
  /** 非业务枚举的静态选项可引用正式词典键；label 仍作为缺项回退。 */
  labelKey?: string;
  disabled?: boolean;
  /** 仅 SDK 合成的缺失当前值项使用此标记；业务 label 和 disabled 不构成不可用事实。 */
  unavailable?: true;
}

/** lookup 请求上下文；依赖字段值由 Shell 收集，Provider 只接收可序列化输入。 */
export interface StandardCrudLookupRequest {
  fieldKey: string;
  keyword: string;
  page: number;
  pageSize: number;
  currentValueIds: string[];
  values: Record<string, unknown>;
  dependencyValues: Record<string, unknown>;
}

/** lookup Provider 的稳定分页结果；当前值与候选页分离，避免搜索和翻页丢失既有选择。 */
export interface StandardCrudLookupPage {
  options: StandardCrudOption[];
  currentOptions: StandardCrudOption[];
  page: number;
  pageSize: number;
  hasMore: boolean;
}

/** lookup 单次执行上下文；取消信号与发起动作位置由宿主传入。 */
export interface StandardCrudLookupExecutionContext extends StandardCrudFeatureRequestContext {
  readonly signal: AbortSignal;
}

/** lookup Provider 只返回候选项，不拥有当前值保留、加载状态或错误展示流程。 */
export interface StandardCrudLookupProvider {
  key: string;
  load: (
    request: StandardCrudLookupRequest,
    context?: StandardCrudLookupExecutionContext,
  ) => Promise<StandardCrudLookupPage | StandardCrudOption[]>;
}

/** 标准 REST lookup Provider 配置；模块只声明 provider key、资源路由和宿主传输。 */
export interface StandardCrudRestLookupProviderOptions {
  key: string;
  apiRoute: string;
  fetcher: typeof fetch;
}

export interface LoadStandardCrudLookupOptionsInput {
  field: StandardCrudFieldDefinition;
  providers: StandardCrudLookupProvider[];
  values: Record<string, unknown>;
  detailFields?: Record<string, unknown>;
}

export interface LoadStandardCrudLookupOptionsResult {
  options: StandardCrudOption[];
  error: string | null;
  /** 原始 provider 失败事实；调用方在当前语言下呈现，诊断不作为用户正文。 */
  failure?: unknown;
}

export interface LoadStandardCrudLookupPageInput extends LoadStandardCrudLookupOptionsInput {
  keyword?: string;
  page?: number;
  pageSize?: number;
}

export interface LoadStandardCrudLookupPageResult extends StandardCrudLookupPage {
  error: string | null;
  /** 原始 provider 失败事实，不改变候选分页与当前值保留规则。 */
  failure?: unknown;
}

export interface UseStandardCrudLookupRuntimeInput {
  fields: StandardCrudFieldDefinition[];
  providers?: StandardCrudLookupProvider[];
  values: Record<string, unknown>;
  detailFields?: Record<string, unknown>;
}

export interface StandardCrudLookupRuntimeState {
  lookupOptions: Record<string, StandardCrudOption[]>;
  lookupLoading: Record<string, boolean>;
  lookupErrors: Record<string, string | null>;
}

/** lookup 字段声明；第二批只声明能力，触发时机和依赖刷新由 Shell 运行时负责。 */
export interface StandardCrudLookupConfig {
  providerKey: string;
  dependsOn?: string[];
  placeholder?: string;
  currentValueLabelField?: string;
}

/** 表单字段声明；元数据只表达字段是什么和允许什么，不承载提交流程。 */
export interface StandardCrudFieldDefinition {
  key: string;
  labelKey: string;
  modelField: string;
  kind: StandardCrudFieldKind;
  placeholder?: string;
  fallbackFields?: string[];
  required?: boolean;
  readOnly?: boolean;
  trim?: boolean;
  emptyAsNull?: boolean;
  maxLength?: number;
  defaultValue?: unknown;
  /** Instant 表单值解释所使用的可信会话 IANA 时区。 */
  timeZoneId?: string;
  options?: StandardCrudOption[];
  lookup?: StandardCrudLookupConfig;
}

/** 表单分区声明；字段布局归属可静态校验，真实渲染和模式切换仍由 Shell 管理。 */
export interface StandardCrudFormSection {
  key: string;
  titleKey: string;
  fields: string[];
}

/** 标准 CRUD 表单模式；详情展示由 Detail View 独立声明，不属于表单模式。 */
export type StandardCrudFormMode = 'create' | 'edit';

/** 标准动作声明；这里只描述入口和权限，不描述执行顺序、回滚或弹窗流程。 */
export interface StandardCrudActionDefinition {
  key: string;
  labelKey: string;
  variant?: 'default' | 'primary' | 'danger';
  standardAction?: 'create' | 'view' | 'edit' | 'refresh' | 'delete' | 'disable' | 'enable' | 'submit';
  confirmMessage?: string;
  confirmMessageKey?: string;
  confirmLabel?: string;
  confirmLabelKey?: string;
  /** 快捷跳转使用目标导航资格，不声明独立业务动作。 */
  navigationTargetKey?: string;
  /** 页动作只消费当前页勾选快照；无选择时禁用。 */
  requiresSelection?: boolean;
  handlerKey?: string;
  inputKey?: string;
}

/** 实例动作状态的受控容器；模块拥有值，宿主只管理生命周期。 */
export interface StandardCrudActionInstance {
  /** 仅当前页面实例和主体有效；结束后的异步工作不能重新取得状态。 */
  readonly active: boolean;
  /** 按模块稳定owner token持有类型化内存状态，实例销毁时调用模块的释放函数。 */
  getOrCreate<T>(owner: symbol, create: () => T, dispose: (value: T) => void): T;
  /** 通知本实例的动作呈现重新读取状态；不刷新数据或执行网络写入。 */
  changed(): void;
}

/** 标准动作上下文；Shell只提供当前资源与受控实例，不暴露刷新策略，实例状态不是服务端资格或HTTP输入。 */
export interface StandardCrudActionContext<TDetail = unknown, TRow = unknown> extends StandardCrudFeatureRequestContext {
  instance?: StandardCrudActionInstance;
  /** 当前受控入口已校验的动作；旧调用方可以省略，不能作为服务端授权凭证。 */
  action?: StandardCrudActionDefinition;
  /** 当前页面定位输入；服务端仍须独立验证，不能作为授权凭证。 */
  queryScope?: Record<string, unknown>;
  id?: string;
  detail?: TDetail;
  row?: TRow;
  selectedRows?: readonly TRow[];
  input?: unknown;
}

/** 模块动作状态只表达通用可见性与可用性；业务事实和组合规则留在模块 resolver 内。 */
export interface StandardCrudActionState {
  visible: boolean;
  available: boolean;
  /** 完整词典键覆盖当前呈现标签；不改变动作身份、功能位置或执行输入。 */
  labelKey?: string;
  /** 旧调用方的说明；提供 reasonKey 后仅保留作诊断，不作为译文兜底。 */
  reason?: string;
  reasonKey?: string;
  args?: Record<string, unknown>;
}

/** 动作状态 resolver 只接收动作声明和当前数据快照，不把模块字段或表达式引入公共契约。 */
export type StandardCrudActionStateResolver<TDetail = unknown, TRow = unknown> = (
  context: Pick<StandardCrudActionContext<TDetail, TRow>, 'detail' | 'row' | 'queryScope' | 'instance'> & { action: StandardCrudActionDefinition },
) => StandardCrudActionState;

/** 异步对象资格由资源 owner 按正式功能位置读取；明确拒绝可隐藏动作，依赖故障必须传播。 */
export type StandardCrudActionEligibilityResolver<TDetail = unknown, TRow = unknown> = (
  context: Pick<StandardCrudActionContext<TDetail, TRow>, 'id' | 'detail' | 'row'> &
    { action: StandardCrudActionDefinition; featurePosition: string; signal: AbortSignal },
) => Promise<StandardCrudActionState>;

/** 模块动作输入渲染上下文；值由 Shell 持有，模块只渲染业务输入并报告变化。 */
export interface StandardCrudActionInputRenderContext extends StandardCrudFeatureRequestContext {
  /** 从活动导航目录取得已允许的读取位置；业务输入不拼接功能位置身份。 */
  navigationFeaturePosition?: (targetKey: string) => string | undefined;
  queryScope?: Record<string, unknown>;
  submitting?: boolean;
  action: StandardCrudActionDefinition;
  value: unknown;
  onChange: (value: unknown) => void;
  detail?: unknown;
  row?: unknown;
  selectedRows?: readonly unknown[];
  /** 共享弹窗据此保护未保存草稿并阻止尚未加载的输入提交。 */
  onDraftStateChange?: (state: { dirty: boolean; ready: boolean }) => void;
}

/** 模块动作输入渲染器；弹层、权限、pending、错误、刷新和关闭生命周期仍归 Shell。 */
export type StandardCrudActionInputRenderer = ((context: StandardCrudActionInputRenderContext) => ReactNode) & {
  /** 新建等业务表单可采用标准 Drawer；省略时沿用 Modal 审阅容器，生命周期仍归宿主。 */
  presentation?: 'modal' | 'drawer';
  /** 输入所有者提供可选宽度；宿主仍负责弹窗与窄屏约束。 */
  dialogWidth?: number;
  /** 只读输入仅展示关闭，不执行动作 handler 或制造一次写入刷新。 */
  readOnly?: boolean;
};

/** 标准动作处理器；执行流程、错误边界和刷新策略由 Shell 运行时收口。 */
export type StandardCrudActionHandler<TDetail = unknown, TRow = unknown> = ((
  context: StandardCrudActionContext<TDetail, TRow>,
) => void | StandardCrudOperationResult | Promise<void | StandardCrudOperationResult>) & {
  /** Owner 同步识别可修改输入解决的错误；仅使409保留输入，不改变原错误或服务端状态。 */
  isCorrectableInputError?: (failure: unknown) => boolean;
};

/** 摘要渲染上下文；模块只接收规范化摘要块和详情数据，不读取 Host 私有状态。 */
export interface StandardCrudSummaryRendererContext<TData = unknown> {
  /** 当前 View 已声明专用动作的正式位置及本次有效授权。 */
  featureActions?: Readonly<Record<string, { position: string; allowed: boolean }>>;
  blockKey: string;
  block: StandardCrudSummaryBlock<TData>;
  title: string;
}

/** 摘要渲染器声明；空态、异常态和装载流程由 Shell 统一处理后再调用模块渲染。 */
export type StandardCrudSummaryRenderer<TData = unknown> = (context: StandardCrudSummaryRendererContext<TData>) => ReactNode;

/** 详情字段渲染上下文；模块扩展只读取当前协议字段和值，不拥有详情结构或装载流程。 */
export interface StandardCrudDetailFieldRendererContext {
  fieldKey: string;
  value: unknown;
  detailFields: Record<string, unknown>;
}

/** 详情字段 renderer binding；选择、顺序、分区和默认 renderer 仍由 Resolved Detail Schema 决定。 */
export type StandardCrudDetailFieldRenderer = (context: StandardCrudDetailFieldRendererContext) => ReactNode;

/** 运行时列只传递可序列化的标准标量描述，不把模块 UI 实现带入 Shell。 */
export interface StandardCrudRuntimeColumn {
  key: string;
  field: string;
  title: string;
  valueKind?: 'text' | 'number' | 'date' | 'datetime' | 'boolean';
  sortable?: boolean;
  hideable?: boolean;
}

/** 一次 Provider 解析得到的完整列集；上下文令牌由模块解释并保持不透明。 */
export interface StandardCrudColumnSet {
  columns: StandardCrudRuntimeColumn[];
  contextToken?: string;
}

/** Provider 接收已提交筛选条件、所属读取位置与取消信号，不读取 Shell 私有状态。 */
export interface StandardCrudColumnContext<TFilter> extends StandardCrudFeatureRequestContext {
  filters: Readonly<TFilter>;
  signal: AbortSignal;
}

/** 模块拥有的运行时列入口；远程协议和业务计算都封装在模块 Adapter 内。 */
export interface StandardCrudListColumnProvider<TFilter> {
  load: (context: StandardCrudColumnContext<TFilter>) => Promise<StandardCrudColumnSet>;
}

/**
 * 模块向 Shell 注册标准 CRUD 资源的公共契约。
 *
 * 第一批承接列表/详情主流程；第二批继续声明字段、表单、lookup、动作行为和 summary
 * renderer 的稳定扩展点。列表操作结构只由 List View 声明，具体加载、执行、状态切换和回滚仍由 Shell 运行时收口。
 */
export interface StandardCrudResourceRegistration<
  TFilter = Record<string, string>,
  TRow = Record<string, unknown>,
  TDetail = StandardCrudDetail,
> {
  resourceKey: string;
  dataAdapter: StandardCrudDataAdapter<TRow, TDetail>;
  listViewKey: string;
  listRoutePath?: string;
  detailViewKey?: string;
  detailRoutePath?: string;
  /** 普通更新需要从已加载详情原样回传的单调递增并发版本字段。 */
  concurrencyRevisionField?: string;
  listPanelTitle?: string;
  // 第二批扩展只声明资源允许的平台化能力；lookup 加载、动作状态、summary 状态和提交流程仍由 Shell 运行时执行。
  fields?: StandardCrudFieldDefinition[];
  formSections?: StandardCrudFormSection[];
  lookupProviders?: StandardCrudLookupProvider[];
  enumOptions?: Record<string, StandardCrudOption[]>;
  actionHandlers?: Record<string, StandardCrudActionHandler<TDetail, TRow>>;
  actionStateResolver?: StandardCrudActionStateResolver<TDetail, TRow>;
  actionEligibilityResolvers?: Partial<Record<string, StandardCrudActionEligibilityResolver<TDetail, TRow>>>;
  actionInputRenderers?: Record<string, StandardCrudActionInputRenderer>;
  detailFieldRenderers?: Record<string, StandardCrudDetailFieldRenderer>;
  summaryRenderers?: Record<string, StandardCrudSummaryRenderer>;
  operationResultPresenters?: StandardCrudOperationResultPresenter[];
  buildMutationValues?: (values: Record<string, unknown>) => StandardCrudMutationValues;
  columnProvider?: StandardCrudListColumnProvider<TFilter>;
  /** 已声明列表字段的模块呈现；不改变字段白名单、排序或筛选语义。 */
  listFieldRenderers?: Record<string, (context: { row: TRow; value: unknown }) => ReactNode>;
}

/** 写入成功后的无详情回执；不得从中推断对象 ID 或读取资格。 */
export interface StandardCrudMutationReceipt { succeeded: true }

/** 标准 CRUD 客户端最小方法集，供 Shell Host 复用统一路径、错误和详情规范化行为。 */
export interface StandardCrudListQueryRequest {
  [key: string]: string | number | boolean | null | undefined;
  pageIndex?: number;
  pageSize?: number;
  sortBy?: string;
  sortDirection?: 'asc' | 'desc';
  fields?: string;
}

/** 正式声明位置随单次请求传输，不进入业务载荷或用户筛选。 */
export interface StandardCrudFeatureRequestContext { featurePosition?: string }
export interface StandardCrudListReadContext extends StandardCrudFeatureRequestContext {
  columnContextToken?: string;
  queryScope?: Record<string, string | null>;
  signal?: AbortSignal;
}

/** 详情取消信号不进入查询 key；功能位置参与缓存隔离但不改变资源标识或 URL。 */
export interface StandardCrudDetailReadContext extends StandardCrudFeatureRequestContext {
  /** 组合动作的模块定位范围；宿主隔离请求，模块解释输入，服务端仍须重验资格。 */
  queryScope?: Record<string, string | null>;
  signal?: AbortSignal;
}

/** 标准 CRUD 客户端最小方法集，供 Shell Host 复用统一路径、错误和详情规范化行为。 */
export interface StandardCrudClient {
  list: (query?: StandardCrudListQueryRequest, readContext?: StandardCrudListReadContext) => Promise<unknown>;
  exportList: (request: StandardCrudExportRequest, context?: StandardCrudFeatureRequestContext) => Promise<StandardCrudExportResponse>;
  getDetail: (id: string, readContext?: StandardCrudDetailReadContext) => Promise<StandardCrudDetail>;
  create: (values: StandardCrudMutationValues, context?: StandardCrudFeatureRequestContext) => Promise<StandardCrudDetail | StandardCrudMutationReceipt>;
  update: (id: string, values: StandardCrudMutationValues, context?: StandardCrudFeatureRequestContext) => Promise<StandardCrudDetail | StandardCrudMutationReceipt>;
  delete: (id: string, context?: StandardCrudFeatureRequestContext) => Promise<void>;
  enable: (id: string, context?: StandardCrudFeatureRequestContext) => Promise<StandardCrudDetail>;
  disable: (id: string, context?: StandardCrudFeatureRequestContext) => Promise<StandardCrudDetail>;
}

/** 一次详情读取同时保留共享呈现数据和模块动作需要的映射值。 */
export interface StandardCrudLoadedDetail<TDetail = StandardCrudDetail> {
  normalized: StandardCrudDetail;
  value: TDetail;
}

/** 共享宿主消费的规范资源数据 Interface。 */
export interface StandardCrudDataAdapter<TRow = Record<string, unknown>, TDetail = StandardCrudDetail> {
  list: (
    query?: StandardCrudListQueryRequest,
    readContext?: StandardCrudListReadContext,
  ) => Promise<StandardCrudListResult<TRow>>;
  get: (id: string, readContext?: StandardCrudDetailReadContext) => Promise<StandardCrudLoadedDetail<TDetail>>;
  create?: (
    values: StandardCrudMutationValues,
    detailFields?: Record<string, unknown>,
    context?: StandardCrudFeatureRequestContext,
  ) => Promise<StandardCrudOperationResult>;
  update?: (
    id: string,
    values: StandardCrudMutationValues,
    detailFields?: Record<string, unknown>,
    context?: StandardCrudFeatureRequestContext,
  ) => Promise<StandardCrudOperationResult>;
  remove?: (id: string, before?: Record<string, unknown>, context?: StandardCrudFeatureRequestContext) => Promise<StandardCrudOperationResult | void>;
  exportList?: (request: StandardCrudExportRequest, context?: StandardCrudFeatureRequestContext) => Promise<StandardCrudExportResponse>;
  getRowId: (row: TRow) => string;
  /** 只读属性打开可使用关联对象主键；行选择和关系动作仍使用 getRowId。 */
  getDetailId?: (row: TRow) => string;
  /** 异构行只返回已登记的详情 View 与真实主键；宿主解析正式路由并重新执行目标准入。 */
  getDetailTarget?: (row: TRow) => { detailViewKey: string; id: string } | undefined;
}

/** 标准 REST 资源总是支持规范写入；其他 Adapter 可以按资源能力选择是否实现。 */
export interface StandardCrudRestDataAdapter<TRow, TDetail = StandardCrudDetail>
  extends StandardCrudDataAdapter<TRow, TDetail> {
  create: (
    values: StandardCrudMutationValues,
    detailFields?: Record<string, unknown>,
    context?: StandardCrudFeatureRequestContext,
  ) => Promise<StandardCrudOperationResult>;
  update: (
    id: string,
    values: StandardCrudMutationValues,
    detailFields?: Record<string, unknown>,
    context?: StandardCrudFeatureRequestContext,
  ) => Promise<StandardCrudOperationResult>;
  remove: (id: string, before?: Record<string, unknown>, context?: StandardCrudFeatureRequestContext) => Promise<StandardCrudOperationResult>;
  exportList: (request: StandardCrudExportRequest, context?: StandardCrudFeatureRequestContext) => Promise<StandardCrudExportResponse>;
}

/** 标准 REST Adapter 的固定配置；模块只提供传输、行映射和稳定标识读取。 */
export interface StandardCrudRestDataAdapterOptions<TRow, TDetail = StandardCrudDetail> {
  apiRoute: string;
  fetcher: typeof fetch;
  mapListItem: (value: unknown) => TRow;
  getRowId: (row: TRow) => string;
  mapDetail?: (detail: StandardCrudDetail) => TDetail;
  /** 模块定义完成本次操作前事实所需字段；不足时在写入之前补读详情。 */
  hasChangeSnapshot?: (fields: Record<string, unknown>) => boolean;
}

/** 标准 CRUD 导出范围；前端只传递用户意图，最终分页边界由后端导出内核裁剪。 */
export type StandardCrudExportMode = 'current' | 'all';

/** 标准 CRUD 导出列请求；来源是 view.columns 的运行时投影，后端仍会按 descriptor 白名单校验。 */
export interface StandardCrudExportColumnRequest {
  key: string;
  field: string;
  title: string;
}

/** 标准 CRUD 导出查询请求；filters 承载当前已应用筛选，pageIndex/pageSize 只对 current 模式生效。 */
export interface StandardCrudExportQueryRequest {
  pageIndex: number;
  pageSize: number;
  sortBy?: string | null;
  sortDirection?: string | null;
  filters: Record<string, string | number | boolean | null | undefined>;
}

/** 标准 CRUD 导出请求；模块不参与执行流程，只通过 metadata 开启能力。 */
export interface StandardCrudExportRequest {
  mode: StandardCrudExportMode;
  columns: StandardCrudExportColumnRequest[];
  query: StandardCrudExportQueryRequest;
}

/** 标准 CRUD 导出响应；fileName 来自后端 Content-Disposition，缺失时由 Host 提供兜底。 */
export interface StandardCrudExportResponse {
  blob: Blob;
  fileName?: string;
}
