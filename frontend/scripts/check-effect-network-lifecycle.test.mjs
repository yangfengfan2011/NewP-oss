import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import test from 'node:test';
import {
  analyzeEffectNetworkLifecycleSource,
  analyzeEffectNetworkLifecycleTree,
  evaluateEffectNetworkLifecycle,
  runEffectNetworkLifecycleGate,
} from './check-effect-network-lifecycle.mjs';
import { effectNetworkLifecycleAllowlist } from './effect-network-lifecycle.allowlist.mjs';

test('the real Workspace edit-values POST is a reviewed read query at its exact effect callsite', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'modules/infor-center/workspace/src/WorkspaceObjectEditInput.tsx',
    source: readFileSync(new URL('../modules/infor-center/workspace/src/WorkspaceObjectEditInput.tsx', import.meta.url), 'utf8'),
  });
  assert.equal(findings.length, 1);
  const finding = findings[0];
  assert.equal(finding.classification, 'read-query');
  assert.equal(finding.id, 'modules/infor-center/workspace/src/WorkspaceObjectEditInput.tsx::WorkspaceObjectEditInput::useEffect::platformFetch');
  assert.deepEqual(evaluateEffectNetworkLifecycle({ findings, allowlist: [] }).diagnostics,
    [{ code: 'EFFECT_NETWORK_NOT_ALLOWLISTED', id: finding.id }]);
  const reviewed = effectNetworkLifecycleAllowlist.filter(entry => entry.id === finding.id);
  assert.equal(reviewed.length, 1);
  assert.equal(evaluateEffectNetworkLifecycle({ findings, allowlist: reviewed }).ok, true);
});

test('Workspace write endpoints and unknown edit-values transport shapes remain write commands', () => {
  const inputs = [
    ['POST', "'/api/workspace/content/folders'"],
    ['POST', "'/api/workspace/content/parts'"],
    ['POST', "'/api/workspace/content/documents'"],
    ['POST', "'/api/workspace/content/delete'"],
    ['PUT', '`/api/workspace/content/${encodeURIComponent(target.LID)}/edit-values`'],
    ['DELETE', '`/api/workspace/content/${encodeURIComponent(target.LID)}/edit-values`'],
    ['POST', '`/api/workspace/content/${target.LID}/edit-values`'],
    ['POST', '`/api/workspace/content/${encodeURIComponent(target.LID)}/edit-values/extra`'],
    ['POST', '`/api/other/content/${encodeURIComponent(target.LID)}/edit-values`'],
    ['POST', '`/api/workspace/content/${encodeURIComponent(target.LID)}${suffix}/edit-values`'],
    ['POST', '`/api/workspace/content/${encodeURIComponent(...ids)}/edit-values`'],
    ['POST', '`/api/workspace/content/${encodeURIComponent?.(target.LID)}/edit-values`'],
    ['POST', '`/api/workspace/content/${encodeURIComponent(target.LID)}/edit-values`', '...options'],
    ['POST', 'queryUrl'],
  ];
  for (const [method, input, additionalInit = ''] of inputs) {
    const findings = analyzeEffectNetworkLifecycleSource({
      file: 'modules/workspace/src/WriteProbe.tsx',
      source: `import { useEffect } from 'react';
        import { platformFetch } from '@icp-platform/core';
        export function WriteProbe() {
          useEffect(() => { void platformFetch(${input}, { method: '${method}', ${additionalInit} }); }, []);
        }`,
    });
    assert.equal(findings.length, 1, input);
    assert.equal(findings[0].classification, 'write-command', `${method} ${input}`);
    assert.deepEqual(evaluateEffectNetworkLifecycle({ findings, allowlist: [] }).diagnostics,
      [{ code: 'EFFECT_NETWORK_WRITE_COMMAND', id: findings[0].id }]);
  }
});

test('reports a nested platformFetch query started by useEffect', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'modules/example/src/ExamplePage.tsx',
    source: `
      import { useEffect as afterCommit } from 'react';
      import { platformFetch } from '@icp-platform/core';

      export function ExamplePage() {
        afterCommit(() => {
          async function load() {
            await platformFetch('/api/example');
          }

          void load();
        }, []);
        return null;
      }
    `,
  });

  assert.deepEqual(findings, [
    {
      id: 'modules/example/src/ExamplePage.tsx::ExamplePage::useEffect::load>platformFetch',
      file: 'modules/example/src/ExamplePage.tsx',
      enclosingFunction: 'ExamplePage',
      hook: 'useEffect',
      triggerChain: 'load>platformFetch',
      classification: 'read-query',
      line: 8,
    },
  ]);
});

test('recognizes React namespace useLayoutEffect', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'packages/ui/src/NamespaceProbe.tsx',
    source: `import * as React from 'react';
      import { platformFetch } from '@icp-platform/core';

      export function NamespaceProbe() {
        React.useLayoutEffect(() => {
          void platformFetch('/api/example');
        }, []);
        return null;
      }
    `,
  });

  assert.deepEqual(findings.map(({ id, classification }) => ({ id, classification })), [
    {
      id: 'packages/ui/src/NamespaceProbe.tsx::NamespaceProbe::useLayoutEffect::platformFetch',
      classification: 'read-query',
    },
  ]);
});

test('follows a local effect callback through useCallback helpers without looping on cycles', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'packages/core/src/PermissionProbe.tsx',
    source: `import { useCallback, useEffect } from 'react';

      export function PermissionProbe() {
        const fetchPermissions = useCallback(async () => {
          await fetch('/api/auth/current-user');
        }, []);
        function first() {
          second();
        }
        function second() {
          fetchPermissions();
          first();
        }
        useEffect(first, [first]);
        return null;
      }
    `,
  });

  assert.deepEqual(findings.map(({ id, classification }) => ({ id, classification })), [
    {
      id: 'packages/core/src/PermissionProbe.tsx::PermissionProbe::useEffect::first>second>fetchPermissions>fetch',
      classification: 'read-query',
    },
  ]);
});

