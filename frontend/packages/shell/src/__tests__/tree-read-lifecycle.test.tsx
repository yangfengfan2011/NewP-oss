// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, onTestFinished, vi } from 'vitest';
import { TreeNodeNotFoundError, type TreeNodeDescriptor, type TreeProviderBinding } from '@icp-platform/sdk';
import type { CompositionChange } from '../composition/pageCompositionRuntime';
import { useTreeReadLifecycle } from '../composition/useTreeReadLifecycle';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const node = (nodeKey: string, nodeType = 'node'): TreeNodeDescriptor => ({
  nodeKey, nodeType, label: nodeKey, hasChildren: false,
});

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(complete => { resolve = complete; });
  return { promise, resolve };
}

function providerWith(nodes: TreeNodeDescriptor[]): TreeProviderBinding {
  return {
    key: 'tree',
    roots: async () => nodes,
    children: async () => [],
    path: async (_scope, key) => nodes.filter(item => item.nodeKey === key),
    search: async () => ({ items: [], total: 0, page: 1, pageSize: 20 }),
    resolveChange: async () => ({ kind: 'unrelated' }),
  };
}

async function renderLifecycle(provider: TreeProviderBinding) {
  let current!: ReturnType<typeof useTreeReadLifecycle>;
  let reader: ((change: CompositionChange) => Promise<void>) | undefined;
  let proceed: (() => void) | undefined;
  let blocked = false;
  const navigation = { nodeKey: null as string | null, nodeTab: null as string | null, locate: vi.fn() };
  const onSelectAccepted = vi.fn();
  const runtime = {
    leave: (action: () => void) => { if (blocked) proceed = action; else action(); },
    subscribe: (_id: string, read: (change: CompositionChange) => Promise<void>) => {
      reader = read;
      return () => { if (reader === read) reader = undefined; };
    },
  };
  let options = { provider, scope: {}, instanceId: 'tree-instance', active: true, runtime, navigation, onSelectAccepted };
  function Harness() { current = useTreeReadLifecycle(options); return null; }
  const container = document.createElement('div');
  const root = createRoot(container);
  onTestFinished(async () => { await act(async () => root.unmount()); });
  await act(async () => root.render(<Harness />));
  return {
    read: () => current,
    navigation,
    onSelectAccepted,
    block: () => { blocked = true; },
    accept: () => { const action = proceed; proceed = undefined; blocked = false; action?.(); },
    publish: (change: CompositionChange) => reader!(change),
    rerender: async (next: Partial<typeof options>) => {
      options = { ...options, ...next };
      await act(async () => root.render(<Harness />));
    },
  };
}

it('changes selection and opens its presentation only after the composition allows leaving', async () => {
  const tree = await renderLifecycle(providerWith([node('a'), node('b', 'other')]));
  const openEditor = vi.fn();
  tree.block();
  act(() => tree.read().select('b', openEditor));
  expect(tree.read().snapshot.selected?.nodeKey).toBe('a');
  expect(openEditor).not.toHaveBeenCalled();
  expect(tree.onSelectAccepted).not.toHaveBeenCalled();

  act(() => tree.accept());
  expect(tree.read().snapshot.selected?.nodeKey).toBe('b');
  expect(tree.navigation.locate).toHaveBeenLastCalledWith('b', null);
  expect(tree.onSelectAccepted).toHaveBeenCalledOnce();
  expect(openEditor).toHaveBeenCalledOnce();
});

it('treats an unreachable location as missing while keeping read failures visible', async () => {
  const provider = providerWith([node('root')]);
  provider.path = async () => { throw new TreeNodeNotFoundError('unreachable'); };
  const tree = await renderLifecycle(provider);
  await act(async () => tree.read().locate('hidden-child'));
  expect(tree.read().snapshot.selected?.nodeKey).toBe('root');
  expect(tree.read().locationNotice).toBe('Platform.TreeNodeMissing');
  expect(tree.read().locationError).toBeNull();

  provider.path = async () => { throw new Error('read plan failed'); };
  await act(async () => tree.read().locate('hidden-child'));
  expect(tree.read().locationError).toBeInstanceOf(Error);
  expect(tree.read().snapshot.selected?.nodeKey).toBe('root');
});

it('cancels synchronization reads when their scope is replaced and does not continue the old path', async () => {
  const branch = deferred<TreeNodeDescriptor[]>();
  const provider = providerWith([node('a')]);
  let readSignal: AbortSignal | undefined;
  provider.children = async (_scope, _key, signal) => { readSignal = signal; return branch.promise; };
  provider.resolveChange = async () => ({
    kind: 'located', operation: 'update', nodeKey: 'a',
    afterLocation: { nodeKey: 'a', parentNodeKey: 'parent' },
  });
  const path = vi.fn(provider.path);
  provider.path = path;
  const tree = await renderLifecycle(provider);
  let finished!: Promise<void>;
  act(() => { finished = tree.publish({ resourceKey: 'resource', operation: 'update', id: 'a' }); });
  await act(async () => undefined);

  await tree.rerender({ provider: providerWith([node('new-scope')]), scope: { name: 'new' } });
  await act(async () => { branch.resolve([node('old-scope')]); await finished; });
  expect(readSignal?.aborted).toBe(true);
  expect(path).not.toHaveBeenCalled();
  expect(tree.read().snapshot.selected?.nodeKey).toBe('new-scope');
  expect(tree.read().snapshot.branches).toEqual({});
  expect(tree.read().error).toBeNull();
});

