import type { FrontendBehaviorBindings, PageProps } from '@icp-platform/sdk';
import { createElement } from 'react';

const loadPart = async () => ({ default: (await import('./partResource')).partResource });

const partPermissionsPage: NonNullable<FrontendBehaviorBindings['pages']>[number] = {
  pageKey: 'part.part.permissions', requiresRecordId: true,
  load: async () => {
    const { ObjectAuthorizationPage } = await import('@icp-platform/ui');
    return { default: (props: PageProps) => createElement(ObjectAuthorizationPage, { ...props,
      target: { resourceKey: 'part.part', id: props.input.recordId, title: '零组件', titleKey: 'Part.Title', name: props.input.context?.label,
        basePath: `/api/part/parts/${encodeURIComponent(props.input.recordId!)}/permissions` } }) };
  },
};

export const partBindings: FrontendBehaviorBindings = {
  behaviorKey: 'part.frontend',
  pages: [partPermissionsPage],
  pageInputResolvers: [{ key: 'part.part.record', pageKeys: ['platform.tabs', 'platform.detail', 'part.part.permissions'],
    resolve: (parameters, context) => {
      const recordId = parameters.recordId ?? context?.context?.id;
      const sourceFeaturePosition = parameters.sourceFeaturePosition ?? context?.context?.sourceFeaturePosition;
      if (typeof recordId !== 'string' || !recordId || typeof sourceFeaturePosition !== 'string' || !sourceFeaturePosition)
        throw new Error('缺少对象及来源读取位置');
      return { parameters: { ...parameters, sourceFeaturePosition }, recordId, queryScope: { sourceFeaturePosition },
        context: { ...(context ?? { nodeKey: recordId, nodeType: 'record', label: String(parameters.name ?? ''), hasChildren: false }),
          context: { id: recordId, sourceFeaturePosition } } };
    } }],
  standardCrudCapabilities: [
    { kind: 'standard-list', targetKey: 'part.part.list', load: loadPart },
    { kind: 'standard-crud-detail', targetKey: 'part.part.detail', load: loadPart },
  ],
};

export default partBindings;
