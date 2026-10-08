import { formatRequestError, useLocalization } from '@icp-platform/core';
import { lazy, Suspense, useCallback, useContext, useEffect, useId, useMemo, useRef, useState, type ComponentType, type CSSProperties } from 'react';
import theme from 'antd/es/theme';
import { ModuleRegistry, type PageInput, type PageLifecycleState, type PageProps, type PageRef, type StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { useLocation, useNavigate } from 'react-router-dom';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { resolvePageInput } from './compositionRegistry';
import { CompositionContext, PageNavigationContext, PageActivityContext, TreeInstanceContext, TreeNavigationContext, type TreeNavigation } from './pageCompositionRuntime';
import { resolveComposedHostPresentation, StandardCrudHostEnvironmentContext, type StandardCrudHostEnvironment } from '../standard-crud/standardCrudHostEnvironment';
import { PageComposition } from './PageCompositionContext';
import { createAddressNavigation } from './compositionNavigation';
import './composition.css';
import { useOptionalAuth } from '../contexts/AuthContext';
import { FeaturePlacementContext, type FeaturePlacement } from '../authorization/FeaturePlacementContext';
const TreePage = lazy(() => import('./StandardTreePage'));
const TabPage = lazy(() => import('./StandardTabPage'));
const ListPage = lazy(() => import('../pages/StandardCrudListPageHost'));
const DetailPage = lazy(() => import('../pages/StandardCrudDetailPageHost'));

export function PageEntry({ page }: { page: PageRef }) {
  const auth = useOptionalAuth();
  const location = useLocation();
  const entryPage = useMemo(() => ({ ...page, parameters: { ...location.state?.pageParameters, ...page.parameters } }), [location.state, page]);
  const navigate = useNavigate();
  const { token } = theme.useToken();
  const { navigation, treeNavigation } = createAddressNavigation(location.search, (search, replace) => {
    navigate({ pathname: location.pathname, search }, { replace });
  });
  // 导航入口切换必须释放旧页面的 dirty/submitting 登记；同一路径的查询参数变化保留组合状态。
  return <PageComposition key={`${auth?.user?.id ?? ''}:${location.pathname}`}><div className="sh-page np-composed-page" style={{
    '--np-composition-surface': token.colorBgContainer,
    '--np-composition-border': token.colorBorderSecondary,
    '--np-composition-secondary': token.colorTextSecondary,
    '--np-composition-radius': `${token.borderRadiusLG}px`,
  } as CSSProperties}>
    <PageOutlet page={entryPage} navigation={navigation} treeNavigation={treeNavigation} />
  </div></PageComposition>;
}
export function PageOutlet({ page, context, close, navigation, treeNavigation, active = true, featurePlacement }: { featurePlacement?: FeaturePlacement; page: PageRef; context?: PageInput['context']; close?: () => void; navigation?: React.ContextType<typeof PageNavigationContext>; treeNavigation?: TreeNavigation | null; active?: boolean }) {
  const { t } = useLocalization();
  const runtime = useContext(CompositionContext);
  const parentActive = useContext(PageActivityContext);
  const inheritedPlacement = useContext(FeaturePlacementContext);
  const placement = useMemo(() => featurePlacement ?? (typeof page.parameters?.featurePosition === "string" ? { entryPosition: page.parameters.featurePosition } : inheritedPlacement), [featurePlacement, inheritedPlacement, page.parameters]);
  const instanceId = useId();
  const reportState = useCallback((state: PageLifecycleState, discard?: () => void, save?: () => Promise<boolean>) => runtime?.report(instanceId, state, discard, save), [instanceId, runtime]);
  useEffect(() => () => runtime?.report(instanceId, null), [instanceId, runtime]);
  const resolved = useMemo(() => {
    try { return { input: resolvePageInput(page, ModuleRegistry.getAllModules(), context) }; }
    catch (error) { return { error: error instanceof Error ? error.message : String(error) }; }
  }, [context, page]);
  if (!resolved.input) return <div role="alert">{t('Platform.PageContextInvalid', '页面配置或上下文无效。')}</div>;
  const props: PageProps = { input: resolved.input, reportState, active: parentActive && active };
  return <FeaturePlacementContext.Provider value={placement}><PageActivityContext.Provider value={props.active!}><TreeNavigationContext.Provider value={treeNavigation ?? null}><PageNavigationContext.Provider value={navigation ?? null}><Suspense fallback={<div role="status">{t('Platform.LoadingPage', '正在加载页面…')}</div>}>
    {page.pageKey === 'platform.tree' ? <TreePage {...props} />
      : page.pageKey === 'platform.tabs' ? <TabPage {...props} />
        : page.pageKey === 'platform.list' || page.pageKey === 'platform.detail'
          ? <StandardPage pageKey={page.pageKey} {...props} close={close} />
          : <CustomPage pageKey={page.pageKey} {...props} />}
  </Suspense></PageNavigationContext.Provider></TreeNavigationContext.Provider></PageActivityContext.Provider></FeaturePlacementContext.Provider>;
}
function CustomPage({ pageKey, ...props }: PageProps & { pageKey: string }) {
  const { t } = useLocalization();
  const [component, setComponent] = useState<ComponentType<PageProps> | null>(null);
  const [error, setError] = useState<unknown>(null);
  useEffect(() => {
    let active = true;
    const binding = ModuleRegistry.getAllModules().flatMap(module => module.pages ?? []).find(page => page.pageKey === pageKey)!;
    void binding.load().then(loaded => { if (active) setComponent(() => loaded.default); }, failure => { if (active) setError(failure); });
    return () => { active = false; };
  }, [pageKey]);
  const Content = component;
  return error != null ? <div role="alert">{formatRequestError(error, t)}</div> : Content ? <Content {...props} /> : <div role="status">{t('Platform.LoadingPage', '正在加载页面…')}</div>;
}
function StandardPage({ pageKey, input, reportState, close, active }: PageProps & { pageKey: string; close?: () => void }) {
  const { t } = useLocalization();
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
  const [error, setError] = useState<unknown>(null);
  useEffect(() => {
    let active = true;
    setRegistration(null); setError(null);
    const listKey = pageKey === 'platform.list' ? viewKey
      : metadata?.viewDocuments.find(document => document.view.mode === 'list' && document.view.detailViewKey === viewKey)?.view.key;
    const modules = ModuleRegistry.getAllModules();
    const registered = modules.flatMap(module => module.standardCrudResources ?? []).find(resource => resource.listViewKey === listKey);
    const binding = modules.flatMap(module => module.standardCrudCapabilities ?? []).find(item => item.kind === 'standard-list' && item.targetKey === listKey);
    const load = registered ? Promise.resolve({ default: registered }) : binding?.load();
    if (!load) setError(new Error('未找到标准页面资源绑定'));
    else void load.then(result => { if (active) setRegistration(result.default); }, failure => { if (active) setError(failure); });
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
  // 组合装载的嵌入 adapter：不拥有地址，生命周期与成功事实经组合运行时交接。
  const environment = useMemo<StandardCrudHostEnvironment>(() => ({
    address: null,
    lifecycle: { active: active !== false, reportState: report, close },
    sync: { committed, refreshVersion },
    input: { instanceId, queryScope: input.queryScope, fixedCreateValues: input.fixedCreateValues, recordId: input.recordId },
    presentation: resolveComposedHostPresentation(input.parameters),
  }), [active, close, committed, input, instanceId, refreshVersion, report]);
  if (error != null) return <div role="alert">{formatRequestError(error, t)}</div>;
  if (!registration) return <div role="status">{t('Platform.LoadingResource', '正在加载标准资源…')}</div>;
  return <StandardCrudHostEnvironmentContext.Provider value={environment}>
    {pageKey === 'platform.list' ? <ListPage viewKey={viewKey} registration={registration} />
      : <DetailPage viewKey={viewKey} id={input.recordId ?? null} registration={registration} />}
  </StandardCrudHostEnvironmentContext.Provider>;
}