test('reports a provider load reached through a local member alias', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'packages/shell/src/RuntimeColumnProbe.tsx',
    source: `import { useEffect } from 'react';

      export function RuntimeColumnProbe({ provider }) {
        useEffect(() => {
          const loadColumns = provider.load;
          async function resolveColumns() {
            await loadColumns({ filters: {} });
          }
          void resolveColumns();
        }, [provider]);
        return null;
      }
    `,
  });

  assert.deepEqual(findings.map(({ id, classification }) => ({ id, classification })), [
    {
      id: 'packages/shell/src/RuntimeColumnProbe.tsx::RuntimeColumnProbe::useEffect::resolveColumns>loadColumns(provider.load)',
      classification: 'read-query',
    },
  ]);
});

test('reports each Standard CRUD detail read path reached from an effect', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'packages/shell/src/DetailRouteProbe.tsx',
    source: `import { useEffect } from 'react';

      export function DetailRouteProbe({ dataAdapter, id }) {
        function openViewDrawer() {
          return dataAdapter.get(id);
        }
        function openEditDrawer() {
          return openViewDrawer();
        }
        useEffect(() => {
          void openViewDrawer();
          void openEditDrawer();
        }, [id]);
        return null;
      }
    `,
  });

  assert.deepEqual(findings.map(({ id, classification }) => ({ id, classification })), [
    {
      id: 'packages/shell/src/DetailRouteProbe.tsx::DetailRouteProbe::useEffect::openViewDrawer>dataAdapter.get',
      classification: 'read-query',
    },
    {
      id: 'packages/shell/src/DetailRouteProbe.tsx::DetailRouteProbe::useEffect::openEditDrawer>openViewDrawer>dataAdapter.get',
      classification: 'read-query',
    },
  ]);
});

test('treats an effect-owned request callback as a read query sink', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'packages/ui/src/SearchTableProbe.tsx',
    source: `import { useEffect } from 'react';

      export function SearchTableProbe({ request, query }) {
        useEffect(() => {
          void Promise.resolve().then(() => request(query));
        }, [query, request]);
        return null;
      }
    `,
  });

  assert.deepEqual(findings.map(({ id, classification }) => ({ id, classification })), [
    {
      id: 'packages/ui/src/SearchTableProbe.tsx::SearchTableProbe::useEffect::request',
      classification: 'read-query',
    },
  ]);
});

test('reports an aliased API client function imported into an effect helper', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'modules/platform/identity/src/SecurityEventsProbe.tsx',
    source: `import { useEffect } from 'react';
      import { listAccountSecurityEvents as listEvents } from './api/identityClient';

      export function SecurityEventsProbe() {
        useEffect(() => {
          async function loadPage() {
            await listEvents('Account_1');
          }
          void loadPage();
        }, []);
        return null;
      }
    `,
  });

  assert.deepEqual(findings.map(({ id, classification }) => ({ id, classification })), [
    {
      id: 'modules/platform/identity/src/SecurityEventsProbe.tsx::SecurityEventsProbe::useEffect::loadPage>listEvents',
      classification: 'read-query',
    },
  ]);
});

test('classifies literal HTTP methods and fails closed for a computed method', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'modules/example/src/TransportProbe.tsx',
    source: `import { useEffect } from 'react';
      import { platformFetch } from '@icp-platform/core';

      export function ReadProbe() {
        useEffect(() => { void fetch('/api/items', { method: 'GET' }); }, []);
        return null;
      }
      export function WriteProbe() {
        useEffect(() => { void platformFetch('/api/items', { method: 'POST' }); }, []);
        return null;
      }
      export function UnknownProbe({ method }) {
        useEffect(() => { void fetch('/api/items', { method }); }, [method]);
        return null;
      }
    `,
  });

  assert.deepEqual(findings.map(({ enclosingFunction, classification }) => ({ enclosingFunction, classification })), [
    { enclosingFunction: 'ReadProbe', classification: 'read-query' },
    { enclosingFunction: 'WriteProbe', classification: 'write-command' },
    { enclosingFunction: 'UnknownProbe', classification: 'unknown-network' },
  ]);
});

test('classifies the real object-authorization transport signature and its import alias without trusting unknown init', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'packages/ui/src/WrapperProbe.tsx',
    source: `import { useEffect } from 'react';
      import { platformFetch } from '@icp-platform/core';
      import { fetchObjectAuthorization, fetchObjectAuthorization as requestObject } from './objectAuthorizationRequest';
      export function ReadProbe() { useEffect(() => { void fetchObjectAuthorization(platformFetch, '/catalog', { signal: undefined, headers: {} }); }, []); }
      export function HeadProbe() { useEffect(() => { void fetchObjectAuthorization(platformFetch, '/catalog', { method: 'HEAD' }); }, []); }
      export function WriteProbe() { useEffect(() => { void fetchObjectAuthorization(platformFetch, '/objects/id', { method: 'POST', body: '{}' }); }, []); }
      export function AliasProbe() { useEffect(() => { void requestObject(platformFetch, '/catalog', { method: 'GET' }); }, []); }
      export function UnknownProbe({ method }) { useEffect(() => { void requestObject(platformFetch, '/objects/id', { method }); }, [method]); }
      export function OpaqueProbe({ init }) { useEffect(() => { void requestObject(platformFetch, '/objects/id', init); }, [init]); }
      export function SpreadProbe({ init }) { useEffect(() => { void requestObject(platformFetch, '/objects/id', { ...init }); }, [init]); }
    `,
  });
  assert.deepEqual(findings.map(({ enclosingFunction, classification }) => ({ enclosingFunction, classification })), [
    { enclosingFunction: 'ReadProbe', classification: 'read-query' },
    { enclosingFunction: 'HeadProbe', classification: 'read-query' },
    { enclosingFunction: 'WriteProbe', classification: 'write-command' },
    { enclosingFunction: 'AliasProbe', classification: 'read-query' },
    { enclosingFunction: 'UnknownProbe', classification: 'unknown-network' },
    { enclosingFunction: 'OpaqueProbe', classification: 'unknown-network' },
    { enclosingFunction: 'SpreadProbe', classification: 'unknown-network' },
  ]);
});

