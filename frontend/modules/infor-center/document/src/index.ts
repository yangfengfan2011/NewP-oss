import { StandardCrudRequestError, type FrontendBehaviorBindings, type PageProps } from '@icp-platform/sdk';
import { createElement } from 'react';

const loadDocument = async () => ({ default: (await import('./documentResource')).documentResource });

const documentPermissionsPage: NonNullable<FrontendBehaviorBindings['pages']>[number] = {
  pageKey: 'document.document.permissions', requiresRecordId: true,
  load: async () => {
    const { ObjectAuthorizationPage } = await import('@icp-platform/ui');
    return { default: (props: PageProps) => createElement(ObjectAuthorizationPage, { ...props,
      target: { resourceKey: 'document.document', id: props.input.recordId, title: '图文档', titleKey: 'Document.Title', name: props.input.context?.label,
        basePath: `/api/document/documents/${encodeURIComponent(props.input.recordId!)}/permissions` } }) };
  },
};

export const documentBindings: FrontendBehaviorBindings = {
  behaviorKey: 'document.frontend',
  pages: [documentPermissionsPage],
  pageInputResolvers: [{ key: 'document.document.historyInput', pageKeys: ['platform.list'],
    resolve: (parameters, context) => {
      const recordId = parameters.recordId ?? context?.context?.id;
      if (typeof recordId !== 'string' || !recordId) throw new StandardCrudRequestError('缺少操作履历对象。', 400, {}, { reasonKey: 'ObjectHistory.TargetRequired' });
      return { parameters: { ...parameters, viewKey: 'document.history.list' }, context, recordId, queryScope: { historyObjectId: recordId } };
    } }, { key: 'document.document.record', pageKeys: ['platform.tabs', 'platform.detail', 'document.document.permissions'],
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
    { kind: 'standard-list', targetKey: 'document.history.list', load: async () => {
      const { createObjectHistoryResource } = await import('@icp-platform/ui');
      return { default: createObjectHistoryResource({ resourceKey: 'document.history', listViewKey: 'document.history.list',
        resolveTarget: scope => {
          const id = scope?.historyObjectId;
          if (typeof id !== 'string' || !id) throw new StandardCrudRequestError('缺少操作履历对象。', 400, {}, { reasonKey: 'ObjectHistory.TargetRequired' });
          return { resourceKey: 'document.document', id, basePath: `/api/document/documents/${encodeURIComponent(id)}/history` };
        } }) };
    } },
    { kind: 'standard-list', targetKey: 'document.document.list', load: loadDocument },
    { kind: 'standard-crud-detail', targetKey: 'document.document.detail', load: loadDocument },
  ],
};

export default documentBindings;
