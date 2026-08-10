import type { FrontendBehaviorBindings } from '@icp-platform/sdk';
import IdentityUsersPage from './pages/IdentityUsersPage';

const identityFrontendBindings: FrontendBehaviorBindings = {
  behaviorKey: 'identity.frontend',
  customPages: [
    { pageKey: 'identity.users.page', component: IdentityUsersPage },
  ],
};

export default identityFrontendBindings;
export { identityFrontendBindings };
