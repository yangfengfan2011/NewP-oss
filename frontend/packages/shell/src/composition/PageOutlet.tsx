import { lazy, Suspense, useCallback, useContext, useEffect, useId, useMemo, useState, type ComponentType } from 'react';
import { ModuleRegistry, type PageInput, type PageLifecycleState, type PageProps, type PageRef, type StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { resolvePageInput } from './compositionRegistry';
import { CompositionContext, EmbeddedCrudContext } from './pageCompositionRuntime';
import { PageComposition } from './PageCompositionContext';
const TreePage = lazy(() => import('./StandardTreePage'));
const TabPage = lazy(() => import('./StandardTabPage'));
const ListPage = lazy(() => import('../pages/StandardCrudListPageHost'));
const DetailPage = lazy(() => import('../pages/StandardCrudDetailPageHost'));

export function PageEntry({ page }: { page: PageRef }) {
  return <PageComposition><PageOutlet page={page} /></PageComposition>;
}
export function PageOutlet({ page, context, close }: { page: PageRef; context?: PageInput['context']; close?: () => void }) {
  const runtime = useContext(CompositionContext);
  const instanceId = useId();
  const reportState = useCallback((state: PageLifecycleState, discard?: () => void) => runtime?.report(instanceId, state, discard), [instanceId, runtime]);
  useEffect(() => () => runtime?.report(instanceId, null), [instanceId, runtime]);
  const resolved = useMemo(() => {
    try { return { input: resolvePageInput(page, ModuleRegistry.getAllModules(), context) }; }
    catch (error) { return { error: error instanceof Error ? error.message : String(error) }; }
  }, [context, page]);
  if (!resolved.input) return <div role="alert">页面配置／上下文错误：{resolved.error}</div>;
  const props: PageProps = { input: resolved.input, reportState };
  return <Suspense fallback={<div role="status">正在加载页面…</div>}>
    {page.pageKey === 'platform.tree' ? <TreePage {...props} />
      : page.pageKey === 'platform.tabs' ? <TabPage {...props} />
        : page.pageKey === 'platform.list' || page.pageKey === 'platform.detail'
          ? <StandardPage pageKey={page.pageKey} {...props} close={close} />
          : <CustomPage pageKey={page.pageKey} {...props} />}
  </Suspense>;
}
function CustomPage({ pageKey, ...props }: PageProps & { pageKey: string }) {
  const [component, setComponent] = useState<ComponentType<PageProps> | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let active = true;
    const binding = ModuleRegistry.getAllModules().flatMap(module => module.pages ?? []).find(page => page.pageKey === pageKey)!;
    void binding.load().then(loaded => { if (active) setComponent(() => loaded.default); }, failure => { if (active) setError(String(failure)); });
    return () => { active = false; };
  }, [pageKey]);
  const Content = component;
  return error ? <div role="alert">{error}</div> : Content ? <Content {...props} /> : <div role="status">正在加载页面…</div>;
}
function StandardPage({ pageKey, input, reportState, close }: PageProps & { pageKey: string; close?: () => void }) {
  const metadata = useRuntimeMetadata();
  const runtime = useContext(CompositionContext)!;
  const viewKey = String(input.parameters.viewKey);
  const instanceId = useId();
  const [refreshVersion, setRefreshVersion] = useState(0);
  const [registration, setRegistration] = useState<StandardCrudResourceRegistration | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let active = true;
    setRegistration(null); setError(null);
    const listKey = pageKey === 'platform.list' ? viewKey
      : metadata?.viewDocuments.find(document => document.view.mode === 'list' && document.view.detailViewKey === viewKey)?.view.key;
    const modules = ModuleRegistry.getAllModules();
    const registered = modules.flatMap(module => module.standardCrudResources ?? []).find(resource => resource.listViewKey === listKey);
    const binding = modules.flatMap(module => module.standardCrudCapabilities ?? []).find(item => item.kind === 'standard-list' && item.targetKey === listKey);
    const load = registered ? Promise.resolve({ default: registered }) : binding?.load();
    if (!load) setError('未找到标准页面资源绑定');
    else void load.then(result => { if (active) setRegistration(result.default); }, failure => { if (active) setError(String(failure)); });
    return () => { active = false; };
  }, [metadata, pageKey, viewKey]);
  useEffect(() => runtime.subscribe(instanceId, async change => {
    if (change.resourceKey === registration?.resourceKey) setRefreshVersion(version => version + 1);
  }), [instanceId, registration?.resourceKey, runtime]);
  const bridge = useMemo(() => ({ input, reportState, committed: runtime.committed, refreshVersion, close }), [close, input, refreshVersion, reportState, runtime.committed]);
  if (error) return <div role="alert">{error}</div>;
  if (!registration) return <div role="status">正在加载标准资源…</div>;
  return <EmbeddedCrudContext.Provider value={bridge}>
    {pageKey === 'platform.list' ? <ListPage viewKey={viewKey} registration={registration} />
      : <DetailPage viewKey={viewKey} id={input.recordId ?? null} registration={registration} />}
  </EmbeddedCrudContext.Provider>;
}
