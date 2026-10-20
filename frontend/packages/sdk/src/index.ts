import { ComponentType, ReactNode } from 'react';
import type { StandardCrudResourceRegistration, StandardCrudDetailReadSource } from './standardCrud.types';

export { createRecoverableCapabilityLoader } from './capabilityLoader';
export type * from './pageComposition';
export { TreeNodeNotFoundError } from './pageComposition';
export { createTreeQueryTransport } from './treeQueryTransport';
import type { PageBinding, PageInputResolverBinding, TreeProviderBinding } from './pageComposition';

export interface ModuleMetadata {
  moduleId: string;
  displayName: string;
  version: string;
  description?: string;
}

export interface RouteConfig {
  path: string;
  component: ComponentType;
  exact?: boolean;
  requireAuth?: boolean;
  featurePosition?: string;
  localAdministratorOnly?: boolean;
}

export interface MenuConfig {
  id: string;
  label: string;
  path?: string;
  icon?: string;
  order?: number;
  featurePosition?: string;
  localAdministratorOnly?: boolean;
  children?: MenuConfig[];
}

export interface StandardListPageDefinition {
  title: string;
  description?: string;
  headerActions?: ReactNode;
  filterBar?: ReactNode;
  content: ReactNode;
  footerLeft?: ReactNode;
  footerRight?: ReactNode;
  advancedFilterOpen?: boolean;
  advancedFilterContent?: ReactNode;
  onAdvancedFilterClose?: () => void;
}

export interface StandardListViewRendererProps {
  viewKey: string;
  viewMetadata: Record<string, unknown>;
  children: (definition: StandardListPageDefinition) => ReactNode;
}

export interface StandardListViewRegistration {
  viewKey: string;
  component: ComponentType<StandardListViewRendererProps>;
}

export interface CustomPageRegistration {
  pageKey: string;
  component: ComponentType;
}

export const ROUTE_CAPABILITY_KINDS = [
  'custom',
  'standard-list',
  'standard-crud-detail',
] as const;

export type RouteCapabilityKind = typeof ROUTE_CAPABILITY_KINDS[number];

export interface RouteCapabilityRef {
  kind: RouteCapabilityKind;
  targetKey: string;
}

export interface RouteCapabilityModule {
  default: ComponentType;
}

export interface RouteCapabilityBinding extends RouteCapabilityRef {
  load: () => Promise<RouteCapabilityModule>;
}

export type StandardCrudCapabilityKind = Extract<
  RouteCapabilityKind,
  'standard-list' | 'standard-crud-detail'
>;

export interface StandardCrudCapabilityModule {
  default: StandardCrudResourceRegistration;
}

/** Standard CRUD 轻量绑定只在授权路由命中后装载可执行资源实现。 */
export interface StandardCrudCapabilityBinding {
  kind: StandardCrudCapabilityKind;
  targetKey: string;
  load: () => Promise<StandardCrudCapabilityModule>;
}

export type {
  UserMessageArgValue,
  UserMessageArgs,
  UserMessageDto,
  UserMessageLevel,
} from './userMessage';

export {
  USER_MESSAGE_LEVELS,
  isUserMessageDto,
} from './userMessage';

