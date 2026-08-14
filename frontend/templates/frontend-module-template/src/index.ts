import { createRecoverableCapabilityLoader, type FrontendBehaviorBindings } from '@icp-platform/sdk';

const loadTemplatePage = createRecoverableCapabilityLoader(
  async () => ({ default: (await import('./TemplatePage.primary')).primary }),
  async () => ({ default: (await import('./TemplatePage.recovery')).recovery }),
);

export const templateModuleBindings: FrontendBehaviorBindings = {
  behaviorKey: 'template-module.frontend',
  routeCapabilities: [
    {
      kind: 'custom',
      targetKey: 'template-module.page',
      load: loadTemplatePage,
    },
  ],
};

export default templateModuleBindings;
