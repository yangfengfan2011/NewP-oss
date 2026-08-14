import { type FrontendBehaviorBindings } from '@icp-platform/sdk';

export const templateModuleBindings: FrontendBehaviorBindings = {
  behaviorKey: 'template-module.frontend',
  routeCapabilities: [
    {
      kind: 'custom',
      targetKey: 'template-module.page',
      load: () => import('./pages/TemplatePage'),
    },
  ],
};

export default templateModuleBindings;