export type {
  StandardCrudClient,
  StandardCrudMutationReceipt,
  StandardCrudColumnContext,
  StandardCrudColumnSet,
  StandardCrudDataAdapter,
  StandardCrudActionContext,
  StandardCrudActionInstance,
  StandardCrudActionDefinition,
  ActionDataAccessRequirement,
  StandardCrudActionPrecheck,
  StandardCrudActionHandler,
  StandardCrudActionInputRenderer,
  StandardCrudActionInputRenderContext,
  StandardCrudActionState,
  StandardCrudActionStateResolver,
  StandardCrudActionEligibilityResolver,
  StandardCrudDetail,
  StandardCrudDetailReadContext,
  StandardCrudDetailReadSource,
  StandardCrudFeatureRequestContext,
  StandardCrudDetailFieldRenderer,
  StandardCrudDetailFieldRendererContext,
  StandardCrudExportColumnRequest,
  StandardCrudExportMode,
  StandardCrudExportQueryRequest,
  StandardCrudExportRequest,
  StandardCrudExportResponse,
  StandardCrudFieldDefinition,
  StandardCrudFieldKind,
  StandardCrudFormMode,
  StandardCrudFormSection,
  StandardCrudListColumnProvider,
  StandardCrudListQueryRequest,
  StandardCrudListReadContext,
  StandardCrudListResult,
  StandardCrudLoadedDetail,
  LoadStandardCrudLookupOptionsInput,
  LoadStandardCrudLookupOptionsResult,
  LoadStandardCrudLookupPageInput,
  LoadStandardCrudLookupPageResult,
  StandardCrudLookupConfig,
  StandardCrudLookupExecutionContext,
  StandardCrudLookupPage,
  StandardCrudLookupProvider,
  StandardCrudLookupRequest,
  StandardCrudLookupRuntimeState,
  StandardCrudMutationValues,
  StandardCrudOperationKind,
  StandardCrudOperationResult,
  StandardCrudOperationResultPresenter,
  StandardCrudOperationResultPresenterContext,
  StandardCrudOperationResultPresenterRegistry,
  StandardCrudOption,
  StandardCrudResourceRegistration,
  StandardCrudRuntimeColumn,
  StandardCrudRestDataAdapterOptions,
  StandardCrudRestLookupProviderOptions,
  StandardCrudRestDataAdapter,
  StandardCrudSummaryBlock,
  StandardCrudSummaryRenderer,
  StandardCrudSummaryRendererContext,
  StandardCrudSummaryStatus,
  UseStandardCrudLookupRuntimeInput,
} from './standardCrud.types';

export {
  applyStandardCrudLookupValueChange,
  buildStandardCrudI18nLabel,
  clearStandardCrudDependentLookupValues,
  createStandardCrudOperationResultPresenterRegistry,
  isStandardCrudRecord,
  loadStandardCrudLookupOptions,
  loadStandardCrudLookupPage,
  normalizeStandardCrudSubmitValues,
  normalizeStandardCrudDetail,
  normalizeStandardCrudListResult,
  normalizeStandardCrudSummaryBlock,
  preserveStandardCrudCurrentOption,
  readStandardCrudFieldValue,
  readStandardCrudBoolean,
  readStandardCrudNullableString,
  readStandardCrudString,
  resolveStandardCrudSummaryMessage,
  useStandardCrudLookupRuntime,
} from './standardCrud';

export {
  buildStandardCrudPath,
  createStandardCrudClient,
  createStandardCrudRestDataAdapter,
  createStandardCrudRestLookupProvider,
  StandardCrudRequestError,
  readStandardCrudRequestError,
  standardCrudRequestHeaders,
} from './standardCrudRest';
export type { StandardCrudFieldErrorFact, StandardCrudProblemFact } from './standardCrudRest';
export { standardCrudInputReadSource } from './standardCrudInputReadSource';

export interface I18nResource {
  locale: string;
  messages: Record<string, string>;
}

/**
 * 可执行前端实现只绑定稳定 key；模块身份、路由、菜单与权限由活动运行时目录提供。
 */
export interface FrontendBehaviorBindings {
  behaviorKey: string;
  routeCapabilities?: RouteCapabilityBinding[];
  standardCrudCapabilities?: StandardCrudCapabilityBinding[];
  standardCrudResources?: StandardCrudResourceRegistration[];
  standardCrudDetailReadSources?: StandardCrudDetailReadSource[];
  standardListViews?: StandardListViewRegistration[];
  customPages?: CustomPageRegistration[];
  pages?: PageBinding[];
  pageInputResolvers?: PageInputResolverBinding[];
  treeProviders?: TreeProviderBinding[];
  initialize?: () => void | Promise<void>;
  cleanup?: () => void | Promise<void>;
}

export interface FrontendModule {
  metadata: ModuleMetadata;
  routeCapabilities?: RouteCapabilityBinding[];
  standardCrudCapabilities?: StandardCrudCapabilityBinding[];
  standardCrudResources?: StandardCrudResourceRegistration[];
  standardCrudDetailReadSources?: StandardCrudDetailReadSource[];
  standardListViews?: StandardListViewRegistration[];
  customPages?: CustomPageRegistration[];
  pages?: PageBinding[];
  pageInputResolvers?: PageInputResolverBinding[];
  treeProviders?: TreeProviderBinding[];
  routes?: RouteConfig[];
  menus?: MenuConfig[];
  i18n?: I18nResource[];
  initialize?: () => void | Promise<void>;
  cleanup?: () => void | Promise<void>;
}

export type ShellExperienceMode = 'hybrid' | 'product' | 'platform';

export interface UiConfig {
  shellExperienceMode: ShellExperienceMode;
}

