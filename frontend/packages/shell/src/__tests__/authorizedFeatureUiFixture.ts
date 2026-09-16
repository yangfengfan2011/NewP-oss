import { vi } from 'vitest';

// These interaction tests supply their authorization input at the UI seam. Explicit placements
// still use the real catalog decision, so the placement-to-request contract remains exercised.
vi.mock('../authorization/FeaturePlacementContext', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../authorization/FeaturePlacementContext')>();
  return {
    ...actual,
    useFeaturePlacement() {
      const features = actual.useFeaturePlacement();
      if (features.placement) return features;
      return {
        ...features,
        read: () => ({ kind: 'allowed' as const, position: 'ui-fixture:read' }),
        action: (key: string, surface: string) => ({ kind: 'allowed' as const, position: `ui-fixture:${surface}:${key}` }),
        navigation: (key: string) => ({ kind: 'allowed' as const, position: `ui-fixture:navigation:${key}` }),
        tab: (key: string) => ({ kind: 'allowed' as const, position: `ui-fixture:tab:${key}` }),
        menu: (position: string) => ({ kind: 'allowed' as const, position }),
      };
    },
  };
});
