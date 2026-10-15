export interface StandardCrudColumnStateItem {
  show?: boolean;
  width?: number;
  order?: number;
  disable?: boolean;
}

export type StandardCrudColumnState = Record<string, StandardCrudColumnStateItem>;

const STORAGE_PREFIX = 'icp.standard-crud.columns';

function cloneColumnState(value: StandardCrudColumnState): StandardCrudColumnState {
  return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, { ...item }]));
}

export function createStandardCrudColumnPreferenceKey(subjectId: string, viewKey: string): string {
  return `${STORAGE_PREFIX}:${encodeURIComponent(subjectId)}:${encodeURIComponent(viewKey)}`;
}

export function loadStandardCrudColumnPreference(
  storage: Storage,
  subjectId: string,
  viewKey: string,
  defaults: StandardCrudColumnState,
): StandardCrudColumnState {
  const raw = storage.getItem(createStandardCrudColumnPreferenceKey(subjectId, viewKey));
  if (!raw) {
    return cloneColumnState(defaults);
  }

  try {
    const parsed = JSON.parse(raw) as unknown;
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
      return cloneColumnState(defaults);
    }

    const restored: StandardCrudColumnState = cloneColumnState(defaults);
    for (const [key, value] of Object.entries(parsed as Record<string, unknown>)) {
      if (!value || typeof value !== 'object' || Array.isArray(value) || !(key in defaults)) {
        continue;
      }
      const item = value as Record<string, unknown>;
      restored[key] = {
        ...(typeof item.width === 'number' && Number.isFinite(item.width) && item.width > 0
          ? { width: item.width } : defaults[key]?.width !== undefined ? { width: defaults[key].width } : {}),
        ...(defaults[key]?.disable === true
          ? { show: true }
          : typeof item.show === 'boolean' ? { show: item.show } : {}),
        ...(typeof item.order === 'number' && Number.isFinite(item.order) ? { order: item.order } : {}),
        ...(defaults[key]?.disable === true ? { disable: true } : {}),
      };
    }
    return restored;
  } catch {
    return cloneColumnState(defaults);
  }
}

export function saveStandardCrudColumnPreference(
  storage: Storage,
  subjectId: string,
  viewKey: string,
  state: StandardCrudColumnState,
): void {
  storage.setItem(createStandardCrudColumnPreferenceKey(subjectId, viewKey), JSON.stringify(state));
}

export function resetStandardCrudColumnPreference(
  storage: Storage,
  subjectId: string,
  viewKey: string,
  defaults: StandardCrudColumnState,
): StandardCrudColumnState {
  storage.removeItem(createStandardCrudColumnPreferenceKey(subjectId, viewKey));
  return cloneColumnState(defaults);
}
