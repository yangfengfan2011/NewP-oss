import { lazy, Suspense, useCallback, useContext, useEffect, useId, useMemo, useRef, useState, type ComponentType } from 'react';
import { ModuleRegistry, type PageInput, type PageLifecycleState, type PageProps, type PageRef, type StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { useLocation, useNavigate } from 'react-router-dom';
import { PageHeader } from '@icp-platform/ui/core';
import { useLocalization } from '@icp-platform/core';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { resolvePageInput } from './compositionRegistry';
import { CompositionContext, EmbeddedCrudContext, PageNavigationContext, PageActivityContext, TreeInstanceContext, TreeNavigationContext, type TreeNavigation } from './pageCompositionRuntime';
import { PageComposition } from './PageCompositionContext';
const TreePage = lazy(() => import('./StandardTreePage'));
const TabPage = lazy(() => import('./StandardTabPage'));
const ListPage = lazy(() => import('../pages/StandardCrudListPageHost'));
const DetailPage = lazy(() => import('../pages/StandardCrudDetailPageHost'));

export function PageEntry({ page }: { page: PageRef }) {
  const location = useLocation();
  const navigate = useNavigate();
  const metadata = useRuntimeMetadata();
  const { t } = useLocalization();
  const view = metadata?.viewDocuments.find(document => document.view.key === page.parameters?.viewKey)?.view;
  const navigation = { tabKey: new URLSearchParams(location.search).get('tab'), selectTab: (key: string) => {
    const query = new URLSearchParams(location.search); query.set('tab', key);
    navigate({ pathname: location.pathname, search: query.toString() });
  } };
  const treeNavigation: TreeNavigation = { nodeKey: new URLSearchParams(location.search).get('node'), nodeTab: new URLSearchParams(location.search).get('nodeTab'), locate: (node, tab) => {
    const query = new URLSearchParams(location.search);
    if (node) query.set('node', node); else query.delete('node');
    if (tab) query.set('nodeTab', tab); else if (tab === null) query.delete('nodeTab');
    if (query.toString() !== new URLSearchParams(location.search).toString()) navigate({ pathname: location.pathname, search: query.toString() }, { replace: true });
  } };
  return <PageComposition><div className="sh-page">
    {view && <PageHeader title={t(`${view.dicKey}.${view.titleKey}`, String(view.titleKey ?? ''))} />}
    <PageOutlet page={page} navigation={navigation} treeNavigation={treeNavigation} />
  </div></PageComposition>;
}
export function PageOutlet({ page, context, close, navigation, treeNavigation, active = true }: { page: PageRef; context?: PageInput['context']; close?: () => void; navigation?: React.ContextType<typeof PageNavigationContext>; treeNavigation?: TreeNavigation | null; active?: boolean }) {
  const runtime = useContext(CompositionContext);
  const parentActive = useContext(PageActivityContext);
  const instanceId = useId();
  const reportState = useCallback((state: PageLifecycleState, discard?: () => void) => runtime?.report(instanceId, state, discard), [instanceId, runtime]);
  useEffect(() => () => runtime?.report(instanceId, null), [instanceId, runtime]);
  const resolved = useMemo(() => {
    try { return { input: resolvePageInput(page, ModuleRegistry.getAllModules(), context) }; }
    catch (error) { return { error: error instanceof Error ? error.message : String(error) }; }
  }, [context, page]);
  if (!resolved.input) return <div role="alert">页面配置／上下文错误：{resolved.error}</div>;
  const props: PageProps = { input: resolved.input, reportState, active: parentActive && active };
  return <PageActivityContext.Provider value={props.active!}><TreeNavigationContext.Provider value={treeNavigation ?? null}><PageNavigationContext.Provider value={navigation ?? null}><Suspense fallback={<div role="status">正在加载页面…</div>}>
    {page.pageKey === 'platform.tree' ? <TreePage {...props} />
      : page.pageKey === 'platform.tabs' ? <TabPage {...props} />
        : page.pageKey === 'platform.list' || page.pageKey === 'platform.detail'
          ? <StandardPage pageKey={page.pageKey} {...props} close={close} />
          : <CustomPage pageKey={page.pageKey} {...props} />}
  </Suspense></PageNavigationContext.Provider></TreeNavigationContext.Provider></PageActivityContext.Provider>;
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
function StandardPage({ pageKey, input, reportState, close, active }: PageProps & { pageKey: string; close?: () => void }) {
  const metadata = useRuntimeMetadata();
  const runtime = useContext(CompositionContext)!;
  const viewKey = String(input.parameters.viewKey);
  const instanceId = useId();
  const [refreshVersion, setRefreshVersion] = useState(0);
  const originTreeId = useContext(TreeInstanceContext);
  const pendingRefresh = useRef(false);
  const [lifecycle, setLifecycle] = useState<PageLifecycleState>({ dirty: false, submitting: false });
  const report = useCallback((state: PageLifecycleState, discard?: () => void) => {
    setLifecycle(current => current.dirty === state.dirty && current.submitting === state.submitting ? current : state);
    reportState(state, discard);
  }, [reportState]);
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
    if (change.resourceKey !== registration?.resourceKey) return;
    if (!active || lifecycle.dirty || lifecycle.submitting) pendingRefresh.current = true;
    else setRefreshVersion(version => version + 1);
  }), [active, instanceId, lifecycle, registration?.resourceKey, runtime]);
  useEffect(() => {
    if (active && !lifecycle.dirty && !lifecycle.submitting && pendingRefresh.current) {
      pendingRefresh.current = false; setRefreshVersion(version => version + 1);
    }
  }, [active, lifecycle]);
  const committed = useCallback((change: import('@icp-platform/sdk').ResourceChange) => runtime.committed({ ...change, originTreeId }), [originTreeId, runtime]);
  const bridge = useMemo(() => ({ input, instanceId, active, reportState: report, committed, refreshVersion, close }), [active, close, committed, input, instanceId, refreshVersion, report]);
  if (error) return <div role="alert">{error}</div>;
  if (!registration) return <div role="status">正在加载标准资源…</div>;
  return <EmbeddedCrudContext.Provider value={bridge}>
    {pageKey === 'platform.list' ? <ListPage viewKey={viewKey} registration={registration} />
      : <DetailPage viewKey={viewKey} id={input.recordId ?? null} registration={registration} />}
  </EmbeddedCrudContext.Provider>;
}
