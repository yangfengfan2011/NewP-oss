import { useEffect } from 'react';
import type { PageProps } from '@icp-platform/sdk';
import ObjectAuthorizationInput, { type ObjectAuthorizationTarget } from './ObjectAuthorizationInput';

const unchanged = () => {};

/** 只读页签复用已有解释界面；维护仍由所属资源的标准动作持有。 */
export default function ObjectAuthorizationPage({ input, reportState, target }: PageProps & { target: ObjectAuthorizationTarget }) {
  useEffect(() => reportState({ dirty: false, submitting: false }), [reportState]);
  return <ObjectAuthorizationInput target={target} action={{ key: 'viewAuthorization', labelKey: 'DataScope.ObjectAuthorization', inputKey: 'objectAuthorizationDetails' }}
    featurePosition={typeof input.parameters.sourceFeaturePosition === 'string' ? input.parameters.sourceFeaturePosition : undefined}
    value={undefined} queryScope={input.queryScope} dataAccess={input.dataAccess} onChange={unchanged} />;
}