export type ShellView = 'business' | 'platform';

export interface AuthUser {
  /** 当前请求从启用角色并集解析的完整功能位置。 */
  featurePositions?: string[];
  id: string;
  ecode: string;
  ename: string;
  mustChangePassword: boolean;
  revision: number;
  isLocalAdministrator: boolean;
  timeZoneId: string;
}

export class ModuleRegistry {
  private static modules: Map<string, FrontendModule> = new Map();
  private static initialized = false;
  private static activeSnapshotId: string | null = null;

  static register(module: FrontendModule): void {
    if (this.modules.has(module.metadata.moduleId)) {
      console.warn(`Module ${module.metadata.moduleId} is already registered`);
      return;
    }
    this.modules.set(module.metadata.moduleId, module);
    console.log(`Module registered: ${module.metadata.moduleId} v${module.metadata.version}`);
  }

  /**
   * 在所有模块完成装载和校验后一次性替换活动注册表，避免 Shell 观察到部分新快照。
   */
  static replaceAll(modules: FrontendModule[], snapshotId: string): void {
    if (!snapshotId.trim()) {
      throw new Error('MODULE_SNAPSHOT_INVALID: snapshotId is required');
    }

    const nextModules = new Map<string, FrontendModule>();
    const normalizedModuleIds = new Set<string>();
    for (const module of modules) {
      const moduleId = module.metadata?.moduleId?.trim();
      if (!moduleId) {
        throw new Error('MODULE_IDENTITY_INVALID: moduleId is required');
      }

      const normalizedModuleId = moduleId.toLocaleLowerCase('en-US');
      if (normalizedModuleIds.has(normalizedModuleId)) {
        throw new Error(`MODULE_IDENTITY_MISMATCH: duplicate moduleId ${moduleId}`);
      }

      normalizedModuleIds.add(normalizedModuleId);
      nextModules.set(moduleId, module);
    }

    this.modules = nextModules;
    this.activeSnapshotId = snapshotId;
    this.initialized = false;
  }

  static getActiveSnapshotId(): string | null {
    return this.activeSnapshotId;
  }

  static getModule(moduleId: string): FrontendModule | undefined {
    return this.modules.get(moduleId);
  }

  static getAllModules(): FrontendModule[] {
    return Array.from(this.modules.values());
  }

  static getRoutes(): RouteConfig[] {
    return this.getAllModules().flatMap(m => m.routes || []);
  }

  static getMenus(): MenuConfig[] {
    const menus = this.getAllModules().flatMap(m => m.menus || []);
    return menus.sort((a, b) => (a.order || 999) - (b.order || 999));
  }


  static getI18nResources(locale: string): Record<string, string> {
    const resources: Record<string, string> = {};
    this.getAllModules().forEach(module => {
      const i18n = module.i18n?.find(r => r.locale === locale);
      if (i18n) {
        Object.assign(resources, i18n.messages);
      }
    });
    return resources;
  }

  static async initializeAll(): Promise<void> {
    if (this.initialized) {
      return;
    }

    const modules = this.getAllModules();
    for (const module of modules) {
      if (module.initialize) {
        try {
          await module.initialize();
          console.log(`Module initialized: ${module.metadata.moduleId}`);
        } catch (error) {
          console.error(`Failed to initialize module ${module.metadata.moduleId}:`, error);
        }
      }
    }

    this.initialized = true;
  }

  static async cleanupAll(): Promise<void> {
    const modules = this.getAllModules();
    for (const module of modules) {
      if (module.cleanup) {
        try {
          await module.cleanup();
        } catch (error) {
          console.error(`Failed to cleanup module ${module.metadata.moduleId}:`, error);
        }
      }
    }
    this.initialized = false;
  }
}

export interface AuthContext {
  isAuthenticated: boolean;
  isInitialized: boolean;
  user?: AuthUser;
  login: (ecode: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  changePassword: (currentPassword: string, newPassword: string) => Promise<void>;
}

export interface PlatformContext {
  auth: AuthContext;
  uiConfig: UiConfig;
  shellView: ShellView;
  setShellView: (view: ShellView) => void;
  canSwitchShellView: boolean;
  navigate: (path: string) => void;
  showNotification: (message: string, type?: 'info' | 'success' | 'warning' | 'error') => void;
  showDialog: (content: ReactNode, options?: { title?: string; width?: number }) => Promise<boolean>;
}

export type { ComponentType, ReactNode };