test('does not treat a same-named callable from another source as the reviewed transport wrapper', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'modules/example/src/Probe.tsx',
    source: `import { useEffect } from 'react';
      import { fetchObjectAuthorization } from './objectAuthorizationRequest';
      export function Probe() { useEffect(() => { void fetchObjectAuthorization(fetch, '/items', { method: 'GET' }); }, []); }
    `,
  });
  assert.deepEqual(findings, []);
});

test('rejects object-authorization wrapper writes and unknown methods in platform query loads', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'packages/ui/src/WrapperQueryProbe.tsx',
    source: `import { platformFetch, usePlatformQuery } from '@icp-platform/core';
      import { fetchObjectAuthorization as requestObject } from './objectAuthorizationRequest';
      export function Probe({ method }) {
        const read = usePlatformQuery({ key: ['read'], load: () => requestObject(platformFetch, '/catalog', { method: 'GET' }) });
        const write = usePlatformQuery({ key: ['write'], load: () => requestObject(platformFetch, '/objects/id', { method: 'POST' }) });
        const unknown = usePlatformQuery({ key: ['unknown'], load: () => requestObject(platformFetch, '/objects/id', { method }) });
        return null;
      }
    `,
  });
  assert.deepEqual(findings.map(({ enclosingFunction, hook, classification }) => ({ enclosingFunction, hook, classification })), [
    { enclosingFunction: 'read', hook: 'usePlatformQuery.load', classification: 'read-query' },
    { enclosingFunction: 'write', hook: 'usePlatformQuery.load', classification: 'write-command' },
    { enclosingFunction: 'unknown', hook: 'usePlatformQuery.load', classification: 'unknown-network' },
  ]);
  assert.equal(evaluateEffectNetworkLifecycle({ findings: [findings[0]], allowlist: [] }).ok, true);
  assert.equal(evaluateEffectNetworkLifecycle({ findings: [findings[1]], allowlist: [] }).ok, false);
  assert.equal(evaluateEffectNetworkLifecycle({ findings: [findings[2]], allowlist: [] }).ok, false);
});

test('classifies explicit data adapter, client, service, and provider member sinks', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'modules/example/src/MemberProbe.tsx',
    source: `import { useEffect } from 'react';

      export function MemberProbe({ dataAdapter, identityClient, organizationService, provider }) {
        useEffect(() => {
          void dataAdapter.delete('1');
          void identityClient.create({});
          void organizationService.list({});
          void provider.load({});
        }, [dataAdapter, identityClient, organizationService, provider]);
        return null;
      }
    `,
  });

  assert.deepEqual(findings.map(({ triggerChain, classification }) => ({ triggerChain, classification })), [
    { triggerChain: 'dataAdapter.delete', classification: 'write-command' },
    { triggerChain: 'identityClient.create', classification: 'write-command' },
    { triggerChain: 'organizationService.list', classification: 'read-query' },
    { triggerChain: 'provider.load', classification: 'read-query' },
  ]);
});

test('uses imported API symbol semantics even when the local name is aliased', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'modules/platform/identity/src/CommandProbe.tsx',
    source: `import { useEffect } from 'react';
      import { setAccountStatus as executeCommand, run as opaqueCall } from './api/identityClient';

      export function CommandProbe() {
        useEffect(() => {
          void executeCommand('Account_1');
          void opaqueCall();
        }, []);
        return null;
      }
    `,
  });

  assert.deepEqual(findings.map(({ triggerChain, classification }) => ({ triggerChain, classification })), [
    { triggerChain: 'executeCommand', classification: 'write-command' },
    { triggerChain: 'opaqueCall', classification: 'unknown-network' },
  ]);
});

test('reports declarative platform query reads without treating non-network effects as sinks', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'packages/shell/src/MetadataListProbe.tsx',
    source: `import { useEffect, useLayoutEffect } from 'react';
      import { usePlatformQuery } from '@icp-platform/core';
      import { loadStandardCrudColumnPreference } from '@icp-platform/ui/crud-list';

      const loadFormRuntime = () => import('./FormRuntime');
      export function MetadataListProbe({ dataAdapter, runtime, query }) {
        const result = usePlatformQuery({
          key: ['standard-crud.list', query],
          load: ({ signal }) => dataAdapter.list(query, { signal }),
        });
        useEffect(() => {
          loadStandardCrudColumnPreference(localStorage, 'User_1', 'people');
          void loadFormRuntime();
          document.addEventListener('click', () => undefined);
        }, []);
        useLayoutEffect(() => {
          runtime.reset();
        }, [runtime]);
        return result.data ?? null;
      }
    `,
  });

  assert.deepEqual(findings.map(({ hook, triggerChain, classification }) => ({ hook, triggerChain, classification })), [
    { hook: 'usePlatformQuery.load', triggerChain: 'dataAdapter.list', classification: 'read-query' },
  ]);
});

test('rejects a platformFetch POST declared by a platform query load', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'modules/example/src/QueryWriteProbe.tsx',
    source: `import { usePlatformQuery } from '@icp-platform/core';
      import { platformFetch } from '@icp-platform/core';

      export function QueryWriteProbe() {
        return usePlatformQuery({
          key: ['example.items'],
          load: () => platformFetch('/api/items', { method: 'POST' }),
        });
      }
    `,
  });

  const result = evaluateEffectNetworkLifecycle({ findings, allowlist: [] });

  assert.deepEqual(findings.map(({ hook, triggerChain, classification }) => ({ hook, triggerChain, classification })), [
    { hook: 'usePlatformQuery.load', triggerChain: 'platformFetch', classification: 'write-command' },
  ]);
  assert.deepEqual(result.diagnostics, [{
    code: 'PLATFORM_QUERY_LOAD_WRITE_COMMAND',
    id: 'modules/example/src/QueryWriteProbe.tsx::QueryWriteProbe::usePlatformQuery.load::platformFetch',
  }]);
});

