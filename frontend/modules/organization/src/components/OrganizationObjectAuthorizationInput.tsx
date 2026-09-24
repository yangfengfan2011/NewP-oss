import { ObjectAuthorizationInput, type ObjectAuthorizationTarget } from '@icp-platform/ui';
import type { StandardCrudActionInputRenderContext } from '@icp-platform/sdk';

/** 组织资源仅提供固定目标，编辑与最终权限解释由通用对象授权界面持有。 */
export default function OrganizationObjectAuthorizationInput(context: StandardCrudActionInputRenderContext & { target: ObjectAuthorizationTarget }) {
  return <ObjectAuthorizationInput {...context} />;
}