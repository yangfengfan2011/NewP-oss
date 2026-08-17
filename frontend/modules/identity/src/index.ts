import {
  createRecoverableCapabilityLoader,
  type FrontendBehaviorBindings,
  type StandardCrudCapabilityModule,
} from '@icp-platform/sdk';

const loadIdentityAccountStandardCrudResource = createRecoverableCapabilityLoader<StandardCrudCapabilityModule>(
  async () => ({
    default: (await import('./standard-crud/identityAccountStandardCrudResource.primary')).primary,
  }),
  async () => ({
    default: (await import('./standard-crud/identityAccountStandardCrudResource.recovery')).recovery,
  }),
);

const identityFrontendBindings: FrontendBehaviorBindings = {
  behaviorKey: 'identity.frontend',
  routeCapabilities: [
    {
      kind: 'custom',
      targetKey: 'identity.accounts.page',
      load: () => import('./pages/IdentityAccountsPage'),
    },
  ],
  standardCrudCapabilities: [
    {
      kind: 'standard-list',
      targetKey: 'identity.account.list',
      load: loadIdentityAccountStandardCrudResource,
    },
  ],
};

export default identityFrontendBindings;
export { identityFrontendBindings };