test('recognizes the SDK lookup option loader as a read-only platform query load', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'packages/shell/src/standard-crud/LookupRuntimeProbe.tsx',
    source: `import { usePlatformQuery } from '@icp-platform/core';
      import { loadStandardCrudLookupOptions } from '@icp-platform/sdk';

      export function LookupRuntimeProbe(input) {
        return usePlatformQuery({
          key: ['standard-crud.lookup', input.resourceKey],
          load: ({ signal }) => loadStandardCrudLookupOptions(input, { signal }),
        });
      }
    `,
  });

  assert.deepEqual(findings.map(({ hook, triggerChain, classification }) => ({ hook, triggerChain, classification })), [
    {
      hook: 'usePlatformQuery.load',
      triggerChain: 'loadStandardCrudLookupOptions',
      classification: 'read-query',
    },
  ]);
});

test('recognizes the SDK paged lookup loader as a read-only platform query load', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'packages/shell/src/standard-crud/PagedLookupRuntimeProbe.tsx',
    source: `import { usePlatformQuery } from '@icp-platform/core';
      import { loadStandardCrudLookupPage } from '@icp-platform/sdk';

      export function PagedLookupRuntimeProbe(input) {
        return usePlatformQuery({
          key: ['standard-crud.lookup-page', input.resourceKey],
          load: ({ signal }) => loadStandardCrudLookupPage(input, { signal }),
        });
      }
    `,
  });

  assert.deepEqual(findings.map(({ hook, triggerChain, classification }) => ({ hook, triggerChain, classification })), [
    {
      hook: 'usePlatformQuery.load',
      triggerChain: 'loadStandardCrudLookupPage',
      classification: 'read-query',
    },
  ]);
});

test('rejects dataAdapter.create reached through a referenced platform query load', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'modules/example/src/AdapterWriteProbe.tsx',
    source: `import { usePlatformQuery } from '@icp-platform/core';

      export function AdapterWriteProbe({ dataAdapter }) {
        const createItem = () => dataAdapter.create({ name: 'new item' });
        return usePlatformQuery({
          key: ['example.items'],
          load: createItem,
        });
      }
    `,
  });

  const result = evaluateEffectNetworkLifecycle({ findings, allowlist: [] });

  assert.deepEqual(findings.map(({ hook, triggerChain, classification }) => ({ hook, triggerChain, classification })), [
    {
      hook: 'usePlatformQuery.load',
      triggerChain: 'createItem>dataAdapter.create',
      classification: 'write-command',
    },
  ]);
  assert.deepEqual(result.diagnostics.map(({ code }) => code), ['PLATFORM_QUERY_LOAD_WRITE_COMMAND']);
});

test('rejects a client delete through two cyclic helpers and a destructured member alias', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'modules/example/src/NestedDeleteProbe.tsx',
    source: `import { usePlatformQuery } from '@icp-platform/core';

      export function NestedDeleteProbe({ client }) {
        const { delete: removeItem } = client;
        function first() {
          second();
        }
        function second() {
          removeItem('Item_1');
          first();
        }
        const loadItems = () => first();
        return usePlatformQuery({ key: ['example.items'], load: loadItems });
      }
    `,
  });

  const result = evaluateEffectNetworkLifecycle({ findings, allowlist: [] });

  assert.deepEqual(findings.map(({ triggerChain, classification }) => ({ triggerChain, classification })), [
    {
      triggerChain: 'loadItems>first>second>removeItem(client.delete)',
      classification: 'write-command',
    },
  ]);
  assert.deepEqual(result.diagnostics.map(({ code }) => code), ['PLATFORM_QUERY_LOAD_WRITE_COMMAND']);
});

test('rejects a useCallback write behind an aliased platform query hook and shorthand load', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'modules/example/src/AliasedQueryProbe.tsx',
    source: `import { useCallback } from 'react';
      import { usePlatformQuery as useQuery } from '@icp-platform/core';

      export function AliasedQueryProbe({ dataAdapter }) {
        const load = useCallback(() => dataAdapter.update('Item_1', {}), [dataAdapter]);
        return useQuery({ key: ['example.items'], load });
      }
    `,
  });

  const result = evaluateEffectNetworkLifecycle({ findings, allowlist: [] });

  assert.deepEqual(findings.map(({ hook, triggerChain, classification }) => ({ hook, triggerChain, classification })), [
    {
      hook: 'usePlatformQuery.load',
      triggerChain: 'load>dataAdapter.update',
      classification: 'write-command',
    },
  ]);
  assert.deepEqual(result.diagnostics.map(({ code }) => code), ['PLATFORM_QUERY_LOAD_WRITE_COMMAND']);
});

test('fails closed for a computed HTTP method in a platform query load method', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'modules/example/src/DynamicMethodQueryProbe.tsx',
    source: `import { usePlatformQuery } from '@icp-platform/core';
      import { platformFetch } from '@icp-platform/core';

      export function DynamicMethodQueryProbe({ method }) {
        return usePlatformQuery({
          key: ['example.items'],
          load() {
            return platformFetch('/api/items', { method });
          },
        });
      }
    `,
  });

  const result = evaluateEffectNetworkLifecycle({ findings, allowlist: [] });

  assert.deepEqual(findings.map(({ hook, triggerChain, classification }) => ({ hook, triggerChain, classification })), [
    { hook: 'usePlatformQuery.load', triggerChain: 'platformFetch', classification: 'unknown-network' },
  ]);
  assert.deepEqual(result.diagnostics.map(({ code }) => code), ['PLATFORM_QUERY_LOAD_UNCLASSIFIED_NETWORK']);
});

