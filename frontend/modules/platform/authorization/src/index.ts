import type { FrontendBehaviorBindings } from '@icp-platform/sdk';
const load = async () => ({ default: (await import('./roleResource')).roleResource });
const authorizationBindings: FrontendBehaviorBindings = {
  behaviorKey: 'authorization.frontend',
  pages: [{ pageKey: 'authorization.workbench', load: () => import('./FeatureWorkbench') },
    { pageKey: 'authorization.dataScope', load: () => import('./DataScopeRulesPage') }],
  standardCrudCapabilities: [
    { kind: 'standard-list', targetKey: 'authorization.role.list', load },
    { kind: 'standard-crud-detail', targetKey: 'authorization.role.detail', load },
  ],
};
export default authorizationBindings;
