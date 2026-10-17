import { platformFetch } from '@icp-platform/core';
import { createObjectOwnershipActions, createObjectAuthorizationActions } from '@icp-platform/ui';
import { createElement, lazy, Suspense } from 'react';
import {
  createStandardCrudRestDataAdapter,
  isStandardCrudRecord,
  readStandardCrudString,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';

const DocumentFilesSummary = lazy(() => import('./DocumentFilesPanel').then(module => ({ default: module.documentFilesSummaryRenderer })));

const documentDataAdapter = createStandardCrudRestDataAdapter({
  apiRoute: '/api/document/documents',
  fetcher: (input, init) => platformFetch(input, init),
  mapListItem: (value): Record<string, unknown> => {
    const row = isStandardCrudRecord(value) ? value : {};
    return { ...row, id: readStandardCrudString(row, ['EID']) };
  },
  getRowId: row => String(row.id),
});

const ownership = createObjectOwnershipActions(({ id, row }) => ({ initialId: id ?? (row as { EID?: string } | undefined)?.EID, pendingPath: '/api/document/documents/ownership/pending',
  statusPath: '/api/document/documents/ownership/{id}/status', savePath: '/api/document/documents/ownership/{id}',
  accountsPath: '/api/document/documents/ownership/accounts', eligibilityPath: '/api/document/documents/ownership/eligibility', batchPath: '/api/document/documents/ownership/initialize' }), '/api/document/documents/ownership/eligibility');

const authorization = createObjectAuthorizationActions('document.document', context => {
  const fields = isStandardCrudRecord(context.row) ? context.row
    : isStandardCrudRecord(context.detail) && isStandardCrudRecord(context.detail.fields) ? context.detail.fields : {};
  const id = context.id ?? readStandardCrudString(fields, ['EID', 'id']);
  return { id, title: '图文档', titleKey: 'Document.Title', name: readStandardCrudString(fields, ['ENAME']),
    basePath: `/api/document/documents/${encodeURIComponent(id)}/permissions` };
});

// 保留 canonical raw/display 输出，字段与布局由正式 Model/View 投影拥有。
export const documentResource: StandardCrudResourceRegistration = {
  resourceKey: 'document.document',
  listViewKey: 'document.document.list',
  detailViewKey: 'document.document.detail',
  summaryRenderers: { files: context => createElement(Suspense, { fallback: null }, createElement(DocumentFilesSummary, context)) },
  actionHandlers: { ...ownership.actionHandlers, ...authorization.actionHandlers },
  actionInputRenderers: { ...ownership.actionInputRenderers, ...authorization.actionInputRenderers },
  actionEligibilityResolvers: { ...ownership.actionEligibilityResolvers, ...authorization.actionEligibilityResolvers },
  dataAdapter: {
    ...documentDataAdapter,
    get: (id, context) => {
      // 只读页签携带原入口位置；普通详情已有的入口位置仍然优先。
      const sourceFeaturePosition = context?.queryScope?.sourceFeaturePosition;
      return documentDataAdapter.get(id, context?.featurePosition === undefined && typeof sourceFeaturePosition === 'string'
        ? { ...context, featurePosition: sourceFeaturePosition } : context);
    },
  },
};