test('fails closed for an unclassified client sink behind a namespace platform query hook', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'modules/example/src/OpaqueClientQueryProbe.tsx',
    source: `import * as Platform from '@icp-platform/core';

      export function OpaqueClientQueryProbe({ inventoryClient }) {
        return Platform.usePlatformQuery({
          key: ['example.inventory'],
          load: () => inventoryClient.run(),
        });
      }
    `,
  });

  const result = evaluateEffectNetworkLifecycle({ findings, allowlist: [] });

  assert.deepEqual(findings.map(({ hook, triggerChain, classification }) => ({ hook, triggerChain, classification })), [
    {
      hook: 'usePlatformQuery.load',
      triggerChain: 'inventoryClient.run',
      classification: 'unknown-network',
    },
  ]);
  assert.deepEqual(result.diagnostics.map(({ code }) => code), ['PLATFORM_QUERY_LOAD_UNCLASSIFIED_NETWORK']);
});

test('classifies a client action in a platform query load as a write command', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'modules/example/src/ActionQueryProbe.tsx',
    source: `import { usePlatformQuery } from '@icp-platform/core';

      export function ActionQueryProbe({ client }) {
        return usePlatformQuery({
          key: ['example.items'],
          load: () => client.action('Item_1'),
        });
      }
    `,
  });

  const result = evaluateEffectNetworkLifecycle({ findings, allowlist: [] });

  assert.deepEqual(findings.map(({ triggerChain, classification }) => ({ triggerChain, classification })), [
    { triggerChain: 'client.action', classification: 'write-command' },
  ]);
  assert.deepEqual(result.diagnostics.map(({ code }) => code), ['PLATFORM_QUERY_LOAD_WRITE_COMMAND']);
});

test('allows known query reads and ignores an unrelated object load property', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'modules/example/src/QueryReadProbe.tsx',
    source: `import { usePlatformQuery, platformFetch } from '@icp-platform/core';

      const unrelated = {
        load: () => platformFetch('/api/items', { method: 'DELETE' }),
      };

      export function QueryReadProbe({ dataAdapter, lookupClient, lookupProvider }) {
        return usePlatformQuery({
          key: ['example.items'],
          load: async () => {
            await dataAdapter.list({});
            await dataAdapter.get('Item_1');
            await platformFetch('/api/items', { method: 'GET' });
            await platformFetch('/api/items', { method: 'HEAD' });
            await lookupClient.lookup({});
            return lookupProvider.load({});
          },
        });
      }
    `,
  });

  const result = evaluateEffectNetworkLifecycle({ findings, allowlist: [] });

  assert.deepEqual(findings.map(({ triggerChain, classification }) => ({ triggerChain, classification })), [
    { triggerChain: 'dataAdapter.list', classification: 'read-query' },
    { triggerChain: 'dataAdapter.get', classification: 'read-query' },
    { triggerChain: 'platformFetch#1', classification: 'read-query' },
    { triggerChain: 'platformFetch#2', classification: 'read-query' },
    { triggerChain: 'lookupClient.lookup', classification: 'read-query' },
    { triggerChain: 'lookupProvider.load', classification: 'read-query' },
  ]);
  assert.deepEqual(result, { ok: true, diagnostics: [] });
});

test('reports only approved shared-loader entrypoints from loader modules', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'packages/shell/src/BootstrapProbe.tsx',
    source: `import { useEffect } from 'react';
      import { loadRuntimeMetadata, getFallbackRuntimeMetadata } from './metadata/runtimeMetadataLoader';
      import { loadRuntimeLocalizationManifest } from './localization/runtimeLocalizationManifest';

      export function BootstrapProbe() {
        useEffect(() => {
          void loadRuntimeMetadata();
          void loadRuntimeLocalizationManifest();
          getFallbackRuntimeMetadata();
        }, []);
        return null;
      }
    `,
  });

  assert.deepEqual(findings.map(({ triggerChain, classification }) => ({ triggerChain, classification })), [
    { triggerChain: 'loadRuntimeMetadata', classification: 'read-query' },
    { triggerChain: 'loadRuntimeLocalizationManifest', classification: 'read-query' },
  ]);
});

test('never permits a write command through the allowlist', () => {
  const finding = {
    id: 'modules/platform/identity/src/Command.tsx::Command::useEffect::client.create',
    file: 'modules/platform/identity/src/Command.tsx',
    enclosingFunction: 'Command',
    hook: 'useEffect',
    triggerChain: 'client.create',
    classification: 'write-command',
    line: 4,
  };
  const allowlistEntry = {
    ...finding,
    owner: 'Identity',
    reason: 'must not bypass the command policy',
    exitCondition: 'remove the effect command',
  };

  const result = evaluateEffectNetworkLifecycle({ findings: [finding], allowlist: [allowlistEntry] });

  assert.equal(result.ok, false);
  assert.deepEqual(result.diagnostics.map(({ code }) => code), [
    'EFFECT_NETWORK_WRITE_COMMAND',
    'EFFECT_NETWORK_ALLOWLIST_WRITE_FORBIDDEN',
  ]);
});

test('never admits a platform query load into the effect allowlist', () => {
  const finding = {
    id: 'modules/example/src/Query.tsx::Query::usePlatformQuery.load::dataAdapter.list',
    file: 'modules/example/src/Query.tsx',
    enclosingFunction: 'Query',
    hook: 'usePlatformQuery.load',
    triggerChain: 'dataAdapter.list',
    classification: 'read-query',
    line: 4,
  };
  const allowlistEntry = {
    ...finding,
    owner: 'Example',
    reason: 'query reads do not need an effect exception',
    exitCondition: 'remove the invalid entry',
  };

  const result = evaluateEffectNetworkLifecycle({ findings: [finding], allowlist: [allowlistEntry] });

  assert.equal(result.ok, false);
  assert.deepEqual(result.diagnostics.map(({ code }) => code), [
    'EFFECT_NETWORK_ALLOWLIST_QUERY_LOAD_FORBIDDEN',
  ]);
});

