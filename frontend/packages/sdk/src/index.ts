import { ComponentType, ReactNode } from 'react';

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
  permissions?: string[];
}

export interface MenuConfig {
  id: string;
  label: string;
  path?: string;
  icon?: string;
  order?: number;
  permissions?: string[];
  children?: MenuConfig[];
}

export interface PermissionDeclaration {
  code: string;
  name: string;
  description?: string;
  category?: string;
}

export interface I18nResource {
  locale: string;
  messages: Record<string, string>;
}

export interface FrontendModule {
  metadata: ModuleMetadata;
  routes?: RouteConfig[];
  menus?: MenuConfig[];
  permissions?: PermissionDeclaration[];
  i18n?: I18nResource[];
  initialize?: () => void | Promise<void>;
  cleanup?: () => void | Promise<void>;
}

export type ShellExperienceMode = 'hybrid' | 'product' | 'platform';

export interface UiConfig {
  shellExperienceMode: ShellExperienceMode;
}

export type ShellView = 'business' | 'platform';

export class ModuleRegistry {
  private static modules: Map<string, FrontendModule> = new Map();
  private static initialized = false;

  static register(module: FrontendModule): void {
    if (this.modules.has(module.metadata.moduleId)) {
      console.warn(`Module ${module.metadata.moduleId} is already registered`);
      return;
    }
    this.modules.set(module.metadata.moduleId, module);
    console.log(`Module registered: ${module.metadata.moduleId} v${module.metadata.version}`);
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

  static getPermissions(): PermissionDeclaration[] {
    return this.getAllModules().flatMap(m => m.permissions || []);
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
  user?: {
    id: string;
    username: string;
    displayName: string;
    permissions: string[];
  };
  login: (username: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  hasPermission: (permission: string) => boolean;
  hasAnyPermission: (permissions: string[]) => boolean;
  hasAllPermissions: (permissions: string[]) => boolean;
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

export { ComponentType, ReactNode };
