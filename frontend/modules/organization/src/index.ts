import { type FrontendBehaviorBindings, type StandardCrudResourceRegistration } from '@icp-platform/sdk';
import OrganizationTreePage from './pages/OrganizationTreePage';
import PositionManagementPage from './pages/PositionManagementPage';
import { personStandardCrudResource } from './standard-crud/personStandardCrudResource';

export const organizationBindings: FrontendBehaviorBindings = {
  behaviorKey: 'organization.frontend',
  standardCrudResources: [
    personStandardCrudResource as unknown as StandardCrudResourceRegistration,
  ],
  customPages: [
    {
      pageKey: 'organization.tree.page',
      component: OrganizationTreePage,
    },
    {
      pageKey: 'organization.positions.page',
      component: PositionManagementPage,
    },
  ],
};

export default organizationBindings;
