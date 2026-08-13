import type { FrontendBehaviorBindings } from '@icp-platform/sdk';
import IdentityAccountsPage from './pages/IdentityAccountsPage';

const identityFrontendBindings: FrontendBehaviorBindings = {
  behaviorKey: 'identity.frontend',
  customPages: [
    { pageKey: 'identity.accounts.page', component: IdentityAccountsPage },
  ],
};

export default identityFrontendBindings;
export { identityFrontendBindings };