it('finishes changes received during an active synchronization without needing another event', async () => {
  const firstBranch = deferred<TreeNodeDescriptor[]>();
  const provider = providerWith(['root', 'p1', 'p2', 'p3'].map(key => node(key)));
  let readingFirstBranch = true;
  provider.children = async (_scope, key) => {
    if (key === 'p1' && readingFirstBranch) return firstBranch.promise;
    return key === 'p3' ? [node('d')] : [];
  };
  provider.resolveChange = async change => ({
    kind: 'located', operation: 'update', nodeKey: 'd',
    beforeLocation: { nodeKey: 'd', parentNodeKey: String(change.before?.parent) },
    afterLocation: { nodeKey: 'd', parentNodeKey: String(change.after?.parent) },
  });
  const tree = await renderLifecycle(provider);
  let first!: Promise<void>;
  let second!: Promise<void>;
  act(() => { first = tree.publish({ resourceKey: 'resource', operation: 'update', id: 'd', before: { parent: 'p1' }, after: { parent: 'p2' } }); });
  await act(async () => undefined);
  act(() => { second = tree.publish({ resourceKey: 'resource', operation: 'update', id: 'd', before: { parent: 'p2' }, after: { parent: 'p3' } }); });
  await act(async () => undefined);
  await act(async () => {
    readingFirstBranch = false;
    firstBranch.resolve([]);
    await Promise.all([first, second]);
  });
  expect(tree.read().snapshot.branches).toMatchObject({ p1: [], p2: [], p3: [node('d')] });
  expect(tree.read().snapshot.selected?.nodeKey).toBe('root');
  expect(tree.read().error).toBeNull();
});

it.each(['approval', 'superseded'])('applies a resolved location only while it remains allowed: %s', async scenario => {
  const path = deferred<TreeNodeDescriptor[]>();
  const provider = providerWith([node('a'), node('b'), node('c')]);
  let signal: AbortSignal | undefined;
  provider.path = async (_scope, _key, currentSignal) => { signal = currentSignal; return path.promise; };
  const tree = await renderLifecycle(provider);
  act(() => tree.read().locate('b'));
  if (scenario === 'approval') tree.block();
  else act(() => tree.read().select('c'));

  await act(async () => { path.resolve([node('b')]); });
  expect(tree.read().snapshot.selected?.nodeKey).toBe(scenario === 'approval' ? 'a' : 'c');
  if (scenario === 'approval') {
    expect(tree.onSelectAccepted).not.toHaveBeenCalled();
    act(() => tree.accept());
    expect(tree.read().snapshot.selected?.nodeKey).toBe('b');
  } else {
    expect(signal?.aborted).toBe(true);
  }
  expect(tree.onSelectAccepted).toHaveBeenCalledOnce();
  expect(tree.read().locationError).toBeNull();
});

it.each(['select', 'locate'] as const)('drops an approved %s request belonging to a replaced scope', async operation => {
  const provider = providerWith([node('a'), node('b')]);
  const tree = await renderLifecycle(provider);
  tree.block();
  act(() => tree.read()[operation]('b'));
  await tree.rerender({ provider: providerWith([node('new-scope')]), scope: { name: 'new' } });
  await act(async () => tree.accept());
  expect(tree.read().snapshot.selected?.nodeKey).toBe('new-scope');
  expect(tree.read().locationError).toBeNull();
  expect(tree.onSelectAccepted).not.toHaveBeenCalled();
  expect(tree.navigation.locate).not.toHaveBeenCalled();
});

it('refreshes an active unknown change and adopts the root expandability from the provider', async () => {
  const provider = providerWith([node('root')]);
  const tree = await renderLifecycle(provider);
  const expandableRoot = { ...node('root'), hasChildren: true };
  provider.resolveChange = vi.fn(async () => ({ kind: 'unknown' as const }));
  provider.roots = vi.fn(async () => [expandableRoot]);
  provider.path = vi.fn(async () => [expandableRoot]);
  await act(async () => { await expect(tree.publish({ resourceKey: 'resource', operation: 'create', id: '' })).resolves.toBeUndefined(); });
  expect(provider.roots).toHaveBeenCalledOnce();
  expect(provider.path).toHaveBeenCalledOnce();
  expect(tree.read().snapshot.roots).toEqual([expandableRoot]);
  expect(tree.read().snapshot.selected).toEqual(expandableRoot);
  expect(tree.read().error).toBeNull();
});