test('fails closed when a read or unknown network finding is not allowlisted', () => {
  const makeFinding = (classification) => ({
    id: `packages/ui/src/Probe.tsx::Probe::useEffect::request>${classification}`,
    file: 'packages/ui/src/Probe.tsx',
    enclosingFunction: 'Probe',
    hook: 'useEffect',
    triggerChain: `request>${classification}`,
    classification,
    line: 4,
  });

  const result = evaluateEffectNetworkLifecycle({
    findings: [makeFinding('read-query'), makeFinding('unknown-network')],
    allowlist: [],
  });

  assert.equal(result.ok, false);
  assert.deepEqual(result.diagnostics.map(({ code }) => code), [
    'EFFECT_NETWORK_NOT_ALLOWLISTED',
    'EFFECT_NETWORK_NOT_ALLOWLISTED',
  ]);
});

test('rejects an allowlist entry that no longer matches a finding', () => {
  const result = evaluateEffectNetworkLifecycle({
    findings: [],
    allowlist: [{
      id: 'packages/ui/src/Old.tsx::Old::useEffect::request',
      file: 'packages/ui/src/Old.tsx',
      enclosingFunction: 'Old',
      hook: 'useEffect',
      triggerChain: 'request',
      classification: 'read-query',
      owner: 'UI',
      reason: 'legacy request path',
      exitCondition: 'remove the path',
    }],
  });

  assert.deepEqual(result.diagnostics.map(({ code }) => code), [
    'EFFECT_NETWORK_ALLOWLIST_STALE',
  ]);
});

test('rejects duplicate allowlist ids and missing ownership metadata', () => {
  const finding = {
    id: 'packages/ui/src/Probe.tsx::Probe::useEffect::request',
    file: 'packages/ui/src/Probe.tsx',
    enclosingFunction: 'Probe',
    hook: 'useEffect',
    triggerChain: 'request',
    classification: 'read-query',
    line: 4,
  };
  const validEntry = {
    ...finding,
    owner: 'UI',
    reason: 'legacy request compatibility',
    exitCondition: 'remove the request effect',
  };
  const missingMetadata = { ...validEntry, owner: '' };

  const result = evaluateEffectNetworkLifecycle({
    findings: [finding],
    allowlist: [missingMetadata, validEntry],
  });

  assert.deepEqual(result.diagnostics.map(({ code }) => code), [
    'EFFECT_NETWORK_ALLOWLIST_DUPLICATE',
    'EFFECT_NETWORK_ALLOWLIST_INVALID',
  ]);
});

test('scans production TypeScript under packages, modules, and templates only', (t) => {
  const frontendRoot = mkdtempSync(path.join(tmpdir(), 'newp-effect-gate-'));
  t.after(() => rmSync(frontendRoot, { recursive: true, force: true }));
  const write = (relativePath, source) => {
    const target = path.join(frontendRoot, relativePath);
    mkdirSync(path.dirname(target), { recursive: true });
    writeFileSync(target, source);
  };
  const queryEffect = (componentName) => `import { useEffect } from 'react';
    export function ${componentName}({ request }) {
      useEffect(() => { void request(); }, []);
      return null;
    }`;
  write('packages/ui/src/PackageProbe.tsx', queryEffect('PackageProbe'));
  write('modules/example/src/__tests__/IgnoredProbe.tsx', queryEffect('IgnoredProbe'));
  write('modules/example/src/IgnoredProbe.test.tsx', queryEffect('IgnoredProbe'));
  write('templates/example/src/TemplateProbe.tsx', queryEffect('TemplateProbe'));
  write('outside/OutsideProbe.tsx', queryEffect('OutsideProbe'));

  const findings = analyzeEffectNetworkLifecycleTree({ frontendRoot });

  assert.deepEqual(findings.map(({ file }) => file), [
    'packages/ui/src/PackageProbe.tsx',
    'templates/example/src/TemplateProbe.tsx',
  ]);
});

test('rejects a production re-adoption of the public SDK legacy lookup runtime', (t) => {
  const frontendRoot = mkdtempSync(path.join(tmpdir(), 'newp-effect-gate-'));
  t.after(() => rmSync(frontendRoot, { recursive: true, force: true }));
  const target = path.join(frontendRoot, 'modules/example/src/LegacyLookupProbe.tsx');
  mkdirSync(path.dirname(target), { recursive: true });
  writeFileSync(target, `import { useStandardCrudLookupRuntime as useLegacyLookups } from '@icp-platform/sdk';
    export function LegacyLookupProbe(input) {
      useLegacyLookups(input);
      return null;
    }`);

  const result = runEffectNetworkLifecycleGate({ frontendRoot, allowlist: [] });

  assert.equal(result.ok, false);
  assert.deepEqual(result.diagnostics.map(({ code }) => code), [
    'COMPATIBILITY_ONLY_SDK_LOOKUP_RUNTIME_READOPTED',
  ]);
});

test('rejects a production SearchTablePage adoption without controlledResult', (t) => {
  const frontendRoot = mkdtempSync(path.join(tmpdir(), 'newp-effect-gate-'));
  t.after(() => rmSync(frontendRoot, { recursive: true, force: true }));
  const target = path.join(frontendRoot, 'modules/example/src/UncontrolledTableProbe.tsx');
  mkdirSync(path.dirname(target), { recursive: true });
  writeFileSync(target, `import { SearchTablePage as LegacyTable } from '@icp-platform/ui/crud-list';
    export function UncontrolledTableProbe({ request }) {
      return <LegacyTable rowKey="id" columns={[]} request={request} />;
    }`);

  const result = runEffectNetworkLifecycleGate({ frontendRoot, allowlist: [] });

  assert.equal(result.ok, false);
  assert.deepEqual(result.diagnostics.map(({ code }) => code), [
    'COMPATIBILITY_ONLY_SEARCH_TABLE_UNCONTROLLED_READOPTED',
  ]);
});

