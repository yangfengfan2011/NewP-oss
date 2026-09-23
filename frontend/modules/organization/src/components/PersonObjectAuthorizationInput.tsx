import type { StandardCrudActionInputRenderContext } from '@icp-platform/sdk';
import type { PersonDetailView } from '../services/organizationService';
import OrganizationObjectAuthorizationInput from './OrganizationObjectAuthorizationInput';

/** 人员详情继续提供原有目标文案，并复用组织资源的授权草稿生命周期。 */
export default function PersonObjectAuthorizationInput(context: StandardCrudActionInputRenderContext) {
  const person = context.detail as PersonDetailView | undefined;
  return <OrganizationObjectAuthorizationInput {...context}
    target={{ resourceKey: 'organization.person', title: '人员', id: person?.id, name: person?.name }} />;
}