it.each(['classification', 'read'])('keeps an active %s failure retryable without replaying the committed change', async failureKind => {
  const provider = providerWith([node('root')]);
  const tree = await renderLifecycle(provider);
  const failure = new Error(`${failureKind} failed`);
  provider.resolveChange = vi.fn(async () => {
    if (failureKind === 'classification') throw failure;
    return { kind: 'unknown' as const };
  });
  provider.roots = vi.fn().mockRejectedValueOnce(failure).mockResolvedValue([node('root')]);
  if (failureKind === 'classification') provider.roots = vi.fn(async () => [node('root')]);
  await act(async () => { await expect(tree.publish({ resourceKey: 'resource', operation: 'create', id: '' })).rejects.toBe(failure); });
  expect(tree.read().error).toEqual({ failure, saved: true });
  await act(async () => tree.read().retry({ kind: 'tree' }));
  expect(tree.read().error).toBeNull();
  expect(provider.resolveChange).toHaveBeenCalledOnce();
  expect(provider.roots).toHaveBeenCalledTimes(failureKind === 'classification' ? 1 : 2);
});

it('defers hidden tree changes without rejecting the visible list commit', async () => {
  const provider = providerWith([node('a')]);
  provider.resolveChange = vi.fn(async () => ({ kind: 'unknown' as const }));
  provider.roots = vi.fn(provider.roots);
  const tree = await renderLifecycle(provider);
  await tree.rerender({ active: false });
  await act(async () => { await expect(tree.publish({ resourceKey: 'resource', operation: 'update', id: 'a' })).resolves.toBeUndefined(); });
  expect(provider.resolveChange).toHaveBeenCalledTimes(1);
  expect(provider.roots).toHaveBeenCalledTimes(1);
  expect(tree.read().error).toBeNull();
  await tree.rerender({ active: true });
  expect(provider.roots).toHaveBeenCalledTimes(2);
  expect(tree.read().error).toBeNull();
});

it('keeps activation read failure local to the tree and retries without another commit', async () => {
  const provider = providerWith([node('a')]);
  provider.resolveChange = vi.fn(async () => { throw new Error('classification failed'); });
  const tree = await renderLifecycle(provider);
  await tree.rerender({ active: false });
  await act(async () => { await expect(tree.publish({ resourceKey: 'resource', operation: 'update', id: 'a' })).resolves.toBeUndefined(); });
  expect(tree.read().error).toBeNull();
  provider.roots = vi.fn().mockRejectedValueOnce(new Error('read failed')).mockResolvedValue([node('a')]);
  await tree.rerender({ active: true });
  expect(tree.read().error).toMatchObject({ saved: true, failure: expect.any(Error) });
  await act(async () => tree.read().retry({ kind: 'tree' }));
  expect(tree.read().error).toBeNull();
  expect(provider.roots).toHaveBeenCalledTimes(2);
  expect(provider.resolveChange).toHaveBeenCalledTimes(1);
});

it.each(['location', 'selection'])('late hidden-tree refresh preserves a newer explicit %s', async action => {
  const nodes = [node('company'), node('person', 'person')];
  const reloaded = deferred<TreeNodeDescriptor[]>();
  const provider = providerWith(nodes);
  const tree = await renderLifecycle(provider);
  await tree.rerender({ active: false });
  provider.resolveChange = async () => ({ kind: 'reload', operation: 'update', nodeKey: 'person' });
  await act(async () => { await tree.publish({ resourceKey: 'resource', operation: 'update', id: 'person' }); });
  provider.roots = async () => reloaded.promise;
  await tree.rerender({ active: true });
  await act(async () => {
    if (action === 'location') tree.read().locate('person');
    else tree.read().select('person');
  });
  expect(tree.read().snapshot.selected?.nodeKey).toBe('person');
  await act(async () => { reloaded.resolve(nodes); });
  expect(tree.read().snapshot.selected?.nodeKey).toBe('person');
});

it('waits for a newer pending location before applying an older refresh', async () => {
  const nodes = [node('company'), node('person', 'person')];
  const reloaded = deferred<TreeNodeDescriptor[]>();
  const located = deferred<TreeNodeDescriptor[]>();
  const provider = providerWith(nodes);
  const tree = await renderLifecycle(provider);
  provider.roots = async () => reloaded.promise;
  provider.path = async (_scope, key) => key === 'person' ? located.promise : [nodes[0]];
  provider.resolveChange = async () => ({ kind: 'reload', operation: 'update', nodeKey: 'person' });
  let finished!: Promise<void>;
  act(() => { finished = tree.publish({ resourceKey: 'resource', operation: 'update', id: 'person' }); });
  await act(async () => undefined);
  act(() => tree.read().locate('person'));
  await act(async () => { reloaded.resolve(nodes); });
  await act(async () => { located.resolve([nodes[1]]); await finished; });
  expect(tree.read().snapshot.selected?.nodeKey).toBe('person');
  expect(tree.navigation.locate).toHaveBeenLastCalledWith('person', undefined);
});