test('rejects a production AuthorizationProvider adoption without controlled feature positions', (t) => {
  const frontendRoot = mkdtempSync(path.join(tmpdir(), 'newp-effect-gate-'));
  t.after(() => rmSync(frontendRoot, { recursive: true, force: true }));
  const target = path.join(frontendRoot, 'templates/example/src/UncontrolledAuthorizationProbe.tsx');
  mkdirSync(path.dirname(target), { recursive: true });
  writeFileSync(target, `import * as Platform from '@icp-platform/core';
    export function UncontrolledAuthorizationProbe() {
      return <Platform.AuthorizationProvider isAuthenticated>{null}</Platform.AuthorizationProvider>;
    }`);

  const result = runEffectNetworkLifecycleGate({ frontendRoot, allowlist: [] });

  assert.equal(result.ok, false);
  assert.deepEqual(result.diagnostics.map(({ code }) => code), [
    'COMPATIBILITY_ONLY_AUTHORIZATION_UNCONTROLLED_READOPTED',
  ]);
});

test('rejects explicit undefined values that keep compatibility-only JSX paths uncontrolled', (t) => {
  const frontendRoot = mkdtempSync(path.join(tmpdir(), 'newp-effect-gate-'));
  t.after(() => rmSync(frontendRoot, { recursive: true, force: true }));
  const target = path.join(frontendRoot, 'modules/example/src/UndefinedControlledValuesProbe.tsx');
  mkdirSync(path.dirname(target), { recursive: true });
  writeFileSync(target, `import { AuthorizationProvider } from '@icp-platform/core';
    import { SearchTablePage } from '@icp-platform/ui/crud-list';
    export function UndefinedControlledValuesProbe({ request }) {
      return (
        <AuthorizationProvider featurePositions={undefined}>
          <SearchTablePage
            rowKey="id"
            columns={[]}
            request={request}
            controlledResult={undefined}
          />
        </AuthorizationProvider>
      );
    }`);

  const result = runEffectNetworkLifecycleGate({ frontendRoot, allowlist: [] });

  assert.equal(result.ok, false);
  assert.deepEqual(result.compatibilityDiagnostics.map(({ code }) => code), [
    'COMPATIBILITY_ONLY_AUTHORIZATION_UNCONTROLLED_READOPTED',
    'COMPATIBILITY_ONLY_SEARCH_TABLE_UNCONTROLLED_READOPTED',
  ]);
});

test('fails closed for unprovable controlled expressions while recognizing JSX shorthand values', (t) => {
  const frontendRoot = mkdtempSync(path.join(tmpdir(), 'newp-effect-gate-'));
  t.after(() => rmSync(frontendRoot, { recursive: true, force: true }));
  const unknownTarget = path.join(frontendRoot, 'modules/example/src/UnknownControlledValuesProbe.tsx');
  mkdirSync(path.dirname(unknownTarget), { recursive: true });
  writeFileSync(unknownTarget, `import { AuthorizationProvider } from '@icp-platform/core';
    import { SearchTablePage } from '@icp-platform/ui/crud-list';
    export function UnknownControlledValuesProbe({ request, featurePositions, controlledResult }) {
      return (
        <AuthorizationProvider featurePositions={featurePositions}>
          <SearchTablePage rowKey="id" columns={[]} request={request} controlledResult={controlledResult} />
        </AuthorizationProvider>
      );
    }`);
  const shorthandTarget = path.join(frontendRoot, 'templates/example/src/ShorthandControlledValuesProbe.tsx');
  mkdirSync(path.dirname(shorthandTarget), { recursive: true });
  writeFileSync(shorthandTarget, `import { AuthorizationProvider } from '@icp-platform/core';
    import { SearchTablePage } from '@icp-platform/ui/crud-list';
    export function ShorthandControlledValuesProbe({ request }) {
      return (
        <AuthorizationProvider featurePositions>
          <SearchTablePage rowKey="id" columns={[]} request={request} controlledResult />
        </AuthorizationProvider>
      );
    }`);

  const result = runEffectNetworkLifecycleGate({ frontendRoot, allowlist: [] });

  assert.deepEqual(result.compatibilityDiagnostics.map(({ code }) => code), [
    'COMPATIBILITY_ONLY_AUTHORIZATION_UNCONTROLLED_READOPTED',
    'COMPATIBILITY_ONLY_SEARCH_TABLE_UNCONTROLLED_READOPTED',
  ]);
});

test('allows the Shell private lookup hook and statically controlled table and authorization adopters', (t) => {
  const frontendRoot = mkdtempSync(path.join(tmpdir(), 'newp-effect-gate-'));
  t.after(() => rmSync(frontendRoot, { recursive: true, force: true }));
  const target = path.join(frontendRoot, 'packages/shell/src/ControlledAdoptersProbe.tsx');
  mkdirSync(path.dirname(target), { recursive: true });
  writeFileSync(target, `import { AuthorizationProvider } from '@icp-platform/core';
    import { CrudTableSection } from '@icp-platform/ui/crud';
    import { useStandardCrudLookupRuntime } from './standard-crud/standardCrudLookupRuntime';
    export function ControlledAdoptersProbe({ input, request, maybeFeaturePositions }) {
      useStandardCrudLookupRuntime(input);
      return (
        <AuthorizationProvider featurePositions={maybeFeaturePositions ?? []}>
          <CrudTableSection
            rowKey="id"
            columns={[]}
            request={request}
            controlledResult={{
              data: [],
              total: 0,
              loading: false,
              hasLoaded: true,
              error: null,
              onRetry: () => undefined,
            }}
            onRefresh={() => undefined}
          />
        </AuthorizationProvider>
      );
    }`);

  const result = runEffectNetworkLifecycleGate({ frontendRoot, allowlist: [] });

  assert.deepEqual(result.compatibilityDiagnostics, []);
  assert.equal(result.ok, true);
});

