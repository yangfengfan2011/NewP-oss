import type { FrontendBehaviorBindings } from '@icp-platform/sdk';

const identityFrontendBindings: FrontendBehaviorBindings = {
  behaviorKey: 'identity.frontend',
  routeCapabilities: [
    {
      kind: 'custom',
      targetKey: 'identity.accounts.page',
      load: () => import('./pages/IdentityAccountsPage'),
    },
  ],
};

export default identityFrontendBindings;
export { identityFrontendBindings };
