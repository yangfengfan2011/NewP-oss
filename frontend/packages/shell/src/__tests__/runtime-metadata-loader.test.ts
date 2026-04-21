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
});

