import { platformFetch } from '@icp-platform/core';
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
