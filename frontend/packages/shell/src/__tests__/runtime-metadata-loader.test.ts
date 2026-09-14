import { describe, expect, it, vi } from 'vitest';
import { loadRuntimeMetadata } from '../metadata/runtimeMetadataLoader';

describe('runtime metadata loader', () => {
  it('loads nav, view and model documents from the runtime manifest', async () => {
    const fetchMock = vi.fn(async (url: string) => {
      if (url === '/plugins/metadata-runtime-manifest.json') {
        return new Response(JSON.stringify({
          modules: [
            {
              moduleId: 'Organization',
              metadataBasePath: '/plugins/runtime/modules/Organization/metadata',
              navFiles: ['navigation/Organization.nav.json'],
              viewFiles: ['views/PersonList.view.json'],
              modelFiles: ['models/Person.model.json'],
            },
          ],
        }));
      }

      if (url.endsWith('Organization.nav.json')) {
        return new Response(JSON.stringify({
          type: 'nav',
          navigation: {
            items: [{ key: 'organization.personCenter', titleKey: 'Navigation.PersonCenter', path: '/organization/persons' }],
          },
        }));
      }

      if (url.endsWith('PersonList.view.json')) {
        return new Response(JSON.stringify({ type: 'view', view: { key: 'organization.person.list', mode: 'list' } }));
      }

      return new Response(JSON.stringify({ type: 'model', Person: { kind: 'Entity' } }));
    });

    const result = await loadRuntimeMetadata(fetchMock as typeof fetch);

    expect(result.navDocuments).toHaveLength(1);
    expect(result.viewDocuments).toHaveLength(1);
    expect(result.modelDocuments).toHaveLength(1);
  });

  it('loads documents with bounded concurrency while preserving manifest order', async () => {
    const modelFiles = Array.from({ length: 10 }, (_, index) => `models/Model${index}.model.json`);
    let activeDocumentLoads = 0;
    let maximumConcurrentLoads = 0;
    const fetchMock = vi.fn(async (url: string) => {
      if (url === '/plugins/metadata-runtime-manifest.json') {
        return new Response(JSON.stringify({
          modules: [{
            moduleId: 'Test',
            metadataBasePath: '/plugins/runtime/modules/Test/metadata',
            navFiles: [],
            viewFiles: [],
            modelFiles,
          }],
        }));
      }

      activeDocumentLoads += 1;
      maximumConcurrentLoads = Math.max(maximumConcurrentLoads, activeDocumentLoads);
      const modelIndex = Number.parseInt(/Model(\d+)/.exec(url)?.[1] ?? '-1', 10);
      await new Promise((resolve) => setTimeout(resolve, 10 - modelIndex));
      activeDocumentLoads -= 1;
      return new Response(JSON.stringify({ type: 'model', modelIndex }));
    });

    const result = await loadRuntimeMetadata(fetchMock as typeof fetch);

    expect(maximumConcurrentLoads).toBe(8);
    expect(result.modelDocuments.map((document) => document.modelIndex)).toEqual(
      Array.from({ length: 10 }, (_, index) => index),
    );
  });

  it('shares only an in-flight load and permits a fresh load after completion', async () => {
    const fetchMock = vi.fn(async (url: string) => {
      await new Promise((resolve) => setTimeout(resolve, 1));
      if (url === '/plugins/metadata-runtime-manifest.json') {
        return new Response(JSON.stringify({
          modules: [{
            moduleId: 'Test',
            metadataBasePath: '/plugins/runtime/modules/Test/metadata',
            navFiles: [],
            viewFiles: [],
            modelFiles: ['models/Test.model.json'],
          }],
        }));
      }

      return new Response(JSON.stringify({ type: 'model', name: 'Test' }));
    });

    await Promise.all([
      loadRuntimeMetadata(fetchMock as typeof fetch),
      loadRuntimeMetadata(fetchMock as typeof fetch),
    ]);
    expect(fetchMock).toHaveBeenCalledTimes(2);

    await loadRuntimeMetadata(fetchMock as typeof fetch);
    expect(fetchMock).toHaveBeenCalledTimes(4);
  });

  it('clears a failed in-flight load so a later call can retry', async () => {
    let documentAttempts = 0;
    const fetchMock = vi.fn(async (url: string) => {
      if (url === '/plugins/metadata-runtime-manifest.json') {
        return new Response(JSON.stringify({
          modules: [{
            moduleId: 'Test',
            metadataBasePath: '/plugins/runtime/modules/Test/metadata',
            navFiles: [],
            viewFiles: [],
            modelFiles: ['models/Test.model.json'],
          }],
        }));
      }

      documentAttempts += 1;
      if (documentAttempts === 1) {
        return new Response('', { status: 503 });
      }

      return new Response(JSON.stringify({ type: 'model', name: 'Test' }));
    });

    await expect(loadRuntimeMetadata(fetchMock as typeof fetch)).rejects.toThrow('503');
    const result = await loadRuntimeMetadata(fetchMock as typeof fetch);

    expect(result.modelDocuments).toHaveLength(1);
    expect(documentAttempts).toBe(2);
    expect(fetchMock).toHaveBeenCalledTimes(4);
  });
});


it("resolves navigation feature references only from the compiled catalog", async () => {
  const load = (features: { key: string; kind: string; position: string }[]) => loadRuntimeMetadata(vi.fn(async (url: string) => new Response(JSON.stringify(
    url.endsWith("metadata-runtime-manifest.json")
      ? { modules: [{ moduleId: "Authorization", metadataBasePath: "/metadata", navFiles: ["nav.json"], viewFiles: [], modelFiles: [], features }] }
      : { type: "nav", navigation: { items: [{ key: "workbench", featureKey: "workbench", path: "/authorization/workbench", page: { pageKey: "authorization.workbench" } }] } },
  ))) as typeof fetch);
  const result = await load([{ key: "workbench", kind: "navigation", position: "compiled/full-position" }]);
  expect(result.navDocuments[0].navigation.items[0].featurePosition).toBe("compiled/full-position");
  await expect(load([])).rejects.toThrow("导航功能引用无效");
  await expect(load([{ key: "workbench", kind: "navigation", position: "first" }, { key: "workbench", kind: "navigation", position: "second" }])).rejects.toThrow("导航功能引用无效");
});