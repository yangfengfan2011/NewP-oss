import type { FrontendBehaviorBindings } from '@icp-platform/sdk';

const loadDocument = async () => ({ default: (await import('./documentResource')).documentResource });

export const documentBindings: FrontendBehaviorBindings = {
  behaviorKey: 'document.frontend',
  standardCrudCapabilities: [
    { kind: 'standard-list', targetKey: 'document.document.list', load: loadDocument },
    { kind: 'standard-crud-detail', targetKey: 'document.document.detail', load: loadDocument },
  ],
};

export default documentBindings;
