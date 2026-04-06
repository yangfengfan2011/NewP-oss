import { ModuleRegistry } from '@icp-platform/sdk';

import auditModule from '../../../modules/audit/src/index';
import authorizationModule from '../../../modules/authorization/src/index';
import configCenterModule from '../../../modules/config-center/src/index';
import demoModule from '../../../modules/demo/src/index';
import filesModule from '../../../modules/files/src/index';
import moduleTemplateSampleModule from '../../../modules/module-template-sample/src/index';

/**
 * Shell-level controlled module registration list.
 * Normal module integration should only update this file,
 * instead of modifying other shell internals directly.
 */
const modules = [
  demoModule,
  auditModule,
  authorizationModule,
  configCenterModule,
  filesModule,
  moduleTemplateSampleModule,
];

export function registerShellModules() {
  modules.forEach(module => ModuleRegistry.register(module));
}
