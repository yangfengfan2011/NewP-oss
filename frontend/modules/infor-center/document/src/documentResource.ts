import { platformFetch } from '@icp-platform/core';
import { createObjectOwnershipActions } from '@icp-platform/ui';
import { createElement, lazy, Suspense } from 'react';
import {
  createStandardCrudRestDataAdapter,
  isStandardCrudRecord,
  readStandardCrudString,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';

const DocumentFilesSummary = lazy(() => import('./DocumentFilesPanel').then(module => ({ default: module.documentFilesSummaryRenderer })));

// 保留 canonical raw/display 输出，字段与布局由正式 Model/View 投影拥有。
export const documentResource: StandardCrudResourceRegistration = {
  resourceKey: 'document.document',
  listViewKey: 'document.document.list',
  detailViewKey: 'document.document.detail',
  ...createObjectOwnershipActions(({ id, row }) => ({ initialId: id ?? (row as { EID?: string } | undefined)?.EID, pendingPath: '/api/document/documents/ownership/pending',
    statusPath: '/api/document/documents/ownership/{id}/status', savePath: '/api/document/documents/ownership/{id}',
    accountsPath: '/api/document/documents/ownership/accounts', eligibilityPath: '/api/document/documents/ownership/eligibility', batchPath: '/api/document/documents/ownership/initialize' }), '/api/document/documents/ownership/eligibility'),
  summaryRenderers: { files: context => createElement(Suspense, { fallback: null }, createElement(DocumentFilesSummary, context)) },
  dataAdapter: createStandardCrudRestDataAdapter({
    apiRoute: '/api/document/documents',
    fetcher: (input, init) => platformFetch(input, init),
    mapListItem: (value): Record<string, unknown> => {
      const row = isStandardCrudRecord(value) ? value : {};
      return { ...row, id: readStandardCrudString(row, ['EID']) };
    },
    getRowId: row => String(row.id),
  }),
};
