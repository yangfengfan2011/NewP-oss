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
        hasFeature: (position: string) => position === '' || position.startsWith('ui-fixture:'),
        action: (key: string, surface: string) => `ui-fixture:${surface}:${key}`,
        navigation: (key: string) => `ui-fixture:navigation:${key}`,
      };
    },
  };
});