test('rejects retired permissions as a substitute for controlled feature positions', (t) => {
  const frontendRoot = mkdtempSync(path.join(tmpdir(), 'newp-effect-gate-'));
  t.after(() => rmSync(frontendRoot, { recursive: true, force: true }));
  const target = path.join(frontendRoot, 'packages/shell/src/RetiredPermissionsProbe.tsx');
  mkdirSync(path.dirname(target), { recursive: true });
  writeFileSync(target, `import { AuthorizationProvider } from '@icp-platform/core';
    export function RetiredPermissionsProbe() {
      return <AuthorizationProvider permissions={[]}>{null}</AuthorizationProvider>;
    }`);

  const result = runEffectNetworkLifecycleGate({ frontendRoot, allowlist: [] });

  assert.equal(result.ok, false);
  assert.deepEqual(result.compatibilityDiagnostics.map(({ code }) => code), [
    'COMPATIBILITY_ONLY_AUTHORIZATION_UNCONTROLLED_READOPTED',
  ]);
});

test('matches the reviewed current-tree baseline with no effect write commands', () => {
  const frontendRoot = path.resolve(import.meta.dirname, '..');
  const findings = analyzeEffectNetworkLifecycleTree({ frontendRoot });
  const result = evaluateEffectNetworkLifecycle({ findings, allowlist: effectNetworkLifecycleAllowlist });
  const gateResult = runEffectNetworkLifecycleGate({ frontendRoot });
  const effectFindings = findings.filter(({ hook }) => hook !== 'usePlatformQuery.load');
  const queryLoadFindings = findings.filter(({ hook }) => hook === 'usePlatformQuery.load');

  assert.equal(findings.filter(({ classification }) => classification === 'write-command').length, 0);
  assert.deepEqual(effectFindings.map(({ id }) => id).sort(), effectNetworkLifecycleAllowlist.map(({ id }) => id).sort());
  assert.deepEqual(queryLoadFindings.map(({ id, classification }) => ({ id, classification })), [
    {
      id: 'modules/platform/identity/src/standard-crud/IdentityAccountSecurityEventsSummary.tsx::query::usePlatformQuery.load::listAccountSecurityEvents',
      classification: 'read-query',
    },
    {
      id: 'packages/shell/src/pages/StandardCrudListPageHost.tsx::metadataListQuery::usePlatformQuery.load::dataAdapter.list',
      classification: 'read-query',
    },
    {
      id: 'packages/shell/src/pages/StandardCrudListPageHost.tsx::runtimeColumnsQuery::usePlatformQuery.load::provider.load',
      classification: 'read-query',
    },
    {
      id: 'packages/shell/src/pages/StandardCrudListPageHost.tsx::runtimeRowsQuery::usePlatformQuery.load::dataAdapter.list',
      classification: 'read-query',
    },
    {
      id: 'packages/shell/src/standard-crud/standardCrudDetailQuery.ts::query::usePlatformQuery.load::dataAdapter.get',
      classification: 'read-query',
    },
    {
      id: 'packages/shell/src/standard-crud/standardCrudLookupRuntime.ts::query::usePlatformQuery.load::loadLookupQuery>loadStandardCrudLookupPage',
      classification: 'read-query',
    },
    {
      id: 'packages/ui/src/ObjectAuthorizationInput.tsx::query::usePlatformQuery.load::read>fetchObjectAuthorization',
      classification: 'read-query',
    },
  ]);
  assert.deepEqual(result, { ok: true, diagnostics: [] });
  assert.deepEqual(gateResult.compatibilityDiagnostics, []);
  assert.equal(
    effectFindings.some(({ triggerChain }) => triggerChain.includes('metadataListQuery') || triggerChain.includes('runtime.reset')),
    false,
  );
});

test('runs as a standalone current-tree gate', () => {
  const frontendRoot = path.resolve(import.meta.dirname, '..');
  const result = spawnSync(process.execPath, [path.join(import.meta.dirname, 'check-effect-network-lifecycle.mjs')], {
    cwd: frontendRoot,
    encoding: 'utf8',
  });

  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /6 reviewed effect findings, 7 platform query loads, 0 write commands/);
});

test('keeps repeated sink call sites distinct without using line numbers in ids', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'packages/ui/src/RepeatedRequestProbe.tsx',
    source: `import { useEffect } from 'react';
      export function RepeatedRequestProbe({ request }) {
        useEffect(() => {
          void request({ page: 1 });
          void request({ page: 2 });
        }, [request]);
        return null;
      }
    `,
  });

  assert.deepEqual(findings.map(({ triggerChain, id }) => ({ triggerChain, id })), [
    {
      triggerChain: 'request#1',
      id: 'packages/ui/src/RepeatedRequestProbe.tsx::RepeatedRequestProbe::useEffect::request#1',
    },
    {
      triggerChain: 'request#2',
      id: 'packages/ui/src/RepeatedRequestProbe.tsx::RepeatedRequestProbe::useEffect::request#2',
    },
  ]);
});

test('recognizes aliased platformFetch and browser-global fetch transports', () => {
  const findings = analyzeEffectNetworkLifecycleSource({
    file: 'packages/core/src/GlobalTransportProbe.tsx',
    source: `import { useEffect } from 'react';
      import { platformFetch as transport } from '@icp-platform/core';
      export function GlobalTransportProbe() {
        useEffect(() => {
          void transport('/api/one');
          void globalThis.fetch('/api/two');
          void window.fetch('/api/three');
        }, []);
        return null;
      }
    `,
  });

  assert.deepEqual(findings.map(({ triggerChain, classification }) => ({ triggerChain, classification })), [
    { triggerChain: 'transport', classification: 'read-query' },
    { triggerChain: 'globalThis.fetch', classification: 'read-query' },
    { triggerChain: 'window.fetch', classification: 'read-query' },
  ]);
});
