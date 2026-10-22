import type { RuntimeViewDocument } from './runtimeMetadataManifest';

type RuntimeViewShape = {
  key?: string;
  mode?: string;
  type?: string;
};

function readViewShape(value: unknown): RuntimeViewShape | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as RuntimeViewShape) : null;
}

function resolveViewDocument(
  viewDocuments: RuntimeViewDocument[],
  viewKey: string,
  expectedMode: 'list' | 'detail',
): RuntimeViewDocument | null {
  return (
    viewDocuments.find((document) => {
      const view = readViewShape(document.view);
      if (!view) {
        return false;
      }

      const viewMode = view.mode;
      const legacyViewType = view.type;
      const resolvedType = viewMode !== undefined ? viewMode : legacyViewType;

      return view.key === viewKey && (resolvedType === undefined || resolvedType === expectedMode);
    }) ?? null
  );
}

export function resolveListViewDocument(
  viewDocuments: RuntimeViewDocument[],
  viewKey: string,
): RuntimeViewDocument | null {
  return resolveViewDocument(viewDocuments, viewKey, 'list');
}

export function resolveDetailViewDocument(
  viewDocuments: RuntimeViewDocument[],
  viewKey: string,
): RuntimeViewDocument | null {
  return resolveViewDocument(viewDocuments, viewKey, 'detail');
}
