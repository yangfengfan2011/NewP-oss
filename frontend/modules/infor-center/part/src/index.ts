import { StandardCrudRequestError, type FrontendBehaviorBindings, type PageProps } from '@icp-platform/sdk';
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
  pageInputResolvers: [{ key: 'part.part.historyInput', pageKeys: ['platform.list'],
    resolve: (parameters, context) => {
      const recordId = parameters.recordId ?? context?.context?.id;
      if (typeof recordId !== 'string' || !recordId) throw new StandardCrudRequestError('缺少操作履历对象。', 400, {}, { reasonKey: 'ObjectHistory.TargetRequired' });
      return { parameters: { ...parameters, viewKey: 'part.history.list' }, context, recordId, queryScope: { historyObjectId: recordId } };
    } }, { key: 'part.part.record', pageKeys: ['platform.tabs', 'platform.detail', 'part.part.permissions'],
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
    { kind: 'standard-list', targetKey: 'part.history.list', load: async () => {
      const { createObjectHistoryResource } = await import('@icp-platform/ui');
      return { default: createObjectHistoryResource({ resourceKey: 'part.history', listViewKey: 'part.history.list',
        resolveTarget: scope => {
          const id = scope?.historyObjectId;
          if (typeof id !== 'string' || !id) throw new StandardCrudRequestError('缺少操作履历对象。', 400, {}, { reasonKey: 'ObjectHistory.TargetRequired' });
          return { resourceKey: 'part.part', id, basePath: `/api/part/parts/${encodeURIComponent(id)}/history` };
        } }) };
    } },
    { kind: 'standard-list', targetKey: 'part.part.list', load: loadPart },
    { kind: 'standard-crud-detail', targetKey: 'part.part.detail', load: loadPart },
  ],
};

export default partBindings;
