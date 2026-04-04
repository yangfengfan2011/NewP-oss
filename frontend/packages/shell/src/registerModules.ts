import { ModuleRegistry } from '@icp-platform/sdk';

import auditModule from '../../../modules/audit/src/index';
import authorizationModule from '../../../modules/authorization/src/index';
import configCenterModule from '../../../modules/config-center/src/index';
import demoModule from '../../../modules/demo/src/index';
import filesModule from '../../../modules/files/src/index';

const modules = [
  demoModule,
  auditModule,
  authorizationModule,
  configCenterModule,
  filesModule,
];

export function registerShellModules() {
  modules.forEach(module => ModuleRegistry.register(module));
}
