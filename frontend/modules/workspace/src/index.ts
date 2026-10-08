import type { FrontendBehaviorBindings } from '@icp-platform/sdk';
import { createWorkspaceTreeProvider, workspacePageInputs } from './workspaceTree';

const workspaceBindings: FrontendBehaviorBindings = {
  behaviorKey: 'workspace.frontend',
  treeProviders: [createWorkspaceTreeProvider()],
  pageInputResolvers: workspacePageInputs,
  standardCrudCapabilities: [
    { kind: 'standard-list', targetKey: 'workspace.folder.list', load: () => import('./workspaceRootResource') },
  ],
};
export default workspaceBindings;
