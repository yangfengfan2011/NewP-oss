import type { FrontendBehaviorBindings } from '@icp-platform/sdk';
const load = async () => ({ default: (await import('./businessChangeResource')).businessChangeResource });
export const auditBindings: FrontendBehaviorBindings = {
  behaviorKey: 'audit.frontend',
  standardCrudCapabilities: [
    { kind: 'standard-list', targetKey: 'audit.change.list', load },
    { kind: 'standard-crud-detail', targetKey: 'audit.change.detail', load },
  ],
};
export default auditBindings;
