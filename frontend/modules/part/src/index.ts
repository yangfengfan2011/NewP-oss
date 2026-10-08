import type { FrontendBehaviorBindings } from '@icp-platform/sdk';

const loadPart = async () => ({ default: (await import('./partResource')).partResource });

export const partBindings: FrontendBehaviorBindings = {
  behaviorKey: 'part.frontend',
  standardCrudCapabilities: [
    { kind: 'standard-list', targetKey: 'part.part.list', load: loadPart },
    { kind: 'standard-crud-detail', targetKey: 'part.part.detail', load: loadPart },
  ],
};

export default partBindings;
