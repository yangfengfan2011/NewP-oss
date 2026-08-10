import { type FrontendBehaviorBindings } from '@icp-platform/sdk';
import TemplatePage from './pages/TemplatePage';

export const templateModuleBindings: FrontendBehaviorBindings = {
  behaviorKey: 'template-module.frontend',
  customPages: [
    {
      pageKey: 'template-module.page',
      component: TemplatePage,
    },
  ],
};

export default templateModuleBindings;
