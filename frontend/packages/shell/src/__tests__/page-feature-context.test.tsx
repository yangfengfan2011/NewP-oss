// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, onTestFinished } from 'vitest';
import { LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry, type PageProps } from '@icp-platform/sdk';
import { PageOutlet } from '../composition/PageOutlet';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

it.each([true, false])('custom pages receive only the resolved entry feature context, placement=%s', async hasPlacement => {
  const Page = ({ input }: PageProps) => <output>{String(input.parameters.featurePosition ?? 'none')}</output>;
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'feature-context', displayName: 'Feature context', version: '1' },
    pages: [{ pageKey: 'feature-context.page', load: async () => ({ default: Page }) }],
    pageInputResolvers: [{ key: 'feature-context.input', pageKeys: ['feature-context.page'],
      resolve: () => ({ parameters: { featurePosition: 'unselected-input' } }) }],
  }], 'feature-context');
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  onTestFinished(async () => {
    await act(async () => root.unmount()); container.remove(); ModuleRegistry.replaceAll([], 'feature-context-cleanup');
  });
  await act(async () => root.render(
    <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}>
      <PageOutlet page={{ pageKey: 'feature-context.page', inputResolverKey: 'feature-context.input' }}
        featurePlacement={hasPlacement ? { entryPosition: 'selected.entry' } : undefined} />
    </LocalizationProvider>,
  ));
  expect(container.querySelector('output')?.textContent).toBe(hasPlacement ? 'selected.entry' : 'none');
});
