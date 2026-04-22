import type { RuntimeViewDocument } from './runtimeMetadataManifest';

type ListViewShape = {
  key?: string;
  mode?: string;
  type?: string;
};

export function resolveListViewDocument(
  viewDocuments: RuntimeViewDocument[],
  viewKey: string,
): RuntimeViewDocument | null {
  return (
    viewDocuments.find((document) => {
      const view = document.view as ListViewShape;
      const viewMode = view.mode;
      const legacyViewType = view.type;
      const resolvedType = viewMode !== undefined ? viewMode : legacyViewType;

      return view.key === viewKey && (resolvedType === undefined || resolvedType === 'list');
    }) ?? null
  );
}
